using System;
using System.Threading;

namespace RynthCore.Engine.Compatibility;

// Marshals off-thread bot ACTIONS onto AC's main (game) thread.
//
// AC is single-threaded and non-reentrant. The plugin pump (and chat-command /
// UI threads) run OFF AC's main thread; when they invoke AC state-mutating
// functions directly — combat-mode flips, melee/missile attacks, movement,
// object use — they race AC's own per-tick bookkeeping and corrupt its
// object / range lists. AC then access-violates LATER on its own thread during
// routine teardown / range recompute: the dump-verified object-teardown class
// 0x0055FA24 (List<ObjectRangeInfo>::remove via CPlayerSystem::CalculateObjectRangeChecks)
// and 0x00416C86 (DBOCache::DestroyObj). The per-callsite SEH trampoline only
// CONTAINS an AV that fires inside OUR call — it cannot stop AC tripping over
// corruption we left behind. The only real fix is to not mutate off-thread:
// enqueue here, execute on the main thread.
//
// Drained from the always-on EngineFrameController.OnEndScene tick (AC's main
// thread; ~22-33 Hz; fires regardless of EnableImGuiBackend / idle / combat).
//
// Multi-producer-safe: Enqueue takes a short lock (it runs on engine-owned
// threads, never inside an AC detour, so a lock is fine). Drain is the single
// consumer (the main thread) and is lock-free + zero-alloc, so it never blocks
// inside the reverse-P/Invoke detour and never reintroduces the GC-in-detour
// fail-fast class.
//
// The drain re-invokes the EXISTING public action methods. Each of those
// methods self-gates on MainThreadGuard.IsOnMainThread(): off-thread it
// enqueues here; on the main thread (i.e. when called from Drain, or from any
// legitimately main-thread caller) it executes the AC call directly. So there
// is no separate "Direct" body to keep in sync, and no risk of an enqueue loop.
//
// CASTS: routed via the separate cast slot below (EnqueueCast / DrainCasts),
// drained from AC's GAME-LOGIC tick (Client::UseTime, via GameTickHooks) — NOT
// EndScene. EndScene is the render phase and does not drive AC's cast state machine
// to completion (proven dead-end, reverted 2026-05-19); UseTime is where AC processes
// player actions, so the cast completes there. SelectItem is paired inside
// CombatActionHooks.CastSpell's main-thread path, so it stays ordered with the cast.
internal static class AcMainThreadQueue
{
    internal enum ActionKind : byte
    {
        ChangeCombatMode,
        MeleeAttack,
        MissileAttack,
        DoMovement,
        StopMovement,
        Jump,
        UseObject,
        // Item mutators marshalled 2026-06-05 (off-thread UseObjectOn/MoveItem/stack ops
        // raced AC's per-tick object graph -> null-deref AVs in Client::UseTime, e.g.
        // acclient+0xF24E0; same off-thread class as the P1 UseObject fix).
        UseObjectOn,
        UseEquippedItem,
        MoveItemExternal,
        MoveItemInternal,
        SplitStackInternal,
        MergeStackInternal,
        GiveObjectTo,
        // CommandInterpreter movement marshalled 2026-06-12: SetAutoRun /
        // TurnToHeading / StopCompletely were the last direct off-thread AC
        // mutators (dump-proven: a SetAutoRun executed 15s after a main thread
        // had wedged) — they rewrite the locomotion channels of the SAME
        // CommandInterpreter/motion graph the CSequence-AV class corrupts.
        SetAutoRun,
        TurnToHeading,
        StopCompletely,
        SetMotion,
        // SetSelectedObject marshalled 2026-06-13: the host-API SelectItem /
        // SetSelectedObjectId (looting/targeting) was the LAST AC-mutating helper
        // still calling AC directly off the pump thread. AC's SetSelectedObject
        // (0x0058D110) updates the selection/targeting UIElement subtree; run off
        // the main thread it raced AC's own UI walk -> deterministic corruption AV
        // at acclient+0x60D1D (UIElement smart-ptr refcount writeback into
        // read-only .text, 5 captures in native-crash.log, two threads at once).
        SetSelectedObject,
        // NativeAttack marshalled 2026-09-01 (deep-audit finding #15): the
        // ClientCombatSystem StartAttackRequest/EndAttackRequest pair (plus the
        // height-change notify) had no MainThreadGuard gate, unlike the sibling
        // MeleeAttack/MissileAttack/ChangeCombatMode. UseNativeAttack defaults
        // true, so CombatManager.FireAttack ran this un-marshalled every fight
        // right after a correctly-marshalled SelectItem on the same pump thread.
        // Carried as one entry (not split) so the whole ordered sequence executes
        // atomically after the paired selection, per the audit's fix note.
        NativeAttack,
        // Jump trio marshalled 2026-09-02 (deep-audit finding #2): CommenceJump/
        // DoJump/TapJump call native CommandInterpreter members through
        // _boundCmdInterp with zero MainThreadGuard gate — unlike every sibling
        // (SetAutoRun/SetMotion/StopCompletely/TurnToHeading), which all marshal
        // via this queue. Live-exercised off-thread today via Jumper.cs's
        // Decal-coexistence pump-thread tick. CommenceJump and the later
        // DoJump/LaunchJumpWithMotion release land in separate queue entries
        // (separate ticks, ~msToHoldDown apart) — that's fine, the ordering
        // just needs to stay FIFO within this single-consumer queue, which it does.
        CommenceJump,
        TapJump,
        DoJumpAutonomous,
        // LaunchJumpWithMotion (PlayerPhysicsHooks.cs) is the more dangerous of
        // the two release paths: it Marshal.WriteInt32's directly into
        // CMotionInterp's forward/strafe/turn fields with NO thread gate at
        // all (not even the thiscall-through-delegate try/catch the
        // CommandInterpreter trio has) before calling DoJump. A torn write here
        // racing AC's own motion-graph tick is exactly the CSequence corruption
        // class. Carries the 5 hold-flags packed into A (bit0=shift, bit1=W,
        // bit2=X, bit3=Z, bit4=C).
        LaunchJumpWithMotion,
        // v70 WieldItem: CM_Inventory::Event_GetAndWieldItem (the 0x001A GetAndWieldItem
        // game action) is an inventory send like GiveObjectTo, so it runs here too.
        WieldItem,
        // v72 player-to-player trade: CM_Trade::Event_* / ClientTradeSystem::Accept/DeclineTrade
        // send trade game actions (and Accept/Decline touch the client's Trade object), so they
        // run here too. A = PlayerTrade.TradeOp, B/C = its arguments.
        Trade,
        // Salvage panel marshalled 2026-09-29: CM_Inventory::SendNotice_OpenSalvagePanel,
        // gmSalvageUI::AddNewItem and gmSalvageUI::Salvage were called straight from
        // the plugin pump (RynthAi SalvageManager, Meta expressions). AddNewItem and
        // Salvage are thiscall on the live gmSalvageUI and mutate its UIElement tree
        // (same class as the SetSelectedObject 0x60D1D AV). A = toolId / itemId.
        // The instance pointer is NOT carried: the drain re-enters the helper, which
        // re-reads SalvageHooks.GmSalvageUIInstance on the main thread.
        SalvageOpen,
        SalvageAddItem,
        SalvageExecute,
        // CancelAttack marshalled 2026-09-29: CM_Combat::Event_CancelAttack (0x1B7) is
        // the same CM_Combat game-action sender class as Melee/Missile/ChangeCombatMode
        // but was called straight from the plugin pump. It rides the RING (not a side
        // queue) on purpose: every RynthAi caller pairs it with ring actions
        // (StopCompletely, ChangeCombatMode, MoveItemInternal) and it must land after
        // any attack already queued, so FIFO order with those is what matters. When a
        // gesture defers the ring, the paired actions wait too, so the teardown stays
        // one ordered group.
        CancelAttack,
        // SetAutonomyLevel marshalled 2026-09-29: CM_Movement::Event_AutonomyLevel
        // (0xF752 game-action send) is exported to plugins as SetAutonomyLevelFn and
        // was the only movement sender still called straight from the plugin pump.
        // Rides the ring beside DoMovement/StopMovement/Jump so it stays ordered with
        // them. A = level.
        SetAutonomyLevel,
        // DropItem (2026-09-30, Inventory panel): CM_Inventory::Event_DropItem (0x001B game
        // action) is an inventory send like WieldItem / GiveObjectTo. A = item id.
        DropItem,
        // Skills panel raises (2026-09-30): CM_Train::Event_TrainAttribute / _TrainAttribute2nd /
        // _TrainSkill send the RaiseAttribute / RaiseVital / RaiseSkill game actions. A game-action
        // send like Trade, so it rides the ring too. A = PlayerTraining.TrainKind, B = stype, C = XP
        // (skill credits for TrainKind.TrainWithCredits: Event_TrainSkillAdvancementClass, 0x0047).
        Train,
        // v76 CloseContainer (2026-10-04): CM_Inventory::Event_NoLongerViewingContents (the
        // 0x0195 game action the client sends when a corpse/chest window closes). An inventory
        // send like DropItem; in the ring so a close queued before the next corpse's UseObject
        // goes out first, in the same drain. A = container id.
        CloseContainer,
    }

    // Four payload slots cover every routed action (the 4th was added for
    // MoveItemInternal/SplitStackInternal: id, container, slot, amount). Floats are
    // carried as their IEEE bit pattern in a uint slot (BitConverter round-trip).
    private readonly struct Entry(AcMainThreadQueue.ActionKind kind, uint a, uint b, uint c, uint d)
    {
        public readonly ActionKind Kind = kind;
        public readonly uint A = a;
        public readonly uint B = b;
        public readonly uint C = c;
        public readonly uint D = d;
    }

    private const int Capacity = 256; // power of two
    private const int Mask = Capacity - 1;
    private static readonly Entry[] _slots = new Entry[Capacity];
    private static readonly object _producerLock = new();

    // Monotonic counters. Producers advance _tail under _producerLock; the sole
    // consumer (main thread) advances _head.
    private static int _head;
    private static int _tail;
    private static long _dropped;

    public static long DroppedCount => Interlocked.Read(ref _dropped);

    private static bool Enqueue(ActionKind kind, uint a, uint b, uint c, uint d = 0)
    {
        lock (_producerLock)
        {
            int tail = _tail;
            int head = Volatile.Read(ref _head);
            if (tail - head >= Capacity)
            {
                // Full. Drop rather than block or fall back to an off-thread AC
                // mutation — a dropped movement/attack tick self-corrects on the
                // next bot tick; an off-thread mutation can crash the client.
                Interlocked.Increment(ref _dropped);
                return false;
            }

            _slots[tail & Mask] = new Entry(kind, a, b, c, d);
            Volatile.Write(ref _tail, tail + 1);
            return true;
        }
    }

    // ── Typed enqueue helpers (called by the public action methods when they
    //    detect they're running off AC's main thread) ───────────────────────
    public static bool EnqueueChangeCombatMode(int mode) =>
        Enqueue(ActionKind.ChangeCombatMode, unchecked((uint)mode), 0, 0);

    public static bool EnqueueMeleeAttack(uint targetId, int attackHeight, float powerLevel) =>
        Enqueue(ActionKind.MeleeAttack, targetId, unchecked((uint)attackHeight),
                BitConverter.SingleToUInt32Bits(powerLevel));

    public static bool EnqueueMissileAttack(uint targetId, int attackHeight, float accuracyLevel) =>
        Enqueue(ActionKind.MissileAttack, targetId, unchecked((uint)attackHeight),
                BitConverter.SingleToUInt32Bits(accuracyLevel));

    public static bool EnqueueCancelAttack() =>
        Enqueue(ActionKind.CancelAttack, 0, 0, 0);

    public static bool EnqueueDoMovement(uint motion, float speed, int holdKey) =>
        Enqueue(ActionKind.DoMovement, motion, unchecked((uint)holdKey),
                BitConverter.SingleToUInt32Bits(speed));

    public static bool EnqueueStopMovement(uint motion, int holdKey) =>
        Enqueue(ActionKind.StopMovement, motion, unchecked((uint)holdKey), 0);

    public static bool EnqueueSetAutoRun(bool enabled) =>
        Enqueue(ActionKind.SetAutoRun, enabled ? 1u : 0u, 0, 0);

    public static bool EnqueueTurnToHeading(float headingDegrees) =>
        Enqueue(ActionKind.TurnToHeading, BitConverter.SingleToUInt32Bits(headingDegrees), 0, 0);

    public static bool EnqueueStopCompletely() =>
        Enqueue(ActionKind.StopCompletely, 0, 0, 0);

    // ── Direct-heading slot (coalesced, latest value wins) ──────────────────────
    // PlayerPhysicsHooks.SetPlayerHeadingDirect snaps the player's CPhysicsObj
    // orientation quaternion with two raw stores (qw, then qz). Plugins call it
    // (Host.TurnToHeading) from the pump thread on every nav tick; run there it
    // raced AC's own physics integration and could leave a torn, non-unit
    // quaternion (new qw with old qz). Off-thread callers now park the heading
    // here and Drain applies it on the main thread. One slot, not a ring entry:
    // nav re-issues the heading every tick and only the newest one matters, so a
    // steering bot can never fill the action ring with headings. Lock-free and
    // allocation-free; never drops (a newer heading simply replaces an older one).
    // Deliberately NOT ActionKind.TurnToHeading: that dispatches the gradual
    // CommandInterpreter turn, which would change the steering behaviour.
    private static int _directHeadingPending;
    private static uint _directHeadingBits;

    public static bool EnqueueDirectHeading(float headingDegrees)
    {
        Volatile.Write(ref _directHeadingBits, BitConverter.SingleToUInt32Bits(headingDegrees));
        Volatile.Write(ref _directHeadingPending, 1);
        return true;
    }

    // Main thread only (from Drain). Same order as the pre-queue code: the direct
    // snap first, the CommandInterpreter turn only when there is no player.
    private static void DrainDirectHeading()
    {
        if (Interlocked.Exchange(ref _directHeadingPending, 0) == 0) return;
        float heading = BitConverter.UInt32BitsToSingle(Volatile.Read(ref _directHeadingBits));
        try
        {
            if (!PlayerPhysicsHooks.SetPlayerHeadingDirect(heading))
                CommandInterpreterHooks.TurnToHeading(heading);
        }
        catch { }
    }

    // ── Overlay click-to-select slot (coalesced, latest click wins) ─────────────
    // A click on a Radar dot or a monster nameplate (ImGui overlays, 2026-09-30)
    // parks the object id here; Drain runs ClientHelperHooks.SelectFromOverlay on
    // the main thread, which re-checks that the object still exists and that the
    // client isn't portaling or logging out right before AC's SetSelectedObject.
    // The faces always park here (never select inside the ImGui frame build), so
    // the select runs in the same main-thread phase as every other queued action.
    // One slot, not a ring entry: a click is one select, two clicks in one frame
    // collapse to the newer one, and the slot sits before the ring's gesture
    // defer (a selection is a UI change, not a motion; a click in the 3D world
    // selects mid-gesture too). Lock-free and allocation-free.
    private static int _overlaySelectPending;
    private static uint _overlaySelectId;

    public static bool EnqueueOverlaySelect(uint objectId)
    {
        if (objectId == 0) return false;
        Volatile.Write(ref _overlaySelectId, objectId);
        Volatile.Write(ref _overlaySelectPending, 1);
        return true;
    }

    // Main thread only (from Drain).
    private static void DrainOverlaySelect()
    {
        if (Interlocked.Exchange(ref _overlaySelectPending, 0) == 0) return;
        uint id = Volatile.Read(ref _overlaySelectId);
        try { ClientHelperHooks.SelectFromOverlay(id); }
        catch { }
    }

    public static bool EnqueueSetMotion(uint motion, bool enabled) =>
        Enqueue(ActionKind.SetMotion, motion, enabled ? 1u : 0u, 0);

    public static bool EnqueueJump(float extent) =>
        Enqueue(ActionKind.Jump, BitConverter.SingleToUInt32Bits(extent), 0, 0);

    public static bool EnqueueSetAutonomyLevel(uint level) =>
        Enqueue(ActionKind.SetAutonomyLevel, level, 0, 0);

    public static bool EnqueueUseObject(uint objectId) =>
        Enqueue(ActionKind.UseObject, objectId, 0, 0);

    public static bool EnqueueUseObjectOn(uint sourceObjectId, uint targetObjectId) =>
        Enqueue(ActionKind.UseObjectOn, sourceObjectId, targetObjectId, 0);

    public static bool EnqueueUseEquippedItem(uint sourceObjectId, uint targetObjectId) =>
        Enqueue(ActionKind.UseEquippedItem, sourceObjectId, targetObjectId, 0);

    public static bool EnqueueMoveItemExternal(uint objectId, uint targetContainerId, int amount) =>
        Enqueue(ActionKind.MoveItemExternal, objectId, targetContainerId, unchecked((uint)amount));

    public static bool EnqueueMoveItemInternal(uint objectId, uint targetContainerId, int slot, int amount) =>
        Enqueue(ActionKind.MoveItemInternal, objectId, targetContainerId, unchecked((uint)slot), unchecked((uint)amount));

    public static bool EnqueueSplitStackInternal(uint objectId, uint targetContainerId, int slot, int amount) =>
        Enqueue(ActionKind.SplitStackInternal, objectId, targetContainerId, unchecked((uint)slot), unchecked((uint)amount));

    public static bool EnqueueMergeStackInternal(uint sourceObjectId, uint targetObjectId) =>
        Enqueue(ActionKind.MergeStackInternal, sourceObjectId, targetObjectId, 0);

    public static bool EnqueueGiveObjectTo(uint objectId, uint targetId, int amount) =>
        Enqueue(ActionKind.GiveObjectTo, objectId, targetId, unchecked((uint)amount));

    public static bool EnqueueWieldItem(uint objectId, uint equipMask) =>
        Enqueue(ActionKind.WieldItem, objectId, equipMask, 0);

    public static bool EnqueueDropItem(uint objectId) =>
        Enqueue(ActionKind.DropItem, objectId, 0, 0);

    public static bool EnqueueCloseContainer(uint containerId) =>
        Enqueue(ActionKind.CloseContainer, containerId, 0, 0);

    public static bool EnqueueTrade(uint op, uint a, uint b) =>
        Enqueue(ActionKind.Trade, op, a, b);

    public static bool EnqueueTrain(uint kind, uint stype, uint xp) =>
        Enqueue(ActionKind.Train, kind, stype, xp);

    public static bool EnqueueSalvageOpen(uint toolId) =>
        Enqueue(ActionKind.SalvageOpen, toolId, 0, 0);

    public static bool EnqueueSalvageAddItem(uint itemId) =>
        Enqueue(ActionKind.SalvageAddItem, itemId, 0, 0);

    public static bool EnqueueSalvageExecute() =>
        Enqueue(ActionKind.SalvageExecute, 0, 0, 0);

    public static bool EnqueueSetSelectedObject(uint objectId) =>
        Enqueue(ActionKind.SetSelectedObject, objectId, 0, 0);

    public static bool EnqueueNativeAttack(int attackHeight, float power) =>
        Enqueue(ActionKind.NativeAttack, unchecked((uint)attackHeight),
                BitConverter.SingleToUInt32Bits(power), 0);

    public static bool EnqueueCommenceJump() => Enqueue(ActionKind.CommenceJump, 0, 0, 0);
    public static bool EnqueueTapJump() => Enqueue(ActionKind.TapJump, 0, 0, 0);
    public static bool EnqueueDoJumpAutonomous(bool autonomous) =>
        Enqueue(ActionKind.DoJumpAutonomous, autonomous ? 1u : 0u, 0, 0);

    public static bool EnqueueLaunchJumpWithMotion(bool shift, bool holdW, bool holdX, bool holdZ, bool holdC)
    {
        uint flags = (shift ? 1u : 0u) | (holdW ? 2u : 0u) | (holdX ? 4u : 0u) | (holdZ ? 8u : 0u) | (holdC ? 16u : 0u);
        return Enqueue(ActionKind.LaunchJumpWithMotion, flags, 0, 0);
    }

    // Latched by EngineLifecycle.Shutdown: once teardown begins, queued plugin
    // actions must NOT keep executing on AC's main thread — the detours stay
    // live until MH_DisableHook(ALL), so without this latch a marshalled
    // mutation (SetAutoRun etc.) can run mid-teardown against state that
    // plugin Shutdowns are concurrently freeing (observed live at
    // 2026-06-11 07:44:03: "Move: SetAutoRun(False)" fired between plugin
    // shutdown steps). Abandoned entries are benign.
    private static volatile bool _disarmed;

    /// <summary>Stop executing queued actions/casts permanently (engine teardown).</summary>
    public static void Disarm() => _disarmed = true;

    /// <summary>Engine teardown has begun (ChatCallbackHooks stops holding chat).</summary>
    public static bool IsDisarmed => _disarmed;

    // Single-consumer drain on AC's main thread (EngineFrameController.OnEndScene).
    // Re-invokes the public action methods; on the main thread they execute the
    // real AC call directly (their IsOnMainThread gate is satisfied here).
    // Gesture-phase defer state for the action ring (mirrors the cast slot's
    // 56e6946 guard; see DrainDeferTickCap note below for why we PROCEED
    // rather than drop after the cap).
    private static int _drainDeferTicks;
    private const int DrainDeferTickCap = 250;

    public static void Drain()
    {
        if (_disarmed)
        {
            // Held incoming chat still prints during teardown (the AddTextToScroll detour
            // stays live until MH_DisableHook(ALL)): it is AC's own line, not a plugin action.
            try { ChatCallbackHooks.OnMainThreadDrain(disarmed: true); } catch { }
            return;
        }

        // We are on AC's main thread by definition. Latch it here too: after a hot
        // reload the first SmartBox/combat detour (the other latches) can come well
        // after the first drain, and until the latch is set every dispatch below
        // thinks it's off-thread and re-queues itself. A chat command a plugin queued
        // straight after the reload then spun the main thread forever
        // (DrainChatCommands -> Dispatch -> EnqueueChatCommand, 2026-09-29).
        MainThreadGuard.RecordIfFirst();

        // Incoming chat held for the plugins' verdict prints first, before anything below
        // writes its own lines, so the chat window keeps the order lines arrived in.
        try { ChatCallbackHooks.OnMainThreadDrain(disarmed: false); } catch { }

        // Deep-audit finding #22 (2026-06-18): these three queues are
        // documented as deliberately separate from the gesture-gated action
        // ring below specifically so appraisals/chat "can never get
        // gesture-deferred" — but the gesture-defer early-return further
        // down used to run BEFORE these calls, so a non-empty action ring
        // sitting behind an in-flight gesture silently stalled chat output
        // and 0xC8 appraisal sends too, contradicting that invariant. Each
        // drains only while ITS OWN queue is non-empty, so moving them here
        // costs nothing when they're empty (the common case).
        DrainChat();
        DrainChatCommands();
        DrainRequestIds();
        DrainQueryHealth();
        // Vendor buy/sell: a packet send through the client's own SendShopEvent, not a
        // motion, so it sits with the non-gesture queues above. Also polls whether the
        // vendor window is still open. Idle fast path when no vendor is open.
        try { VendorTrade.MainThreadTick(); } catch { }
        // Char-select service: publishes the UIFlow mode, runs a pending auto-login
        // LogOnCharacter request and snapshots the native character list (throttled).
        // Pre-login this runs from the Client::UseTime drain (EndScene is not hooked
        // until after login). Idle in the world.
        try { CharacterManagementHooks.MainThreadTick(); } catch { }

        // Plugin heading snap (coalesced slot). Before the ring and its gesture
        // defer: the pre-queue raw write landed immediately, gesture or not, and
        // ahead of the SetAutoRun a nav tick queues right after it. Keep both.
        DrainDirectHeading();

        // Overlay click-to-select (Radar dot / nameplate click): also before the
        // gesture defer, so a click selects at once even while the bot swings.
        DrainOverlaySelect();

        int head = _head;                     // only the main thread writes _head
        int tail = Volatile.Read(ref _tail);

        // ── Gesture-phase serialization (extends the 56e6946 anim-walk guard
        // to the whole ring) ─────────────────────────────────────────────────
        // Every ring action perturbs AC's action/motion state to some degree:
        // UseObject starts a reach gesture, ChangeCombatMode rebuilds the
        // motion graph, movement rewrites locomotion channels. Retail AC
        // serializes these against the IN-FLIGHT gesture via its action queue;
        // we previously serialized thread + tick-phase but not gesture-phase.
        // Defer the drain while the pending-motion list [CMI+0x80] is
        // non-empty — bounded, then PROCEED (unlike the cast slot we never
        // drop: dropping arbitrary item/movement actions desyncs the bot far
        // worse than a late injection, and the pre-2026-06-12 behavior was
        // "always inject" anyway, so proceeding past the cap is never worse).
        if (head != tail)
        {
            if (PlayerPhysicsHooks.TryGetCastGestureInProgress(out bool gestureInFlight) && gestureInFlight
                && ++_drainDeferTicks <= DrainDeferTickCap)
                return;
            _drainDeferTicks = 0;
        }
        while (head != tail)
        {
            Entry e = _slots[head & Mask];
            Volatile.Write(ref _head, ++head);

            try
            {
                switch (e.Kind)
                {
                    case ActionKind.ChangeCombatMode:
                        CombatActionHooks.ChangeCombatMode(unchecked((int)e.A));
                        break;
                    case ActionKind.MeleeAttack:
                        CombatActionHooks.MeleeAttack(e.A, unchecked((int)e.B),
                            BitConverter.UInt32BitsToSingle(e.C));
                        break;
                    case ActionKind.MissileAttack:
                        CombatActionHooks.MissileAttack(e.A, unchecked((int)e.B),
                            BitConverter.UInt32BitsToSingle(e.C));
                        break;
                    case ActionKind.CancelAttack:
                        CombatActionHooks.CancelAttack();
                        break;
                    case ActionKind.DoMovement:
                        MovementActionHooks.DoMovement(e.A,
                            BitConverter.UInt32BitsToSingle(e.C), unchecked((int)e.B));
                        break;
                    case ActionKind.StopMovement:
                        MovementActionHooks.StopMovement(e.A, unchecked((int)e.B));
                        break;
                    case ActionKind.Jump:
                        MovementActionHooks.JumpNonAutonomous(BitConverter.UInt32BitsToSingle(e.A));
                        break;
                    case ActionKind.SetAutonomyLevel:
                        MovementActionHooks.SetAutonomyLevel(e.A);
                        break;
                    case ActionKind.UseObject:
                        ClientHelperHooks.UseObject(e.A);
                        break;
                    case ActionKind.UseObjectOn:
                        ClientHelperHooks.UseObjectOn(e.A, e.B);
                        break;
                    case ActionKind.UseEquippedItem:
                        ClientHelperHooks.UseEquippedItem(e.A, e.B);
                        break;
                    case ActionKind.MoveItemExternal:
                        ClientHelperHooks.MoveItemExternal(e.A, e.B, unchecked((int)e.C));
                        break;
                    case ActionKind.MoveItemInternal:
                        ClientHelperHooks.MoveItemInternal(e.A, e.B, unchecked((int)e.C), unchecked((int)e.D));
                        break;
                    case ActionKind.SplitStackInternal:
                        ClientHelperHooks.SplitStackInternal(e.A, e.B, unchecked((int)e.C), unchecked((int)e.D));
                        break;
                    case ActionKind.MergeStackInternal:
                        ClientHelperHooks.MergeStackInternal(e.A, e.B);
                        break;
                    case ActionKind.GiveObjectTo:
                        ClientHelperHooks.GiveObjectTo(e.A, e.B, unchecked((int)e.C));
                        break;
                    case ActionKind.WieldItem:
                        ClientHelperHooks.WieldItem(e.A, e.B);
                        break;
                    case ActionKind.DropItem:
                        ClientHelperHooks.DropItem(e.A);
                        break;
                    case ActionKind.CloseContainer:
                        ClientHelperHooks.CloseContainer(e.A);
                        break;
                    case ActionKind.Trade:
                        PlayerTrade.RunQueued(e.A, e.B, e.C);
                        break;
                    case ActionKind.Train:
                        PlayerTraining.RunQueued(e.A, e.B, e.C);
                        break;
                    case ActionKind.SalvageOpen:
                        ClientHelperHooks.SalvagePanelOpen(e.A);
                        break;
                    case ActionKind.SalvageAddItem:
                        ClientHelperHooks.SalvagePanelAddItem(e.A);
                        break;
                    case ActionKind.SalvageExecute:
                        ClientHelperHooks.SalvagePanelExecute();
                        break;
                    case ActionKind.SetSelectedObject:
                        // On the main thread now -> SetSelectedObjectId's gate is
                        // satisfied and it calls AC's SetSelectedObject directly.
                        ClientHelperHooks.SetSelectedObjectId(e.A);
                        break;
                    case ActionKind.NativeAttack:
                        // On the main thread now -> NativeAttack's gate is
                        // satisfied and it fires the real StartAttackRequest/
                        // EndAttackRequest pair directly.
                        ClientCombatHooks.NativeAttack(unchecked((int)e.A),
                            BitConverter.UInt32BitsToSingle(e.B));
                        break;
                    case ActionKind.CommenceJump:
                        CommandInterpreterHooks.CommenceJump();
                        break;
                    case ActionKind.TapJump:
                        CommandInterpreterHooks.TapJump();
                        break;
                    case ActionKind.DoJumpAutonomous:
                        CommandInterpreterHooks.DoJump(e.A != 0);
                        break;
                    case ActionKind.LaunchJumpWithMotion:
                        PlayerPhysicsHooks.LaunchJumpWithMotion(
                            (e.A & 1u) != 0, (e.A & 2u) != 0, (e.A & 4u) != 0,
                            (e.A & 8u) != 0, (e.A & 16u) != 0);
                        break;
                    case ActionKind.SetAutoRun:
                        CommandInterpreterHooks.SetAutoRun(e.A != 0);
                        break;
                    case ActionKind.TurnToHeading:
                        CommandInterpreterHooks.TurnToHeading(BitConverter.UInt32BitsToSingle(e.A));
                        break;
                    case ActionKind.StopCompletely:
                        CommandInterpreterHooks.StopCompletely();
                        break;
                    case ActionKind.SetMotion:
                        CommandInterpreterHooks.SetMotion(e.A, e.B != 0);
                        break;
                }
            }
            catch
            {
                // One action must never break the drain loop. (AC-side AVs are
                // not managed exceptions and won't be caught here — but these
                // now run on the correct thread, which is the whole point.)
            }

            tail = Volatile.Read(ref _tail);
        }
    }

    // ── RequestId slot (0xC8 appraisal sends from AutoIdService, and every other
    //    off-thread CombatActionHooks.RequestId caller: plugin RequestIdFn, the
    //    PluginManager.TickAll login self-identify) ─────────────────────────────
    // AutoIdService runs on a Timer thread; sending 0xC8 directly there does AC heap
    // allocation + a non-atomic shared UI-counter increment off AC's main thread (the
    // off-thread-send class flagged for RequestId). Marshal onto the main thread here.
    // Kept in its OWN queue — NOT the gesture-deferred action ring above — so a backlog
    // of appraisals can never crowd out combat/movement actions or get gesture-deferred:
    // an appraisal is just a packet send and doesn't perturb AC's motion/UI state.
    private static readonly System.Collections.Generic.Queue<uint> _requestIdQueue = new();
    private static readonly object _requestIdLock = new();
    private const int MaxRequestIdQueue = 256;

    // Pump/Timer-thread enqueue. Drops (returns false) if the queue is full.
    public static bool EnqueueRequestId(uint objectId)
    {
        if (objectId == 0) return false;
        lock (_requestIdLock)
        {
            if (_requestIdQueue.Count >= MaxRequestIdQueue)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _requestIdQueue.Enqueue(objectId);
            return true;
        }
    }

    // Single-consumer drain on AC's main thread (from Drain()). On the main thread
    // CombatActionHooks.RequestId sends the 0xC8 directly.
    private static void DrainRequestIds()
    {
        // Bounded like DrainChatCommands: a re-queue never spins the main thread.
        int budget;
        lock (_requestIdLock) budget = _requestIdQueue.Count;
        while (budget-- > 0)
        {
            uint id;
            lock (_requestIdLock)
            {
                if (_requestIdQueue.Count == 0) return;
                id = _requestIdQueue.Dequeue();
            }
            try { CombatActionHooks.RequestId(id); } catch { }
        }
    }

    // ── QueryHealth slot (0x1BF CM_Combat::Event_QueryHealth sends) ─────────────────
    // Plugins (RynthAi CombatManager, once per fight target) call QueryHealthFn from the
    // plugin pump; the send allocates an AC blob and pushes AC's send queue, so it is
    // main-thread-only like 0xC8. Same shape as the RequestId slot: its own small
    // queue, NOT the gesture-deferred action ring (a packet send doesn't perturb the
    // motion graph and a health query must not wait out a swing). Callers ignore the
    // result and only want the async 0x01C0 reply, so a target already waiting here is
    // not queued twice. Pre-sized, so enqueue does not allocate.
    private const int MaxQueryHealthQueue = 64;
    private static readonly System.Collections.Generic.Queue<uint> _queryHealthQueue = new(MaxQueryHealthQueue);
    private static readonly object _queryHealthLock = new();
    private static int _queryHealthPending; // lock-free "anything queued?" for the per-frame drain

    // Pump-thread enqueue. Drops (returns false) if the queue is full.
    public static bool EnqueueQueryHealth(uint targetId)
    {
        if (targetId == 0) return false;
        lock (_queryHealthLock)
        {
            if (_queryHealthQueue.Contains(targetId))
                return true; // already pending; one reply answers both
            if (_queryHealthQueue.Count >= MaxQueryHealthQueue)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _queryHealthQueue.Enqueue(targetId);
            Volatile.Write(ref _queryHealthPending, _queryHealthQueue.Count);
            return true;
        }
    }

    // Single-consumer drain on AC's main thread (from Drain()). On the main thread
    // CombatActionHooks.QueryHealth sends the 0x1BF directly.
    private static void DrainQueryHealth()
    {
        if (Volatile.Read(ref _queryHealthPending) == 0) return;
        int budget;
        lock (_queryHealthLock) budget = _queryHealthQueue.Count;
        while (budget-- > 0)
        {
            uint id;
            lock (_queryHealthLock)
            {
                if (_queryHealthQueue.Count == 0) { Volatile.Write(ref _queryHealthPending, 0); return; }
                id = _queryHealthQueue.Dequeue();
                Volatile.Write(ref _queryHealthPending, _queryHealthQueue.Count);
            }
            try { CombatActionHooks.QueryHealth(id); } catch { }
        }
    }

    // ── Chat slot (WriteToChat strings) ──────────────────────────────────────────
    // WriteToChat carries a string, which can't ride the uint Entry queue. Off-thread
    // AddTextToScroll races AC's chat-scroll buffer and corrupts it (the recurring
    // 0x00460D1D write-AV that killed a 5h+ session 2026-06-05). Chat writes marshal
    // here and drain on the main thread from Drain(), alongside the item/movement
    // actions. WriteToChat's 100 ms rate-limit runs BEFORE the enqueue, so a bot retry
    // burst is dropped on the pump thread and this queue never fills with spam.
    private static readonly System.Collections.Generic.Queue<(string Text, int ChatType)> _chatQueue = new();
    private static readonly object _chatLock = new();
    private const int MaxChatQueue = 64;

    // Pump-thread enqueue. Drops (returns false) if the queue is full.
    public static bool EnqueueWriteToChat(string text, int chatType)
    {
        lock (_chatLock)
        {
            if (_chatQueue.Count >= MaxChatQueue)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _chatQueue.Enqueue((text, chatType));
            return true;
        }
    }

    // Single-consumer drain on AC's main thread. Re-invokes WriteToChat, whose
    // IsOnMainThread gate is satisfied here so it runs AddTextToScroll directly.
    private static void DrainChat()
    {
        // Bounded like DrainChatCommands: a re-queue never spins the main thread.
        int budget;
        lock (_chatLock) budget = _chatQueue.Count;
        while (budget-- > 0)
        {
            (string Text, int ChatType) item;
            lock (_chatLock)
            {
                if (_chatQueue.Count == 0) return;
                item = _chatQueue.Dequeue();
            }
            try { ClientHelperHooks.WriteToChat(item.Text, item.ChatType); }
            catch { }
        }
    }

    // ── Chat COMMAND slot (full ChatCommandDispatcher.Dispatch lines) ────────────
    // Deep-audit finding #4 (2026-06-18): ChatCommandDispatcher.Dispatch is not
    // network-only — it runs arbitrary plugin OnChatBarEnter handlers,
    // constructs/destructs AC's native PStringBase<char> (alloc/free in AC's
    // heap), and calls Event_Talk/Event_Emote/etc. directly against AC's live
    // chat-manager, all of which assumed (incorrectly) "safe from any thread".
    // Off-thread callers exist today: OnLoginCommandRunner (ThreadPool via
    // Task.Run), ChatFileDispatcher (FileSystemWatcher thread), and
    // Host.InvokeChatParser (plugin export, any thread). Gating inside
    // Dispatch() itself (rather than at each caller) covers all three at once.
    // A full command line can't ride the uint Entry ring, so it gets its own
    // string queue, same shape as the WriteToChat slot above.
    private static readonly System.Collections.Generic.Queue<string> _chatCommandQueue = new();
    private static readonly object _chatCommandLock = new();
    private const int MaxChatCommandQueue = 64;

    // Off-thread enqueue. Drops (returns false) if the queue is full.
    public static bool EnqueueChatCommand(string text)
    {
        lock (_chatCommandLock)
        {
            if (_chatCommandQueue.Count >= MaxChatCommandQueue)
            {
                Interlocked.Increment(ref _dropped);
                return false;
            }
            _chatCommandQueue.Enqueue(text);
            return true;
        }
    }

    // Single-consumer drain on AC's main thread. Re-invokes Dispatch, whose
    // IsOnMainThread gate is satisfied here so it runs the full body directly
    // instead of re-enqueuing.
    private static void DrainChatCommands()
    {
        // Only what was queued before this drain: anything a dispatch queues again
        // waits for the next frame instead of spinning here.
        int budget;
        lock (_chatCommandLock) budget = _chatCommandQueue.Count;
        while (budget-- > 0)
        {
            string text;
            lock (_chatCommandLock)
            {
                if (_chatCommandQueue.Count == 0) return;
                text = _chatCommandQueue.Dequeue();
            }
            try { ChatCommandDispatcher.Dispatch(text); }
            catch { }
        }
    }

    // ── Cast slot (SelectItem + CastSpell pair) ─────────────────────────────────
    // Drained at AC's GAME-LOGIC tick (Client::UseTime via GameTickHooks), NOT the
    // EndScene render tick. Single-outstanding: EnqueueCast returns false while a cast
    // is pending so the off-thread caller (BuffManager / CombatManager) retries next
    // tick — they already handle a false return without wedging. At ~frame-rate drain
    // the slot clears within ~1 frame. This is the LAST off-thread AC mutator to be
    // marshalled, closing the off-thread object-graph corruption class at its source.
    private static int _castPending;
    private static uint _castTargetId;
    private static uint _castSpellId;
    private static readonly object _castLock = new();

    // Ticks DrainCasts has deferred the pending cast because a motion gesture was
    // still in flight. Bounded so a stuck gesture (the documented wand-wield wedge
    // variant) can't park a cast forever: past the cap the cast is DROPPED — the
    // plugin's no-chat-resolve machinery already retries casts that produce no
    // chat, so a drop degrades to one retry cycle, never a wedge.
    private static int _castDeferTicks;
    private static long _castDeferDrops;
    private const int CastDeferTickCap = 250;   // ~4-8 s at the 30-63 Hz UseTime rate

    /// <summary>Casts dropped after deferring CastDeferTickCap ticks (stuck gesture).</summary>
    public static long CastDeferDropCount => Interlocked.Read(ref _castDeferDrops);

    // Pump-thread enqueue. Returns false (caller retries next tick) if a cast is
    // already pending.
    public static bool EnqueueCast(uint targetId, uint spellId)
    {
        lock (_castLock)
        {
            if (_castPending != 0) return false;
            _castTargetId = targetId;
            _castSpellId = spellId;
            _castPending = 1;
            return true;
        }
    }

    // Drained on AC's MAIN thread from the Client::UseTime detour (game-logic tick).
    // Re-invokes CombatActionHooks.CastSpell, which on the main thread executes the
    // cast directly (SelectItem + ClientMagicSystem::CastSpell via the SEH trampoline).
    public static void DrainCasts()
    {
        if (_disarmed) return;                               // engine teardown in progress
        if (Volatile.Read(ref _castPending) == 0) return;   // alloc-free fast path

        // ── Anim-walk race guard (dump-proven 2026-06-12) ────────────────────
        // DrainCasts runs PRE-tick (selection timing), but a cast initiates a
        // wind-up gesture that REPLACES the player's pending CSequence motions —
        // and the SAME UseTime call then walks that sequence. If a gesture is
        // already mid-flight when we inject, the walker can hit a freed/null
        // node: AV at acclient 0x00526840 AnimSequenceNode::get_part_frame
        // [null+0xC], full stack in CrashDumps\acclient_anim_av_4716.dmp
        // (7.6 h overnight soak, crash INSIDE _originalUseTime, our detour on
        // the stack below it). Same mechanism the post-tick reorder fixed for
        // UseObject/movement — casts were exempted and carried the residue.
        // Defer while the pending-motion list is non-empty (the exact structure
        // the tick walks); main-thread read, alloc-free, fail-open: if the read
        // itself fails we cast (pre-fix behavior) rather than park.
        if (PlayerPhysicsHooks.TryGetCastGestureInProgress(out bool gestureInFlight) && gestureInFlight)
        {
            if (++_castDeferTicks <= CastDeferTickCap)
                return;                                      // retry next tick
            // Gesture stuck past the cap — drop the cast instead of injecting
            // into a wedged motion graph; the plugin retries via no-chat-resolve.
            lock (_castLock) { _castPending = 0; }
            _castDeferTicks = 0;
            Interlocked.Increment(ref _castDeferDrops);
            return;
        }
        _castDeferTicks = 0;

        uint target, spell;
        lock (_castLock)
        {
            if (_castPending == 0) return;
            target = _castTargetId;
            spell = _castSpellId;
            _castPending = 0;
        }
        CombatActionHooks.CastSpell(target, unchecked((int)spell));
    }
}
