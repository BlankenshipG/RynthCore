// ============================================================================
//  RynthCore.Engine - ImGui/MonsterHud/MonsterHud.cs
//  RynthVision nameplates: MMO-style plates over monsters in the 3D world
//  (name, level badge, health bar with absolute HP when a max is known, "weak
//  to" pill, distance, a row of debuff icons); the selected target gets a gold
//  frame, a glow and a pointer. Also the host of the other world overlays:
//  the player's own plate (PlayerPlate) and the combat text (CombatText:
//  damage / heal numbers, kill bursts, XP / Luminance / Radiance gains).
//
//  Part of RynthVision: everything here is off unless the RynthVision plugin
//  is in the plugin list (HudFeed.VisionInstalled), and each part has its own
//  switch (MonsterHudSettings).
//
//  Drawn into UnderUiLayer's draw list, which renders at AC's 3D->UI
//  transition: after the world, under AC's own windows (chat, radar,
//  inventory) and every ImGui panel. The only input it takes is a left-click
//  on a monster plate (MonsterHudSettings.ClickToSelect): while the cursor is
//  on a plate and no ImGui panel is under it, WantsMouse asks the game window
//  to keep that click from AC (EngineFrameController ORs it into the capture
//  flags), and the press selects the plate's monster through the main-thread
//  overlay-select slot. Every other click still goes to the game.
//
//  Two rates:
//    Update (AC's render thread, before the ImGui frame): at most 4x a second
//      it picks which monsters get a plate - attackable creatures from the
//      engine's main-thread snapshots (ClientObjectHooks.TryGetSnapshot*, a
//      dictionary lookup each, no AC calls), within range, nearest first,
//      capped at MaxPlates, the target always kept. Players are skipped. The
//      same scan picks the name-only labels: non-attackable creatures (NPCs,
//      vendors; summoned pets skipped) within NpcMaxDistance, nearest first,
//      capped at MaxNpcLabels apart from the plates (and players, if "Player
//      names" is on). Each object is classified once (Classes). The same tick
//      polls the player's XP / Luminance for the gain text.
//    Draw (inside the ImGui frame): every frame each chosen monster's LIVE
//      position is read (main thread, a handful of objects) and projected with
//      the engine's camera capture (GameMatrixCapture), so plates stick to
//      moving monsters without the 10 Hz snapshot's stepping.
//
//  World -> screen: AC transforms geometry on the CPU; GameMatrixCapture
//  rebuilds the view from SmartBox's camera frame and hooks SetTransform for
//  the projection. Positions are landblock-local; the view is built in
//  GameMatrixCapture.FrameLandblock's frame, so a monster in a neighbouring
//  landblock is shifted 192 m per landblock step before projecting (D3D axes:
//  x = east, y = up, z = north). Viewport pixels are mapped to ImGui's
//  display size in case the backbuffer and the client differ.
//
//  Unload-safe: no threads, timers, hooks or process-wide subscriptions; all
//  state is engine-owned statics that die with the generation. Nothing here
//  blocks: settings I/O and the spell-table read are queued, data locks are brief.
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices;
using ImGuiNET;
using RynthCore.Engine.Compatibility;
using RynthCore.Engine.D3D9;
using RynthCore.Engine.UI.Data;

namespace RynthCore.Engine.ImGuiBackend.Hud;

internal static class MonsterHud
{
    // ── Tunables ─────────────────────────────────────────────────────────
    private const int SnapshotPeriodMs = 250;        // monster list refresh (4 Hz)
    private const double AttackerMemorySec = 20;     // "hit you recently"
    private const double HurtMemorySec = 60;         // "hurt recently" (engaged filter)
    private const uint TypeCreature = 0x10;          // ITEM_TYPE TYPE_CREATURE
    private const uint BfPlayer = 0x8;               // PublicWeenieDesc BF_PLAYER
    private const uint BfVendor = 0x200;             // PublicWeenieDesc BF_VENDOR
    private const uint PetOwnerIid = 44;             // PropertyInstanceId PetOwner (summoned pets)
    internal const float LandblockSize = 192f;
    private const int MaxIcons = 5;                  // debuff icons per plate; more shows "+N"
    private const float BlinkSec = 5f;               // icons blink in their last seconds

    // ── Palette (ARGB literals -> ImGui ABGR) ────────────────────────────
    private static readonly uint BarBack = C(0xE00C1118), BarEdge = C(0xFF000000), Tick = C(0x70000000),
        Shine = C(0x38FFFFFF), Trail = C(0xFFF3E2A8), Gold = C(0xFFE6C766), GoldBright = C(0xFFFFD966),
        GoldGlow = C(0x55E6C766), NameText = C(0xFFF2E8DC), AttackerText = C(0xFFFF8A7A), Outline = C(0xE6000000),
        HpText = C(0xFFFFFFFF), DimText = C(0xFFB8C4D0), BadgeBack = C(0xD0101820), BadgeEdge = C(0xFF34587A),
        Leader = C(0x60FFFFFF), HoverEdge = C(0xC0FFFFFF), UnknownFill = C(0xFF5E8A6A), IconBack = C(0xD80B1219), OthersTint = C(0xFFFF8A7A);
    private static readonly Vector3 HpHigh = new(0x3F, 0xD0, 0x6A), HpMid = new(0xE8, 0xB3, 0x33), HpLow = new(0xE0, 0x4A, 0x4A);

    // Element colours, shared by the "weak to" pill and the vulnerability icons.
    private static readonly uint ElemSlash = C(0xFFC9D1DB), ElemPierce = C(0xFFD9C19A), ElemBludgeon = C(0xFFB59A7E),
        ElemFire = C(0xFFFF7A3D), ElemCold = C(0xFF7FD3FF), ElemLightning = C(0xFFC89BFF), ElemAcid = C(0xFF8FE36A),
        ElemNether = C(0xFFB06BE0);

    // One glyph and tint per DebuffKind (index = kind).
    private static readonly string[] KindGlyph =
    {
        PhosphorIcons.Fire, PhosphorIcons.Snowflake, PhosphorIcons.Lightning, PhosphorIcons.Flask,
        PhosphorIcons.Sword, PhosphorIcons.Knife, PhosphorIcons.Hammer,
        PhosphorIcons.ShieldSlash, PhosphorIcons.MagicWand, PhosphorIcons.ShieldWarning,
        PhosphorIcons.ArrowFatLinesDown, PhosphorIcons.HeartBreak, PhosphorIcons.Footprints,
        PhosphorIcons.TrendDown, PhosphorIcons.Skull, PhosphorIcons.ArrowFatDown,
    };
    private static readonly uint[] KindTint =
    {
        ElemFire, ElemCold, ElemLightning, ElemAcid, ElemSlash, ElemPierce, ElemBludgeon,
        C(0xFF9FB4C8),   // Imperil: steel
        C(0xFFB98CFF),   // Magic Yield: violet
        C(0xFFFFB347),   // melee / missile defense: orange
        C(0xFFF2D24B),   // attributes: yellow
        C(0xFFFF6B8A),   // vitals / regen: pink-red
        C(0xFFA8C66C),   // movement: moss
        C(0xFF8FB8DE),   // skills: blue-grey
        C(0xFF7CE08A),   // damage over time: green
        C(0xFFC8C8C8),   // other
    };
    private static readonly string[] StackDigits = { "", "", "2", "3", "4", "5", "6", "7", "8", "9" };

    internal static uint C(uint argb) => RynthTheme.Argb(argb);

    // ── Per-monster state ────────────────────────────────────────────────
    private sealed class Plate
    {
        public uint Id;
        public string Name = "";
        /// <summary>RadarKind.Monster: a full monster plate. Npc / Vendor / Player: a name-only label.</summary>
        public byte Kind = RadarKind.Monster;
        public bool IsLabel => Kind != RadarKind.Monster;
        public int Level;                // 0 = unknown
        public string LevelText = "";
        public float HeadHeight = DefaultHeadHeight;
        public WeakTag? Weak;
        public uint Wcid;

        public bool Chosen;              // in the latest snapshot
        public bool Gone;                // live position read failed (despawned / died)
        public bool OnScreen;
        public bool Appeared;            // became visible this frame: layout snaps instead of easing
        public float Alpha;              // smoothed, 0..1
        public float Scale = 1f;         // smoothed
        public float Nudge;              // smoothed crowd offset (px, upward)
        public float NudgeTarget;
        public float ShownRatio = 1f;    // smoothed bar
        public float TrailRatio = 1f;    // "chip" behind the bar after a hit
        public long TrailHoldUntil;
        public bool HpKnown;
        public float Ratio = 1f;
        public uint HpCur, HpMax;
        public float Distance;
        public bool IsTarget, IsAttacker;
        public Vector2 Anchor;           // screen point above the head
        public float LastWx, LastWy, LastWz;
        public bool HasWorld;
        public uint LastCell;            // last live position, AC landblock-local (feet)
        public float LastX, LastY, LastZ;

        // Debuff icons (this frame)
        public readonly DebuffIcon[] Icons = new DebuffIcon[MaxIcons];
        public int IconCount;            // may exceed MaxIcons (overflow)
        public bool Others;              // "debuffed by others" marker

        // Layout (this frame)
        public float Width, Height, BarW, BarH, NameSize, SmallSize, BelowBar, IconSize;
        public ImFontPtr NameFont, SmallFont;
        public float NameW, BadgeW;

        // Cached strings (rebuilt only when the value changes)
        public string HpString = "";
        public long HpKey = long.MinValue;
        public string DistString = "";
        public int DistKey = int.MinValue;
        public string OverflowString = "";
        public int OverflowKey = -1;
    }

    private sealed record WeakTag(string Label, uint Color);

    private const float DefaultHeadHeight = 2.0f;
    private static readonly Dictionary<uint, Plate> Plates = new(64);
    private static readonly List<Plate> DrawOrder = new(64);
    private static readonly List<Plate> Placed = new(64);
    private static readonly List<(uint Id, float D2)> Candidates = new(128);
    private static readonly List<(uint Id, float D2)> LabelCandidates = new(64);
    private static readonly List<uint> Evict = new(16);

    // What each live object is, worked out once per id (item type, BF_PLAYER, BF_VENDOR and
    // PetOwner never change), so the 4 Hz scan skips non-creatures with one lookup and the
    // monster pick no longer reads the bitfield live. Attackable is NOT cached: it is read
    // from the snapshot every scan, as before. Entries not seen for a while are pruned.
    private const byte ClsUnknown = 0, ClsOther = 1, ClsCreature = 2, ClsVendor = 3, ClsPlayer = 4, ClsPet = 5;
    private const byte ClsCreatureNoPwd = 6;   // a creature whose PWD isn't in the snapshot yet: never cached
    private struct ObjClass { public byte Cls; public int Seen; }
    private static readonly Dictionary<uint, ObjClass> Classes = new(512);
    private static readonly List<uint> ClassEvict = new(64);
    private static int _classPass;
    private static readonly Comparison<(uint Id, float D2)> ByD2 = static (a, b) => a.D2.CompareTo(b.D2);
    private static readonly Comparison<Plate> NearFirst = static (a, b) =>
        a.IsTarget != b.IsTarget ? (a.IsTarget ? -1 : 1) : a.Distance.CompareTo(b.Distance);

    private static long _nextSnapshotTicks;
    private static bool _wasInWorld;
    private static volatile bool _hasContent;
    private static uint _playerId;
    private static int _playerLevel;
    private static long _nextPlayerLevelTicks;

    // Weakness ("Weak to") from RynthAi's learned damage data (UiSources.Damage).
    private static bool _damageSubscribed;
    private static long _weakVersion = -1;
    private static Dictionary<uint, WeakTag> _weakByWcid = new();
    private static Dictionary<string, WeakTag> _weakByName = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True while there is anything to draw (fading plates, the self plate, combat text). Any thread.</summary>
    public static bool WantsFrame => _hasContent;

    // Click-to-select (AC's render thread only).
    private static bool _wantsMouse;
    private static uint _hoverId;    // plate under the cursor this frame (0: none)
    private static uint _pressId;    // plate the held left press began on (0: none)

    /// <summary>
    /// True when this frame's plates want the mouse: the cursor is on a clickable
    /// plate, or a press that began on one is still held. Read after the frame is
    /// built, on AC's render thread.
    /// </summary>
    public static bool WantsMouse => _wantsMouse || PlayerPlate.WantsMouse;

    /// <summary>Plates being tracked (diagnostics).</summary>
    public static int PlateCount => Plates.Count;

    // ════════════════════════════════════════════════════════════════════
    //  Update: which monsters get a plate (throttled)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>
    /// AC's render thread, every frame the ImGui layer is on, BEFORE the frame
    /// is built (so <see cref="WantsFrame"/> can ask for one). Cheap when throttled.
    /// </summary>
    public static void Update(bool inWorld)
    {
        // Draw recomputes it; a frame that doesn't draw the plates never wants the mouse.
        _wantsMouse = false;
        PlayerPlate.ClearMouse();
        try
        {
            if (!MonsterHudSettings.Loaded)
            {
                MonsterHudSettings.EnsureLoadQueued();
                _hasContent = false;
                return;
            }

            if (!inWorld || !HudFeed.VisionInstalled)
            {
                if (_wasInWorld)
                {
                    // Logout / character switch (or RynthVision removed): object ids don't carry over.
                    _wasInWorld = false;
                    ClearAll();
                }
                SetDamageSubscription(false);
                _hasContent = false;
                return;
            }
            _wasInWorld = true;

            // Monsters are tracked for the plates, and also for the combat text (it finds
            // "the Drudge you hit" among them) when the plates themselves are off.
            bool monsters = MonsterHudSettings.Enabled;
            bool trackMonsters = monsters || MonsterHudSettings.Numbers;
            // Name-only labels over NPCs (and, if asked, players): their own switches and cap.
            bool track = trackMonsters || MonsterHudSettings.NpcNames || MonsterHudSettings.PlayerNames;
            if (!track) ClearPlates();
            SetDamageSubscription(monsters && MonsterHudSettings.ShowWeakness);
            if (monsters && MonsterHudSettings.ShowDebuffs) PortalSpellTable.EnsureLoadQueued();

            long now = Stopwatch.GetTimestamp();
            if (now >= _nextSnapshotTicks)
            {
                _nextSnapshotTicks = now + Stopwatch.Frequency * SnapshotPeriodMs / 1000;
                RefreshPlayer(now);
                CombatText.PollGains(_playerId);
                if (track) Snapshot(now, trackMonsters);
            }

            _hasContent = Plates.Count > 0 || MonsterHudSettings.SelfPlate || CombatText.HasContent;
        }
        catch (Exception ex)
        {
            _hasContent = false;
            RynthLog.UI($"MonsterHud: update failed - {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void ClearPlates()
    {
        Plates.Clear();
        DrawOrder.Clear();
        ResetClick();
    }

    private static void ResetClick()
    {
        _wantsMouse = false;
        _hoverId = 0;
        _pressId = 0;
    }

    private static void ClearAll()
    {
        ClearPlates();
        MonsterHudData.Clear();
        PlateDebuffs.Clear();
        CombatText.Clear();
        PlayerPlate.Reset();
        Classes.Clear();
        _playerId = 0;
    }

    /// <summary>The player's id, level (con colours) and spell-duration augmentations, every 5 s.</summary>
    private static void RefreshPlayer(long now)
    {
        uint playerId = ClientHelperHooks.GetPlayerId();
        if (playerId == 0) return;
        if (playerId == _playerId && now < _nextPlayerLevelTicks) return;
        if (playerId != _playerId) CombatText.Clear();   // a new character: no gains from the old baseline
        _playerId = playerId;
        _nextPlayerLevelTicks = now + Stopwatch.Frequency * 5;
        _playerLevel = ClientObjectHooks.TryGetObjectIntProperty(playerId, 25, out int lvl) ? lvl : 0;
        // PropertyInt.AugmentationIncreasedSpellDuration: +20 % debuff duration each.
        PlateDebuffs.SetDurationAugmentation(ClientObjectHooks.TryGetObjectIntProperty(playerId, 238, out int aug) ? aug : 0);
    }

    private static void SetDamageSubscription(bool want)
    {
        if (want == _damageSubscribed) return;
        _damageSubscribed = want;
        if (want)
        {
            UiSources.Damage.Subscribe();
            UiSources.Damage.RequestRefresh();
        }
        else
        {
            UiSources.Damage.Unsubscribe();
        }
    }

    private static void Snapshot(long now, bool trackMonsters)
    {
        uint playerId = _playerId;
        if (playerId == 0 || !GameMatrixCapture.HasCapturedFrame || TeleportStateHooks.IsPortaling ||
            !PlayerPhysicsHooks.TryGetPlayerPose(out uint pCell, out float px, out float py, out float pz, out _, out _, out _, out _) ||
            (pCell >> 16) == 0)
        {
            // Portal space, no camera yet, or the player isn't placed: nothing to label.
            ClearPlates();
            return;
        }

        RefreshWeakMaps();

        uint target = ClientHelperHooks.GetSelectedItemId();
        float maxD = MonsterHudSettings.MaxDistance;
        float maxD2 = maxD * maxD;
        float targetD2 = MathF.Max(maxD2, 100f * 100f);   // the target keeps its plate a bit further out
        bool engagedOnly = MonsterHudSettings.Filter == PlateFilter.Engaged && MonsterHudSettings.Enabled;
        bool npcNames = MonsterHudSettings.NpcNames, playerNames = MonsterHudSettings.PlayerNames;
        float npcD = MonsterHudSettings.NpcMaxDistance;
        float npcD2 = npcD * npcD;
        float npcTargetD2 = MathF.Max(npcD2, 100f * 100f);   // a selected NPC keeps its name further out too
        bool targetIsLabel = false;

        int pass = ++_classPass;
        Candidates.Clear();
        LabelCandidates.Clear();
        uint[] ids = ClientObjectHooks.LiveObjectIds;
        for (int i = 0; i < ids.Length; i++)
        {
            uint id = ids[i];
            if (id == playerId) continue;

            // Classified once per id; a cached non-creature costs this one lookup.
            ref ObjClass oc = ref CollectionsMarshal.GetValueRefOrAddDefault(Classes, id, out _);
            oc.Seen = pass;
            byte cls = oc.Cls;
            if (cls == ClsUnknown)
            {
                cls = Classify(id);
                if (cls != ClsCreatureNoPwd) oc.Cls = cls;
            }
            if (cls == ClsOther || cls == ClsUnknown) continue;

            // Monster plate (attackable creature) or name label (NPC / vendor, player)?
            bool label;
            if (cls == ClsPlayer)
            {
                // Players never get a monster plate (attackable in PK or not).
                if (!playerNames) continue;
                label = true;
            }
            else
            {
                if (!ClientObjectHooks.TryGetSnapshotAttackable(id, out bool attackable)) continue;
                if (attackable)
                {
                    if (!trackMonsters) continue;
                    label = false;
                }
                else
                {
                    // Non-attackable: an NPC (vendors included). Pets and creatures not yet
                    // classified (no PWD) get nothing.
                    if (!npcNames || (cls != ClsCreature && cls != ClsVendor)) continue;
                    label = true;
                }
            }

            if (!ClientObjectHooks.TryGetSnapshotPosition(id, out uint cell, out float x, out float y, out float z)) continue;
            if (!TryOffset(pCell, cell, out float ox, out float oy)) continue;
            float dx = x + ox - px, dy = y + oy - py, dz = z - pz;
            float d2 = dx * dx + dy * dy + dz * dz;
            if (label)
            {
                if (d2 > (id == target ? npcTargetD2 : npcD2)) continue;
                LabelCandidates.Add((id, d2));
                if (id == target) targetIsLabel = true;
            }
            else
            {
                if (d2 > (id == target ? targetD2 : maxD2)) continue;
                Candidates.Add((id, d2));
            }
        }
        Candidates.Sort(ByD2);
        LabelCandidates.Sort(ByD2);
        if ((pass & 63) == 0) PruneClasses(pass);

        foreach (Plate p in Plates.Values) p.Chosen = false;

        int cap = MonsterHudSettings.MaxPlates;
        int chosen = 0;
        bool targetChosen = false;
        for (int i = 0; i < Candidates.Count; i++)
        {
            uint id = Candidates[i].Id;
            bool isTarget = id == target;
            if (chosen >= cap && !isTarget)
            {
                if (targetChosen || target == 0) break;
                continue;   // keep scanning only to find the target
            }

            // Players are creatures too (attackable in PK): no plates for them. Classified
            // ids were sorted out in the scan; one whose PWD wasn't snapshotted yet is read live.
            if ((!Classes.TryGetValue(id, out ObjClass oc) || oc.Cls == ClsUnknown) &&
                ClientObjectHooks.TryGetObjectBitfield(id, out uint bf) && (bf & BfPlayer) != 0) continue;

            ClientObjectHooks.TryGetSnapshotName(id, out string name);
            if (engagedOnly && !isTarget && !IsEngaged(id, name, now)) continue;

            Plate plate = GetPlate(id, RadarKind.Monster);
            if (name.Length > 0 && name != plate.Name) plate.Name = name;
            RefreshIdentity(plate);
            chosen++;
            if (isTarget) targetChosen = true;
        }

        // ── Name labels: NPCs (vendors in their own colour) and players, capped apart from the plates ──
        int labelCap = MonsterHudSettings.MaxNpcLabels;
        int labels = 0;
        for (int i = 0; i < LabelCandidates.Count; i++)
        {
            uint id = LabelCandidates[i].Id;
            bool isTarget = id == target;
            if (labels >= labelCap && !isTarget)
            {
                if (!targetIsLabel) break;
                continue;   // keep scanning only to find the target
            }
            if (!ClientObjectHooks.TryGetSnapshotName(id, out string name) || name.Length == 0) continue;

            byte cls = Classes.TryGetValue(id, out ObjClass oc) ? oc.Cls : ClsCreature;
            byte kind = cls == ClsPlayer ? RadarKind.Player : cls == ClsVendor ? RadarKind.Vendor : RadarKind.Npc;
            Plate plate = GetPlate(id, kind);
            // Head height once per name; again while selected (selecting appraises: CreatureType may arrive).
            if (name != plate.Name || isTarget)
            {
                plate.Name = name;
                plate.HeadHeight = EstimateHeadHeight(id, name);
            }
            labels++;
            if (isTarget) targetIsLabel = false;   // found: the cap applies again
        }

        // Plates no longer chosen fade out in Draw and are evicted at zero alpha.
    }

    /// <summary>The plate for <paramref name="id"/> (made on first sight), marked chosen this snapshot.</summary>
    private static Plate GetPlate(uint id, byte kind)
    {
        if (!Plates.TryGetValue(id, out Plate? plate))
        {
            plate = new Plate { Id = id, Alpha = 0f };
            Plates[id] = plate;
        }
        plate.Kind = kind;
        plate.Chosen = true;
        plate.Gone = false;
        return plate;
    }

    /// <summary>
    /// What an object is, from the main-thread snapshots only (no AC call): not a creature,
    /// a player, a summoned pet, a vendor or another creature. ClsUnknown / ClsCreatureNoPwd
    /// when the snapshots don't have it yet (asked again next scan).
    /// </summary>
    private static byte Classify(uint id)
    {
        if (!ClientObjectHooks.TryGetSnapshotItemType(id, out uint type)) return ClsUnknown;
        if ((type & TypeCreature) == 0) return ClsOther;
        if (!ClientObjectHooks.TryGetSnapshotPwdInfo(id, out uint bf, out _, out _)) return ClsCreatureNoPwd;
        if ((bf & BfPlayer) != 0) return ClsPlayer;
        if (ClientObjectHooks.TryGetObjectInstanceIdProperty(id, PetOwnerIid, out uint owner) && owner != 0) return ClsPet;
        return (bf & BfVendor) != 0 ? ClsVendor : ClsCreature;
    }

    /// <summary>Drops class entries for ids the last scan didn't see (despawned / out of range). Every 64 scans (~16 s).</summary>
    private static void PruneClasses(int pass)
    {
        ClassEvict.Clear();
        foreach (KeyValuePair<uint, ObjClass> kv in Classes)
            if (kv.Value.Seen != pass) ClassEvict.Add(kv.Key);
        foreach (uint id in ClassEvict) Classes.Remove(id);
    }

    private static bool IsEngaged(uint id, string name, long now)
    {
        if (MonsterHudData.AttackedRecently(name, AttackerMemorySec)) return true;
        return MonsterHudData.TryGetHealth(id, out HealthObservation h) && h.Ratio < 0.999f &&
               (now - h.Ticks) < (long)(HurtMemorySec * Stopwatch.Frequency);
    }

    /// <summary>Level, creature type (appraisal) and weakness (RynthAi): all can arrive after the plate appears.</summary>
    private static void RefreshIdentity(Plate p)
    {
        p.HeadHeight = EstimateHeadHeight(p.Id, p.Name);
        if (AppraisalHooks.TryGetCachedIntProperty(p.Id, 25, out int lvl) && lvl > 0 && lvl != p.Level)
        {
            p.Level = lvl;
            p.LevelText = lvl.ToString(CultureInfo.InvariantCulture);
        }

        if (!MonsterHudSettings.ShowWeakness) { p.Weak = null; return; }
        if (p.Wcid == 0) ClientObjectHooks.TryGetObjectWcid(p.Id, out p.Wcid);
        if (p.Wcid != 0 && _weakByWcid.TryGetValue(p.Wcid, out WeakTag? w)) p.Weak = w;
        else if (p.Name.Length > 0 && _weakByName.TryGetValue(p.Name, out w)) p.Weak = w;
        else p.Weak = null;
    }

    /// <summary>
    /// Metres from the landblock-local frame of <paramref name="fromCell"/> to that of
    /// <paramref name="toCell"/> (192 m per landblock step). False for far-apart
    /// landblocks (another dungeon, a bogus cell).
    /// </summary>
    internal static bool TryOffset(uint fromCell, uint toCell, out float ox, out float oy)
    {
        int fx = (int)((fromCell >> 24) & 0xFF), fy = (int)((fromCell >> 16) & 0xFF);
        int tx = (int)((toCell >> 24) & 0xFF), ty = (int)((toCell >> 16) & 0xFF);
        int dx = tx - fx, dy = ty - fy;
        ox = dx * LandblockSize;
        oy = dy * LandblockSize;
        return (toCell >> 16) != 0 && Math.Abs(dx) <= 2 && Math.Abs(dy) <= 2;
    }

    // ── Head height ──────────────────────────────────────────────────────

    /// <summary>
    /// Metres from the feet to just above the head. The client's own model
    /// height isn't read (its CPartArray/CSetup layout isn't mapped here), so
    /// this goes by CreatureType from the appraisal cache, then by name, then
    /// a humanoid default; "Height lift" in the settings corrects the rest.
    /// </summary>
    internal static float EstimateHeadHeight(uint id, string name)
    {
        if (AppraisalHooks.TryGetCachedIntProperty(id, 2, out int ct) && ct > 0 && ct < CreatureTypeHeights.Length &&
            CreatureTypeHeights[ct] > 0)
            return CreatureTypeHeights[ct];

        foreach ((string key, float h) in NameHeights)
            if (name.Contains(key, StringComparison.OrdinalIgnoreCase))
                return h;
        return DefaultHeadHeight;
    }

    // Indexed by AC CreatureType (PropertyInt 2); 0 = use the default.
    private static readonly float[] CreatureTypeHeights =
    {
        0,
        2.3f, // 1 Olthoi
        2.4f, // 2 Banderling
        1.7f, // 3 Drudge
        2.0f, // 4 Mosswart
        3.3f, // 5 Lugian
        2.1f, // 6 Tumerok
        1.0f, // 7 Mite
        3.2f, // 8 Tusker
        1.6f, // 9 Phyntos Wasp
        0.8f, // 10 Rat
        2.2f, // 11 Auroch
        2.0f, // 12 Cow
        3.4f, // 13 Golem
        2.1f, // 14 Undead
        1.4f, // 15 Gromnie
        1.4f, // 16 Reedshark
        1.2f, // 17 Armoredillo
        2.0f, // 18 Fae
        2.3f, // 19 Virindi
        1.8f, // 20 Wisp
        2.2f, // 21 Knathtead
        2.3f, // 22 Shadow
        2.0f, // 23 Mattekar
        2.1f, // 24 Mumiyah
        0.8f, // 25 Rabbit
        2.0f, // 26 Sclavus
        1.4f, // 27 Shallows Shark
        2.8f, // 28 Monouga
        2.1f, // 29 Zombie
        2.1f, // 30 Skeleton
        2.1f, // 31 Human
        2.2f, // 32 Shreth
        1.4f, // 33 Chittick
        2.2f, // 34 Moarsman
        1.0f, // 35 Olthoi Larvae
        1.8f, // 36 Slithis
        2.0f, // 37 Deru
        2.6f, // 38 Fire Elemental
        2.4f, // 39 Snowman
        0,    // 40 Unknown
        0.8f, // 41 Bunny
        2.6f, // 42 Lightning Elemental
        2.6f, // 43 Rockslide
        2.7f, // 44 Grievver
        1.8f, // 45 Niffis
        2.8f, // 46 Ursuin
        2.6f, // 47 Crystal
        2.1f, // 48 Hollow Minion
        2.4f, // 49 Scarecrow
        2.8f, // 50 Idol
    };

    private static readonly (string Key, float Height)[] NameHeights =
    {
        ("Golem", 3.4f), ("Tusker", 3.2f), ("Lugian", 3.3f), ("Ursuin", 2.8f), ("Monouga", 2.8f),
        ("Grievver", 2.7f), ("Banderling", 2.4f), ("Olthoi", 2.3f), ("Mite", 1.0f), ("Rat", 0.8f),
        ("Rabbit", 0.8f), ("Bunny", 0.8f), ("Gromnie", 1.4f), ("Drudge", 1.7f), ("Wasp", 1.6f),
        ("Armoredillo", 1.2f), ("Chittick", 1.4f), ("Reedshark", 1.4f), ("Chicken", 0.9f),
    };

    // ── Weakness ─────────────────────────────────────────────────────────

    private static void RefreshWeakMaps()
    {
        if (!MonsterHudSettings.ShowWeakness) return;
        var snap = UiSources.Damage.Current;
        if (snap == null || snap.Version == _weakVersion) return;
        _weakVersion = snap.Version;
        var byWcid = new Dictionary<uint, WeakTag>();
        var byName = new Dictionary<string, WeakTag>(StringComparer.OrdinalIgnoreCase);
        foreach (DamageRow r in snap.Value.Rows)
        {
            if (r.IsDefault || string.IsNullOrEmpty(r.Weak)) continue;
            WeakTag? tag = ParseWeak(r.Weak);
            if (tag == null) continue;
            if (r.Wcid != 0) byWcid.TryAdd(r.Wcid, tag);
            if (r.Name.Length > 0) byName.TryAdd(r.Name, tag);
        }
        _weakByWcid = byWcid;
        _weakByName = byName;
    }

    /// <summary>"Bludgeon 1.0, Pierce 0.86" -> the first (most damage taken) element.</summary>
    private static WeakTag? ParseWeak(string weak)
    {
        int comma = weak.IndexOf(',');
        string first = (comma > 0 ? weak[..comma] : weak).Trim();
        int sp = first.IndexOf(' ');
        string elem = sp > 0 ? first[..sp] : first;
        return elem.ToLowerInvariant() switch
        {
            "slash" or "slashing" => new WeakTag("SL", ElemSlash),
            "pierce" or "piercing" => new WeakTag("PI", ElemPierce),
            "bludgeon" or "bludgeoning" => new WeakTag("BL", ElemBludgeon),
            "fire" => new WeakTag("FI", ElemFire),
            "cold" => new WeakTag("CO", ElemCold),
            "lightning" or "electric" => new WeakTag("LI", ElemLightning),
            "acid" => new WeakTag("AC", ElemAcid),
            "nether" => new WeakTag("NE", ElemNether),
            _ => null,
        };
    }

    // ════════════════════════════════════════════════════════════════════
    //  Draw: project and paint (every frame, inside the ImGui frame)
    // ════════════════════════════════════════════════════════════════════

    /// <summary>What every overlay needs to project a world point this frame.</summary>
    internal struct Frame
    {
        public uint FrameCell, PlayerCell;
        public float Px, Py, Pz;         // player, AC landblock-local (feet)
        public float MapX, MapY;         // viewport px -> ImGui display px
        public Vector2 Display;
        public float Dt;
        public long Now;
        public float UiScale;
        public ImDrawListPtr Dl;
    }

    /// <summary>AC's render thread, inside the ImGui frame (between NewFrame and Render).</summary>
    public static void Draw(float uiScale)
    {
        if (!_hasContent) { ResetClick(); PlayerPlate.DropDrag(); return; }
        try
        {
            if (!TryBeginFrame(uiScale, out Frame f)) { ResetClick(); PlayerPlate.DropDrag(); return; }
            DrawPlates(ref f, MonsterHudSettings.Enabled);
            PlayerPlate.Draw(ref f);
            CombatText.Draw(ref f);
            // The player plate fixed on screen: ImGui's background list (the normal UI pass), not the layer above.
            PlayerPlate.DrawScreen(ref f);
        }
        catch (Exception ex)
        {
            RynthLog.UI($"MonsterHud: draw failed - {ex.GetType().Name}: {ex.Message}");
            ClearPlates();
            CombatText.Clear();
            _hasContent = false;
        }
    }

    private static bool TryBeginFrame(float uiScale, out Frame f)
    {
        f = default;
        if (!GameMatrixCapture.HasCapturedFrame) return false;
        uint vpW = GameMatrixCapture.ViewportWidth, vpH = GameMatrixCapture.ViewportHeight;
        if (vpW == 0 || vpH == 0) return false;
        if (!PlayerPhysicsHooks.TryGetPlayerPose(out uint pCell, out float px, out float py, out float pz, out _, out _, out _, out _) ||
            (pCell >> 16) == 0 || TeleportStateHooks.IsPortaling)
            return false;

        uint frameCell = GameMatrixCapture.FrameLandblock << 16;
        if (frameCell == 0) frameCell = pCell;

        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        Vector2 display = io.DisplaySize;
        if (display.X <= 1 || display.Y <= 1) return false;
        f.FrameCell = frameCell;
        f.PlayerCell = pCell;
        f.Px = px; f.Py = py; f.Pz = pz;
        f.MapX = display.X / vpW;
        f.MapY = display.Y / vpH;
        f.Display = display;
        f.Dt = Math.Clamp(io.DeltaTime, 0.001f, 0.1f);
        f.Now = Stopwatch.GetTimestamp();
        f.UiScale = uiScale;
        // Our own list, drawn at AC's 3D->UI transition so AC's windows cover the
        // plates (UnderUiLayer). The background list (over AC's UI) only if the
        // layer isn't available.
        f.Dl = UnderUiLayer.BeginBuild(display);
        if (!UnderUiLayer.IsBuilding) f.Dl = ImGuiNET.ImGui.GetBackgroundDrawList();
        return true;
    }

    /// <summary>
    /// An AC landblock-local point (x east, y north, z up) to ImGui screen pixels,
    /// rounded. False when it is behind the camera or far outside the screen.
    /// </summary>
    internal static bool Project(ref Frame f, uint cell, float x, float y, float z, out Vector2 screen, bool keepOnScreen = false)
    {
        screen = default;
        if (!TryOffset(f.FrameCell, cell, out float fx, out float fy)) return false;
        if (!GameMatrixCapture.WorldToScreen(x + fx, z, y + fy, out float sx, out float sy)) return false;
        sx *= f.MapX; sy *= f.MapY;
        if (keepOnScreen && MonsterHudSettings.KeepOnScreen)
        {
            // In front of the camera but past an edge (the camera closes in near walls): pin to the edge.
            Vector2 c = ClampToScreen(ref f, sx, sy, 24f * f.UiScale, 30f * f.UiScale);
            screen = new Vector2(MathF.Round(c.X), MathF.Round(c.Y));
            return true;
        }
        const float margin = 200f;
        if (sx < -margin || sx > f.Display.X + margin || sy < -margin || sy > f.Display.Y + margin) return false;
        screen = new Vector2(MathF.Round(sx), MathF.Round(sy));
        return true;
    }

    /// <summary>A screen point pulled inside the display, <paramref name="side"/> px from the left/right/bottom
    /// edges and <paramref name="top"/> px from the top (room for what is drawn above the point).</summary>
    internal static Vector2 ClampToScreen(ref Frame f, float sx, float sy, float side, float top) =>
        new(Math.Clamp(sx, side, MathF.Max(side, f.Display.X - side)),
            Math.Clamp(sy, top, MathF.Max(top, f.Display.Y - side)));

    /// <summary>
    /// Live positions for every tracked monster and name label; the monster plates themselves
    /// only when <paramref name="paint"/> (labels exist only while their own switch is on).
    /// </summary>
    private static void DrawPlates(ref Frame f, bool paint)
    {
        float monsterMaxD = MonsterHudSettings.MaxDistance;
        float labelMaxD = MonsterHudSettings.NpcMaxDistance;
        float opacity = MonsterHudSettings.Opacity;
        float userScale = MonsterHudSettings.Scale;
        float lift = MonsterHudSettings.HeightLift;
        bool fade = MonsterHudSettings.FadeWithDistance;
        bool debuffs = MonsterHudSettings.ShowDebuffs;
        bool others = debuffs && MonsterHudSettings.ShowOthersDebuff;
        float dt = f.Dt;
        float kAlpha = 1f - MathF.Exp(-dt * 12f);
        float kScale = 1f - MathF.Exp(-dt * 10f);
        float kBar = 1f - MathF.Exp(-dt * 14f);
        uint target = ClientHelperHooks.GetSelectedItemId();

        DrawOrder.Clear();
        Evict.Clear();
        foreach (Plate p in Plates.Values)
        {
            // ── Live position (main thread; ~MaxPlates reads) ──
            if (!p.Gone && ClientObjectHooks.TryGetObjectPosition(p.Id, out uint cell, out float x, out float y, out float z) &&
                TryOffset(f.FrameCell, cell, out float fx, out float fy) && TryOffset(f.PlayerCell, cell, out float ox, out float oy))
            {
                float dx = x + ox - f.Px, dy = y + oy - f.Py, dz = z - f.Pz;
                p.Distance = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
                p.LastWx = x + fx;                         // D3D x = east
                p.LastWy = z + p.HeadHeight + lift;        // D3D y = up
                p.LastWz = y + fy;                         // D3D z = north
                p.LastCell = cell; p.LastX = x; p.LastY = y; p.LastZ = z;
                p.HasWorld = true;
            }
            else
            {
                p.Gone = true;   // despawned / died / out of reach: fade where it was
            }

            bool label = p.IsLabel;
            if (!paint && !label)
            {
                // Tracked for the combat text only: no plate, no fade.
                p.Alpha = 0f;
                if (!p.Chosen || p.Gone) Evict.Add(p.Id);
                continue;
            }

            p.IsTarget = p.Id == target && target != 0;
            p.IsAttacker = !label && MonsterHudData.AttackedRecently(p.Name, AttackerMemorySec);

            p.OnScreen = false;
            if (p.HasWorld && GameMatrixCapture.WorldToScreen(p.LastWx, p.LastWy, p.LastWz, out float sx, out float sy))
            {
                sx *= f.MapX; sy *= f.MapY;
                const float margin = 60f;
                if (MonsterHudSettings.KeepOnScreen)
                {
                    // Pinned to the edge: room above the anchor for the name, bar and debuff row (a label: the name).
                    Vector2 c = ClampToScreen(ref f, sx, sy, (label ? 50f : 70f) * f.UiScale * userScale,
                        (label ? 22f : 56f) * f.UiScale * userScale);
                    p.Anchor = new Vector2(MathF.Round(c.X), MathF.Round(c.Y));
                    p.OnScreen = true;
                }
                else if (sx > -margin && sx < f.Display.X + margin && sy > -margin && sy < f.Display.Y + margin)
                {
                    // Whole pixels: text and 1 px lines stay crisp and a still monster never shimmers.
                    p.Anchor = new Vector2(MathF.Round(sx), MathF.Round(sy));
                    p.OnScreen = true;
                }
            }

            // ── Alpha / scale targets ──
            float maxD = label ? labelMaxD : monsterMaxD;
            float t = maxD > 1f ? Math.Clamp(p.Distance / maxD, 0f, 1f) : 0f;
            float distFade = fade ? 1f - 0.55f * SmoothStep(0.45f, 1f, t) : 1f;
            bool show = p.OnScreen && (p.Chosen || p.IsTarget) && !p.Gone;
            float alphaTarget = show ? opacity * (p.IsTarget ? 1f : distFade) : 0f;
            bool wasVisible = p.Alpha > 0.01f;
            p.Appeared = !wasVisible;
            if (!p.OnScreen)
                p.Alpha = 0f;   // behind the camera / off screen: no stale position, pop back in with a fade
            else
                p.Alpha += (alphaTarget - p.Alpha) * kAlpha;

            float distScale = fade ? 1f - 0.28f * SmoothStep(0.15f, 1f, t) : 1f;
            float scaleTarget = userScale * distScale * (p.IsTarget ? 1.12f : 1f);
            // Appearing: start at the right size; visible: ease (target pop, distance).
            p.Scale = wasVisible ? p.Scale + (scaleTarget - p.Scale) * kScale : scaleTarget;

            // ── Health (monster plates only) ──
            if (!label) UpdateHealth(p, f.Now, kBar, dt);

            if (!show && p.Alpha < 0.01f)
            {
                if (!p.Chosen || p.Gone) Evict.Add(p.Id);
                continue;
            }
            if (p.OnScreen && p.Alpha >= 0.01f)
            {
                // ── Debuffs (only for plates that draw) ──
                p.Others = false;
                p.IconCount = debuffs && !label ? PlateDebuffs.GetIcons(p.Id, p.Icons, out p.Others) : 0;
                if (!others) p.Others = false;
                DrawOrder.Add(p);
            }
        }
        foreach (uint id in Evict) Plates.Remove(id);
        if (DrawOrder.Count == 0) { ResetClick(); return; }

        // ── Layout: nearest (and the target) claim their spot; others step up ──
        DrawOrder.Sort(NearFirst);
        Placed.Clear();
        foreach (Plate p in DrawOrder)
        {
            Measure(p, f.UiScale);
            ResolveOverlap(p, f.UiScale);
            p.Nudge = p.Appeared ? p.NudgeTarget : p.Nudge + (p.NudgeTarget - p.Nudge) * (1f - MathF.Exp(-dt * 10f));
            Placed.Add(p);
        }

        // ── Click-to-select: which plate is under the cursor, and a press on it ──
        PlateClick(f.UiScale);

        // ── Paint: farthest first, nearest (and the target) on top ──
        for (int i = DrawOrder.Count - 1; i >= 0; i--)
        {
            Plate p = DrawOrder[i];
            if (p.IsLabel) PaintLabel(f.Dl, p, f.UiScale);
            else Paint(f.Dl, p, f.UiScale, f.Now);
        }
    }

    internal static float SmoothStep(float e0, float e1, float x)
    {
        float t = Math.Clamp((x - e0) / (e1 - e0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static void UpdateHealth(Plate p, long now, float kBar, float dt)
    {
        if (MonsterHudData.TryGetHealth(p.Id, out HealthObservation h))
        {
            p.HpKnown = true;
            p.Ratio = h.Ratio;
            uint max = h.Max;
            if (ObjectQualityCache.TryGetCreatureVitals(p.Id, out CreatureVitals v) && v.MaxHealth > 0)
                max = v.MaxHealth;   // the appraisal CreatureProfile's absolute max wins
            p.HpMax = max;
            p.HpCur = max > 0 ? (uint)MathF.Round(max * h.Ratio) : 0;
        }
        else
        {
            // Never observed: an untouched monster is at full health in practice.
            p.HpKnown = false;
            p.Ratio = 1f;
            p.HpMax = ObjectQualityCache.TryGetCreatureVitals(p.Id, out CreatureVitals v) ? v.MaxHealth : 0;
            p.HpCur = p.HpMax;
        }

        if (p.Alpha <= 0.01f)
        {
            // Not visible: snap, so a plate never appears mid-animation.
            p.ShownRatio = p.TrailRatio = p.Ratio;
            p.TrailHoldUntil = 0;
            return;
        }
        StepBar(ref p.ShownRatio, ref p.TrailRatio, ref p.TrailHoldUntil, p.Ratio, now, kBar, dt);
    }

    /// <summary>
    /// Eases a bar toward <paramref name="ratio"/> and runs its "chip": after a
    /// drop the chip holds 0.45 s, then drains at 80 % of the bar per second;
    /// a rise pulls it up at once. Shared with the player plate.
    /// </summary>
    internal static void StepBar(ref float shown, ref float trail, ref long holdUntil, float ratio, long now, float kBar, float dt)
    {
        shown += (ratio - shown) * kBar;
        if (ratio > trail)
        {
            trail = ratio;       // healed: the chip follows up at once
        }
        else if (ratio < trail - 0.001f)
        {
            if (holdUntil == 0) holdUntil = now + Stopwatch.Frequency * 45 / 100;
            if (now >= holdUntil)
                trail = MathF.Max(ratio, trail - 0.8f * dt);
        }
        else
        {
            holdUntil = 0;
        }
    }

    // ── Layout ───────────────────────────────────────────────────────────

    private static void Measure(Plate p, float uiScale)
    {
        float s = MathF.Max(0.3f, p.Scale);
        float k = s * uiScale;

        if (p.IsLabel)
        {
            // Name only: the same bold bake as a monster's name, no bar, badge or icons.
            p.NameSize = MathF.Round(ImGuiFonts.Get(UiFont.UiBold11).FontSize * s);
            p.NameFont = ImGuiFonts.Sharp(p.NameSize, bold: true);
            p.NameW = TextWidth(p.NameFont, p.NameSize, p.Name);
            p.BadgeW = 0f;
            p.BarW = p.BarH = 0f;
            p.IconSize = 0f;
            p.BelowBar = p.IsTarget ? 9f * k : 3f * k;   // the target pointer hangs under the name
            p.Width = p.NameW + 8f * k;
            p.Height = p.NameSize + 2f * k + p.BelowBar;
            return;
        }

        p.BarW = MathF.Round(112f * k);
        p.BarH = MathF.Round((MonsterHudSettings.ShowHpNumbers ? 12f : 7f) * k);

        // Sizes follow the 11 px bold / 9 px regular bakes; the font drawn is the smallest bake at
        // least that big, so a scaled-up plate shrinks a bigger bake instead of stretching (blur).
        p.NameSize = MathF.Round(ImGuiFonts.Get(UiFont.UiBold11).FontSize * s);
        p.NameFont = ImGuiFonts.Sharp(p.NameSize, bold: true);
        p.SmallSize = MathF.Round(ImGuiFonts.Get(UiFont.Ui9).FontSize * s);
        p.SmallFont = ImGuiFonts.Sharp(p.SmallSize, bold: false);

        p.NameW = MonsterHudSettings.ShowNames && p.Name.Length > 0 ? TextWidth(p.NameFont, p.NameSize, p.Name) : 0f;
        p.BadgeW = MonsterHudSettings.ShowLevel && p.LevelText.Length > 0
            ? TextWidth(p.SmallFont, p.SmallSize, p.LevelText) + 8f * k : 0f;

        // Debuff row under the bar: icon + countdown line.
        p.IconSize = MathF.Round(13f * k);
        float iconRow = p.IconCount > 0 || p.Others ? p.IconSize + MathF.Round(3f * k) + 3f : 0f;
        p.BelowBar = iconRow + (p.IsTarget ? 9f * k : 3f * k);

        float nameLine = p.NameW + (p.BadgeW > 0 ? p.BadgeW + 4f * k : 0f);
        float nameH = (p.NameW > 0 || p.BadgeW > 0) ? p.NameSize + 2f * k : 0f;
        // The weak pill (left) and distance (right) hang off the bar's ends: keep them
        // inside the crowd-avoidance box, symmetric so the plate stays centred.
        float side = (p.Weak != null || MonsterHudSettings.ShowDistance) ? 2f * 30f * k : 0f;
        p.Width = MathF.Max(p.BarW + side, nameLine) + 8f * k;
        p.Height = nameH + p.BarH + p.BelowBar;
    }

    /// <summary>Screen rect of a plate at a given upward nudge: (x0, y0, x1, y1).</summary>
    private static Vector4 RectAt(Plate p, float nudge, float uiScale)
    {
        float k = p.Scale * uiScale;
        float bottom = p.Anchor.Y - 4f * k - nudge + p.BelowBar;
        return new Vector4(p.Anchor.X - p.Width * 0.5f, bottom - p.Height, p.Anchor.X + p.Width * 0.5f, bottom);
    }

    private static void ResolveOverlap(Plate p, float uiScale)
    {
        float gap = 2f * uiScale;
        float nudge = 0f;
        float maxNudge = 160f * uiScale;
        for (int pass = 0; pass < 8; pass++)
        {
            Vector4 r = RectAt(p, nudge, uiScale);
            bool moved = false;
            foreach (Plate q in Placed)
            {
                Vector4 o = RectAt(q, q.NudgeTarget, uiScale);
                if (r.X >= o.Z || r.Z <= o.X || r.Y >= o.W || r.W <= o.Y) continue;
                // Step above the plate it hits.
                float need = nudge + (r.W - o.Y) + gap;
                if (need > nudge) { nudge = need; moved = true; r = RectAt(p, nudge, uiScale); }
            }
            if (!moved || nudge >= maxNudge) break;
        }
        p.NudgeTarget = MathF.Min(nudge, maxNudge);
    }

    // ── Click-to-select ──────────────────────────────────────────────────

    /// <summary>
    /// Finds the plate under the cursor (the topmost: DrawOrder[0] paints last) and
    /// turns a left press on it into a select. A plate only counts when click-to-select
    /// is on, the Insert UI toggle is on and no ImGui panel or popup is under the
    /// cursor (or holding the mouse). The press must already be the plate's when it
    /// arrives: the game window ate it because the previous frame's capture flags
    /// (Win32Backend.MouseCaptured) were up, so AC never saw it. The press selects at
    /// once (as a click in the 3D world does) and the capture stays until the button
    /// is released, so the release never reaches AC either. One select per press.
    /// </summary>
    private static void PlateClick(float uiScale)
    {
        uint prevHover = _hoverId;   // the plate whose hover put the capture up for this frame's input
        _hoverId = 0;
        ImGuiIOPtr io = ImGuiNET.ImGui.GetIO();
        bool usable = MonsterHudSettings.ClickToSelect && Win32Backend.IsUiCaptureEnabled() && !io.WantCaptureMouse;
        if (usable)
        {
            Vector2 m = io.MousePos;
            foreach (Plate p in DrawOrder)
            {
                if (p.Gone || p.Alpha < 0.25f) continue;   // fading in or out: not a target yet
                Vector4 r = RectAt(p, p.Nudge, uiScale);
                if (m.X < r.X || m.X > r.Z || m.Y < r.Y || m.Y > r.W) continue;
                _hoverId = p.Id;
                break;
            }
        }

        if (_pressId == 0)
        {
            // The press went where last frame's hover sent it: if the cursor slipped off
            // that plate as it pressed, the click was still eaten for it, so it still selects it.
            uint target = _hoverId != 0 ? _hoverId : prevHover;
            // Another button held (e.g. the game's right-drag camera) keeps the press with the game.
            if (usable && target != 0 && Win32Backend.MouseCaptured && ImGuiNET.ImGui.IsMouseClicked(ImGuiMouseButton.Left)
                && !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Right) && !ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                _pressId = target;
                AcMainThreadQueue.EnqueueOverlaySelect(target);
            }
        }
        else if (!ImGuiNET.ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            _pressId = 0;   // released: the capture can drop
        }

        _wantsMouse = _hoverId != 0 || _pressId != 0;
    }

    /// <summary>Text width at <paramref name="size"/> px. No font push: this runs outside any window.</summary>
    internal static float TextWidth(ImFontPtr font, float size, string text) =>
        font.CalcTextSizeA(size, float.MaxValue, 0f, text).X;

    // ── Paint ────────────────────────────────────────────────────────────

    private static void Paint(ImDrawListPtr dl, Plate p, float uiScale, long now)
    {
        float a = Math.Clamp(p.Alpha, 0f, 1f);
        float k = MathF.Max(0.3f, p.Scale) * uiScale;
        float cx = p.Anchor.X;
        float barBottom = MathF.Round(p.Anchor.Y - 4f * k - p.Nudge);
        float barTop = barBottom - p.BarH;
        float barL = MathF.Round(cx - p.BarW * 0.5f), barR = barL + p.BarW;
        var b0 = new Vector2(barL, barTop);
        var b1 = new Vector2(barR, barBottom);
        float round = MathF.Max(1f, 2f * k);
        bool icons = p.IconCount > 0 || p.Others;
        float iconTop = barBottom + MathF.Round(3f * k);
        float pointerTop = icons ? iconTop + p.IconSize + 3f + 2f * k : barBottom + 2f * k;

        // Crowd leader: a plate pushed up keeps a faint line down to its monster.
        if (p.Nudge > 10f * uiScale)
            dl.AddLine(new Vector2(cx, pointerTop - 1), new Vector2(cx, p.Anchor.Y - 2f * k), Mul(Leader, a), 1f);

        // ── Target: glow + gold frame + pointer ──
        if (p.IsTarget)
        {
            float g = 3f * k;
            dl.AddRectFilled(b0 - new Vector2(g, g), b1 + new Vector2(g, g), Mul(GoldGlow, a), round + g);
            float tri = MathF.Round(5f * k);
            var t0 = new Vector2(cx - tri, pointerTop);
            var t1 = new Vector2(cx + tri, pointerTop);
            var t2 = new Vector2(cx, pointerTop + tri);
            dl.AddTriangleFilled(t0 - new Vector2(1, 1), t1 + new Vector2(1, -1), t2 + new Vector2(0, 1.5f), Mul(Outline, a));
            dl.AddTriangleFilled(t0, t1, t2, Mul(GoldBright, a));
        }

        // ── Bar ──
        PaintBar(dl, b0, b1, round, a, p.ShownRatio, p.TrailRatio, p.HpKnown ? HealthColor(p.Ratio) : UnknownFill, p.BarW >= 60f);

        if (p.IsTarget)
            dl.AddRect(b0 - new Vector2(1.5f, 1.5f), b1 + new Vector2(1.5f, 1.5f), Mul(Gold, a), round + 1.5f, ImDrawFlags.None, 1.5f);
        else if (p.Id == _hoverId)
            dl.AddRect(b0 - new Vector2(1.5f, 1.5f), b1 + new Vector2(1.5f, 1.5f), Mul(HoverEdge, a), round + 1.5f, ImDrawFlags.None, 1f);   // click-to-select hover

        // HP numbers inside the bar.
        if (MonsterHudSettings.ShowHpNumbers && p.HpKnown && p.BarH >= 9f)
        {
            string hp = HpString(p);
            if (hp.Length > 0)
            {
                float w = TextWidth(p.SmallFont, p.SmallSize, hp);
                var pos = new Vector2(MathF.Round(cx - w * 0.5f), MathF.Round(barTop + (p.BarH - p.SmallSize) * 0.5f));
                OutlinedText(dl, p.SmallFont, p.SmallSize, pos, a, hp);
                dl.AddText(p.SmallFont, p.SmallSize, pos, Mul(HpText, a), hp);
            }
        }

        // Left of the bar: the "weak to" element pill.
        if (p.Weak != null)
        {
            float pw = TextWidth(p.SmallFont, p.SmallSize, p.Weak.Label) + 6f * k;
            float ph = MathF.Max(p.BarH + 2f, p.SmallSize + 2f);
            var q0 = new Vector2(MathF.Round(barL - 4f * k - pw), MathF.Round(barTop + (p.BarH - ph) * 0.5f));
            var q1 = q0 + new Vector2(pw, ph);
            dl.AddRectFilled(q0, q1, Mul(BadgeBack, a), 3f * k);
            dl.AddRect(q0, q1, Mul(p.Weak.Color, a * 0.9f), 3f * k, ImDrawFlags.None, 1f);
            var tp = new Vector2(MathF.Round(q0.X + 3f * k), MathF.Round(q0.Y + (ph - p.SmallSize) * 0.5f));
            dl.AddText(p.SmallFont, p.SmallSize, tp, Mul(p.Weak.Color, a), p.Weak.Label);
        }

        // Distance, right of the bar.
        if (MonsterHudSettings.ShowDistance)
        {
            string d = DistString(p);
            var pos = new Vector2(MathF.Round(barR + 4f * k), MathF.Round(barTop + (p.BarH - p.SmallSize) * 0.5f));
            OutlinedText(dl, p.SmallFont, p.SmallSize, pos, a, d);
            dl.AddText(p.SmallFont, p.SmallSize, pos, Mul(DimText, a), d);
        }

        // Under the bar: the debuff icons.
        if (icons)
            PaintIcons(dl, p, cx, iconTop, k, a, now);

        // ── Name line: [level badge] Name ──
        if (p.NameW > 0 || p.BadgeW > 0)
        {
            float lineW = p.NameW + (p.BadgeW > 0 ? p.BadgeW + (p.NameW > 0 ? 4f * k : 0f) : 0f);
            float x = MathF.Round(cx - lineW * 0.5f);
            float nameTop = MathF.Round(barTop - 2f * k - p.NameSize);

            if (p.BadgeW > 0)
            {
                float bh = p.SmallSize + 2f * k;
                var c0 = new Vector2(x, MathF.Round(nameTop + (p.NameSize - bh) * 0.5f + 1f));
                var c1 = c0 + new Vector2(p.BadgeW, bh);
                uint con = ConColor(p.Level);
                dl.AddRectFilled(c0, c1, Mul(BadgeBack, a), 3f * k);
                dl.AddRect(c0, c1, Mul(p.IsTarget ? Gold : BadgeEdge, a), 3f * k, ImDrawFlags.None, 1f);
                var lp = new Vector2(MathF.Round(c0.X + 4f * k), MathF.Round(c0.Y + (bh - p.SmallSize) * 0.5f));
                dl.AddText(p.SmallFont, p.SmallSize, lp, Mul(con, a), p.LevelText);
                x += p.BadgeW + 4f * k;
            }

            if (p.NameW > 0)
            {
                uint col = p.IsTarget ? GoldBright : p.IsAttacker ? AttackerText : NameText;
                var pos = new Vector2(x, nameTop);
                OutlinedText(dl, p.NameFont, p.NameSize, pos, a, p.Name);
                dl.AddText(p.NameFont, p.NameSize, pos, Mul(col, a), p.Name);
            }
        }
    }

    /// <summary>
    /// A name-only label (NPC, vendor, player): the name in the radar's colour for that
    /// kind (NPC yellow, vendor gold, player blue; the radar's colour picks apply), the
    /// crowd leader line, the gold pointer when selected and an underline on hover.
    /// </summary>
    private static void PaintLabel(ImDrawListPtr dl, Plate p, float uiScale)
    {
        if (p.NameW <= 0f) return;
        float a = Math.Clamp(p.Alpha, 0f, 1f);
        float k = MathF.Max(0.3f, p.Scale) * uiScale;
        float cx = p.Anchor.X;
        float baseY = MathF.Round(p.Anchor.Y - 4f * k - p.Nudge);   // the name's bottom
        float nameTop = MathF.Round(baseY - p.NameSize);
        float x = MathF.Round(cx - p.NameW * 0.5f);
        float pointerTop = baseY + 2f * k;

        if (p.Nudge > 10f * uiScale)
            dl.AddLine(new Vector2(cx, pointerTop - 1), new Vector2(cx, p.Anchor.Y - 2f * k), Mul(Leader, a), 1f);

        if (p.IsTarget)
        {
            float tri = MathF.Round(5f * k);
            var t0 = new Vector2(cx - tri, pointerTop);
            var t1 = new Vector2(cx + tri, pointerTop);
            var t2 = new Vector2(cx, pointerTop + tri);
            dl.AddTriangleFilled(t0 - new Vector2(1, 1), t1 + new Vector2(1, -1), t2 + new Vector2(0, 1.5f), Mul(Outline, a));
            dl.AddTriangleFilled(t0, t1, t2, Mul(GoldBright, a));
        }
        else if (p.Id == _hoverId)
        {
            dl.AddLine(new Vector2(x, baseY + 1f), new Vector2(x + p.NameW, baseY + 1f), Mul(HoverEdge, a), 1f);   // click-to-select hover
        }

        uint col = C(UI.Panels.RadarSettingsStore.KindColor(p.Kind));
        var pos = new Vector2(x, nameTop);
        OutlinedText(dl, p.NameFont, p.NameSize, pos, a, p.Name);
        dl.AddText(p.NameFont, p.NameSize, pos, Mul(col, a), p.Name);
    }

    /// <summary>
    /// A bar: black edge, dark track, the chip (what the last drop took), the
    /// gradient fill with a shine line, and optional quarter ticks. Shared with
    /// the player plate.
    /// </summary>
    internal static void PaintBar(ImDrawListPtr dl, Vector2 b0, Vector2 b1, float round, float a,
        float shownRatio, float trailRatio, uint baseCol, bool ticks)
    {
        dl.AddRectFilled(b0 - Vector2.One, b1 + Vector2.One, Mul(BarEdge, a * 0.9f), round + 1);
        dl.AddRectFilled(b0, b1, Mul(BarBack, a), round);

        float innerW = b1.X - b0.X - 2f;
        var i0 = new Vector2(b0.X + 1, b0.Y + 1);
        float innerBottom = b1.Y - 1;
        if (innerBottom <= i0.Y) innerBottom = i0.Y + 1;
        float shown = Math.Clamp(shownRatio, 0f, 1f);
        float trail = Math.Clamp(trailRatio, 0f, 1f);

        if (trail > shown + 0.002f)
            dl.AddRectFilled(new Vector2(i0.X + innerW * shown, i0.Y), new Vector2(i0.X + innerW * trail, innerBottom), Mul(Trail, a * 0.85f));

        if (shown > 0.001f)
        {
            uint top = Mul(Shade(baseCol, 1.18f), a), bottom = Mul(Shade(baseCol, 0.72f), a);
            var f1 = new Vector2(i0.X + MathF.Max(1f, innerW * shown), innerBottom);
            dl.AddRectFilledMultiColor(i0, f1, top, top, bottom, bottom);
            dl.AddLine(i0 + new Vector2(0, 0.5f), new Vector2(f1.X, i0.Y + 0.5f), Mul(Shine, a), 1f);
        }

        if (ticks && innerBottom - i0.Y >= 3f)
            for (int q = 1; q <= 3; q++)
            {
                float tx = MathF.Round(i0.X + innerW * q * 0.25f);
                dl.AddLine(new Vector2(tx, i0.Y + 1), new Vector2(tx, innerBottom - 1), Mul(Tick, a), 1f);
            }
    }

    /// <summary>
    /// The debuff row, centred under the bar: one icon per kind with a shrinking
    /// countdown line, blinking in its last seconds; "+N" when more than fit;
    /// then the "debuffed by others" marker.
    /// </summary>
    private static void PaintIcons(ImDrawListPtr dl, Plate p, float cx, float top, float k, float a, long now)
    {
        float sz = p.IconSize;
        float gap = MathF.Max(2f, MathF.Round(2f * k));
        int count = p.IconCount;
        int shown = count > MaxIcons ? MaxIcons - 1 : count;
        int overflow = count - shown;
        string more = "";
        float moreW = 0f;
        if (overflow > 0)
        {
            if (overflow != p.OverflowKey)
            {
                p.OverflowKey = overflow;
                p.OverflowString = "+" + overflow.ToString(CultureInfo.InvariantCulture);
            }
            more = p.OverflowString;
            moreW = TextWidth(p.SmallFont, p.SmallSize, more) + 4f * k;
        }
        int slots = shown + (p.Others ? 1 : 0);
        float rowW = slots * sz + Math.Max(0, slots - 1) * gap + (overflow > 0 ? gap + moreW : 0f);
        float x = MathF.Round(cx - rowW * 0.5f);
        float glyphPx = MathF.Round(sz * 0.8f);
        ImFontPtr iconFont = ImGuiFonts.Sharp(glyphPx, bold: false);   // the icons are merged into every bake
        float lineH = MathF.Max(1f, MathF.Round(1.5f * k));

        for (int i = 0; i < shown; i++)
        {
            DebuffIcon ic = p.Icons[i];
            int kind = (int)ic.Kind;
            uint tint = kind < KindTint.Length ? KindTint[kind] : KindTint[^1];
            float remaining = (ic.Expiry - now) / (float)Stopwatch.Frequency;
            float frac = ic.Duration > 0 ? Math.Clamp(remaining / ic.Duration, 0f, 1f) : 0f;
            float blink = remaining < BlinkSec ? 0.45f + 0.55f * MathF.Abs(MathF.Cos(remaining * MathF.PI * 2f)) : 1f;

            var i0 = new Vector2(x, top);
            var i1 = i0 + new Vector2(sz, sz);
            dl.AddRectFilled(i0, i1, Mul(IconBack, a), 2f * k);
            dl.AddRect(i0, i1, Mul(tint, a * 0.55f * blink), 2f * k, ImDrawFlags.None, 1f);
            PhosphorIcons.DrawCentered(dl, iconFont, glyphPx, KindGlyph[kind < KindGlyph.Length ? kind : KindGlyph.Length - 1],
                i0, new Vector2(sz, sz), Mul(tint, a * blink));
            if (ic.Stack > 1 && ic.Stack < StackDigits.Length)
            {
                string digit = StackDigits[ic.Stack];
                float ds = MathF.Round(p.SmallSize * 0.85f);
                var dp = new Vector2(i1.X - TextWidth(p.SmallFont, ds, digit) + 1f, i0.Y - 2f);
                OutlinedText(dl, p.SmallFont, ds, dp, a, digit);
                dl.AddText(p.SmallFont, ds, dp, Mul(HpText, a), digit);
            }
            // Countdown: a line under the icon that shrinks to nothing at expiry.
            float ly = i1.Y + 1f;
            dl.AddRectFilled(new Vector2(i0.X, ly), new Vector2(i1.X, ly + lineH), Mul(Outline, a * 0.8f));
            if (frac > 0f)
                dl.AddRectFilled(new Vector2(i0.X, ly), new Vector2(i0.X + MathF.Max(1f, sz * frac), ly + lineH), Mul(tint, a * blink));
            x += sz + gap;
        }

        if (overflow > 0)
        {
            var mp = new Vector2(x, MathF.Round(top + (sz - p.SmallSize) * 0.5f));
            OutlinedText(dl, p.SmallFont, p.SmallSize, mp, a, more);
            dl.AddText(p.SmallFont, p.SmallSize, mp, Mul(DimText, a), more);
            x += moreW + gap;
        }

        if (p.Others)
        {
            var i0 = new Vector2(x, top);
            dl.AddRectFilled(i0, i0 + new Vector2(sz, sz), Mul(IconBack, a * 0.8f), 2f * k);
            PhosphorIcons.DrawCentered(dl, iconFont, glyphPx, PhosphorIcons.UsersThree, i0, new Vector2(sz, sz), Mul(OthersTint, a * 0.8f));
        }
    }

    /// <summary>A soft 1 px outline under text (the text itself is drawn by the caller).</summary>
    internal static void OutlinedText(ImDrawListPtr dl, ImFontPtr font, float size, Vector2 pos, float a, string text)
    {
        uint o = Mul(Outline, a);
        dl.AddText(font, size, pos + new Vector2(-1, 0), o, text);
        dl.AddText(font, size, pos + new Vector2(1, 0), o, text);
        dl.AddText(font, size, pos + new Vector2(0, -1), o, text);
        dl.AddText(font, size, pos + new Vector2(0, 1), o, text);
        dl.AddText(font, size, pos + new Vector2(1, 1), o, text);
    }

    private static string HpString(Plate p)
    {
        long key = p.HpMax > 0 ? ((long)p.HpMax << 32) | p.HpCur : -1 - (long)MathF.Round(p.Ratio * 100f);
        if (key == p.HpKey) return p.HpString;
        p.HpKey = key;
        p.HpString = p.HpMax > 0
            ? p.HpCur.ToString(CultureInfo.InvariantCulture) + " / " + p.HpMax.ToString(CultureInfo.InvariantCulture)
            : ((int)MathF.Round(p.Ratio * 100f)).ToString(CultureInfo.InvariantCulture) + "%";
        return p.HpString;
    }

    private static string DistString(Plate p)
    {
        int d = (int)MathF.Round(p.Distance);
        if (d == p.DistKey) return p.DistString;
        p.DistKey = d;
        p.DistString = d.ToString(CultureInfo.InvariantCulture) + "yd";
        return p.DistString;
    }

    // ── Lookups for the other overlays (render thread) ──────────────────

    /// <summary>
    /// A monster's head point (AC landblock-local, z = feet + head height + lift) and its feet height:
    /// live position when the client still has it, else its plate's last one.
    /// </summary>
    internal static bool TryGetHead(uint id, out uint cell, out float x, out float y, out float z, out float feetZ)
    {
        Plates.TryGetValue(id, out Plate? p);
        float head = p?.HeadHeight ?? EstimateHeadHeight(id, ClientObjectHooks.TryGetSnapshotName(id, out string n) ? n : "");
        if (ClientObjectHooks.TryGetObjectPosition(id, out cell, out x, out y, out feetZ) && (cell >> 16) != 0)
        {
            z = feetZ + head + MonsterHudSettings.HeightLift;
            return true;
        }
        if (p != null && p.LastCell != 0)
        {
            cell = p.LastCell; x = p.LastX; y = p.LastY; feetZ = p.LastZ;
            z = feetZ + head + MonsterHudSettings.HeightLift;
            return true;
        }
        z = 0;
        return false;
    }

    /// <summary>
    /// The plate whose monster has <paramref name="name"/>: the selected one first,
    /// then the one the player hit most recently, then the nearest. 0 if none.
    /// </summary>
    internal static uint FindPlateByName(string name)
    {
        uint target = ClientHelperHooks.GetSelectedItemId();
        uint best = 0;
        long bestHit = long.MinValue;
        float bestD = float.MaxValue;
        foreach (Plate p in Plates.Values)
        {
            if (p.Gone || p.IsLabel || !p.Name.Equals(name, StringComparison.Ordinal)) continue;
            if (p.Id == target) return p.Id;
            long hit = CombatText.LastHitTicks(p.Id);
            if (hit > bestHit || (hit == bestHit && p.Distance < bestD))
            {
                best = p.Id; bestHit = hit; bestD = p.Distance;
            }
        }
        return best;
    }

    /// <summary>
    /// The plate whose monster's name appears in a death message (the longest
    /// such name; ties: the one hit most recently, then the target). 0 if none.
    /// </summary>
    internal static uint FindPlateNamedIn(string message)
    {
        uint target = ClientHelperHooks.GetSelectedItemId();
        uint best = 0;
        int bestLen = 0;
        long bestHit = long.MinValue;
        foreach (Plate p in Plates.Values)
        {
            if (p.IsLabel || p.Name.Length == 0 || p.Name.Length < bestLen) continue;
            if (!message.Contains(p.Name, StringComparison.Ordinal)) continue;
            long hit = CombatText.LastHitTicks(p.Id);
            if (p.Id == target) hit = Math.Max(hit, long.MinValue + 1);
            if (p.Name.Length > bestLen || hit > bestHit)
            {
                best = p.Id; bestLen = p.Name.Length; bestHit = hit;
            }
        }
        return best;
    }

    // ── Colours ──────────────────────────────────────────────────────────

    /// <summary>Green above 60 %, amber through the middle, red below 30 %; blended.</summary>
    private static uint HealthColor(float ratio)
    {
        Vector3 c = ratio >= 0.6f ? Vector3.Lerp(HpMid, HpHigh, Math.Clamp((ratio - 0.6f) / 0.25f, 0f, 1f))
                  : ratio >= 0.3f ? Vector3.Lerp(HpLow, HpMid, (ratio - 0.3f) / 0.3f)
                  : HpLow;
        return 0xFF000000u | ((uint)c.Z << 16) | ((uint)c.Y << 8) | (uint)c.X;   // ABGR
    }

    /// <summary>Level badge text by the monster's level against the player's ("con" colours).</summary>
    private static uint ConColor(int level)
    {
        if (_playerLevel <= 0 || level <= 0) return C(0xFFE6E6E6);
        float r = (float)level / _playerLevel;
        return r < 0.6f ? C(0xFF9AA3AD)      // trivial: grey
             : r < 0.9f ? C(0xFF7BE08F)      // easy: green
             : r < 1.1f ? C(0xFFF2F2F2)      // even: white
             : r < 1.3f ? C(0xFFFFB347)      // tough: orange
             : C(0xFFFF6B6B);                // dangerous: red
    }

    /// <summary>Scales an ABGR colour's alpha by <paramref name="a"/>.</summary>
    internal static uint Mul(uint abgr, float a)
    {
        uint alpha = (uint)Math.Clamp((int)MathF.Round((abgr >> 24) * a), 0, 255);
        return (abgr & 0x00FFFFFF) | (alpha << 24);
    }

    /// <summary>Scales an ABGR colour's RGB by <paramref name="k"/> (alpha kept).</summary>
    internal static uint Shade(uint abgr, float k)
    {
        uint r = (uint)Math.Min(255, (int)((abgr & 0xFF) * k));
        uint g = (uint)Math.Min(255, (int)(((abgr >> 8) & 0xFF) * k));
        uint b = (uint)Math.Min(255, (int)(((abgr >> 16) & 0xFF) * k));
        return (abgr & 0xFF000000) | (b << 16) | (g << 8) | r;
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    /// <summary>Chat lines for "/rv plates status": what the overlays see right now. AC's thread.</summary>
    public static string[] DescribeLines()
    {
        int onScreen = 0, known = 0, labels = 0, labelsOnScreen = 0;
        foreach (Plate p in Plates.Values)
        {
            if (p.IsLabel)
            {
                labels++;
                if (p.OnScreen && p.Alpha > 0.01f) labelsOnScreen++;
                continue;
            }
            if (p.OnScreen && p.Alpha > 0.01f) onScreen++;
            if (p.HpKnown) known++;
        }
        return new[]
        {
            $"Nameplates: {Plates.Count - labels} tracked, {onScreen} on screen, {known} with health seen; " +
            $"name labels {labels} ({labelsOnScreen} on screen), {Classes.Count} objects classified; " +
            $"camera {(GameMatrixCapture.HasCapturedFrame ? "captured" : "not captured")} " +
            $"(frame lb 0x{GameMatrixCapture.FrameLandblock:X4}, viewport {GameMatrixCapture.ViewportWidth}x{GameMatrixCapture.ViewportHeight}); " +
            $"weakness data {(_weakByWcid.Count + _weakByName.Count > 0 ? "loaded" : "none")}.",
            PlateDebuffs.Describe(),
            PlayerPlate.Describe() + " " + CombatText.Describe(),
        };
    }
}
