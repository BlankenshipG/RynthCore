using System;
using System.Runtime.InteropServices;

namespace RynthCore.PluginSdk;

[StructLayout(LayoutKind.Sequential)]
public struct RynthCoreApiNative
{
    public uint Version;
    public IntPtr ImGuiContext;
    public IntPtr D3DDevice;
    public IntPtr GameHwnd;
    public IntPtr LogFn;
    public IntPtr ProbeClientHooksFn;
    public IntPtr GetClientHookFlagsFn;
    public IntPtr ChangeCombatModeFn;
    public IntPtr CancelAttackFn;
    public IntPtr QueryHealthFn;
    public IntPtr MeleeAttackFn;
    public IntPtr MissileAttackFn;
    public IntPtr DoMovementFn;
    public IntPtr StopMovementFn;
    public IntPtr JumpNonAutonomousFn;
    public IntPtr SetAutonomyLevelFn;
    public IntPtr SetAutoRunFn;
    public IntPtr TapJumpFn;
    public IntPtr SetIncomingChatSuppressionFn;
    public IntPtr SelectItemFn;
    public IntPtr SetSelectedObjectIdFn;
    public IntPtr GetSelectedItemIdFn;
    public IntPtr GetPreviousSelectedItemIdFn;
    public IntPtr GetPlayerIdFn;
    public IntPtr GetGroundContainerIdFn;
    public IntPtr GetNumContainedItemsFn;
    public IntPtr GetNumContainedContainersFn;
    public IntPtr GetCurCoordsFn;
    public IntPtr UseObjectFn;
    public IntPtr UseObjectOnFn;
    public IntPtr UseEquippedItemFn;
    public IntPtr MoveItemExternalFn;
    public IntPtr MoveItemInternalFn;
    public IntPtr WriteToChatFn;
    public IntPtr GetPlayerPoseFn;
    public IntPtr IsPortalingFn;
    public IntPtr SetMotionFn;
    public IntPtr StopCompletelyFn;
    public IntPtr TurnToHeadingFn;
    public IntPtr GetPlayerHeadingFn;
    public IntPtr GetObjectNameFn;
    public IntPtr GetPlayerVitalsFn;
    public IntPtr GetObjectPositionFn;
    public IntPtr RequestIdFn;
    public IntPtr GetTargetVitalsFn;
    public IntPtr CastSpellFn;
    public IntPtr GetItemTypeFn;
    public IntPtr GetObjectIntPropertyFn;
    public IntPtr GetObjectBoolPropertyFn;
    public IntPtr ObjectIsAttackableFn;
    public IntPtr GetObjectSkillFn;
    public IntPtr IsSpellKnownFn;
    public IntPtr ReadPlayerEnchantmentsFn;
    public IntPtr GetServerTimeFn;
    public IntPtr ReadObjectEnchantmentsFn;
    public IntPtr WorldToScreenFn;
    public IntPtr GetViewportSizeFn;
    public IntPtr Nav3DClearFn;
    public IntPtr Nav3DAddRingFn;
    public IntPtr Nav3DAddLineFn;
    public IntPtr InvokeChatParserFn;
    public IntPtr GetObjectDoublePropertyFn;
    public IntPtr GetObjectQuadPropertyFn;
    public IntPtr GetObjectAttribute2ndBaseLevelFn;
    public IntPtr GetPlayerBaseVitalsFn;
    public IntPtr GetObjectStringPropertyFn;
    public IntPtr GetObjectWielderInfoFn;
    public IntPtr NativeAttackFn;
    public IntPtr IsPlayerReadyFn;
    public IntPtr SetFpsLimitFn;
    public IntPtr GetContainerContentsFn;
    public IntPtr GetObjectOwnershipInfoFn;
    public IntPtr SplitStackInternalFn;
    public IntPtr MergeStackInternalFn;
    public IntPtr GetCurrentCombatModeFn;
    public IntPtr SalvagePanelOpenFn;
    public IntPtr SalvagePanelAddItemFn;
    public IntPtr SalvagePanelExecuteFn;
    public IntPtr GetVitaeFn;
    public IntPtr GetAccountNameFn;
    public IntPtr GetWorldNameFn;
    public IntPtr GetObjectWcidFn;
    public IntPtr HasAppraisalDataFn;
    public IntPtr GetLastIdTimeFn;
    public IntPtr GetObjectHeadingFn;
    public IntPtr GetBusyStateFn;
    public IntPtr GetObjectSpellIdsFn;
    public IntPtr GetObjectSkillBuffedFn;
    public IntPtr GetObjectAttributeFn;
    public IntPtr GetObjectMotionOnFn;
    public IntPtr GetObjectStateFn;
    public IntPtr GetObjectBitfieldFn;
    public IntPtr ForceResetBusyCountFn;
    public IntPtr GetObjectPalettesFn;
    public IntPtr CommenceJumpFn;
    public IntPtr DoJumpFn;
    public IntPtr LaunchJumpWithMotionFn;
    public IntPtr GetRadarRectFn;
    public IntPtr SetRadarSuppressedFn;
    public IntPtr SetChatSuppressedFn;
    public IntPtr SetPowerbarSuppressedFn;
    public IntPtr GetCastBusyStateFn;
    public IntPtr ReadKnownSpellsFn;
    public IntPtr Nav3DAddTriangleFn;
    public IntPtr Nav3DAddRingExFn;
    // APPEND-AT-END (ABI): must mirror PluginContract.RynthCoreAPI order exactly. v62+.
    public IntPtr GiveObjectToFn;
    public IntPtr GetUseDoneSeqFn; // v63: monotonic count of inbound server UseDone (0x1C7)
    public IntPtr GetEngineStatusJsonFn;   // v64: engine-side status fields as ANSI JSON (excludes bot)
    public IntPtr GetPluginSnapshotJsonFn; // v64: broker a named plugin's RynthPluginGetSnapshotJson
    public IntPtr SendPluginCommandFn;     // v64: forward (action,value) to a named plugin's command export
    public IntPtr GetObjectDataIdPropertyFn; // v65: read a PWD DataID property (Icon=8 → _iconID) for inventory icons
    public IntPtr GetPluginExportJsonFn;     // v66: broker any RynthPluginGet*Json export on a named plugin
    // v67: vendor trading (see VendorTypes.cs / RynthCoreHost vendor section)
    public IntPtr GetVendorInfoFn;           // int  GetVendorInfo(VendorInfoNative*)
    public IntPtr GetVendorItemsFn;          // int  GetVendorItems(VendorItemNative*, int maxCount)
    public IntPtr VendorBuyFn;               // uint VendorBuy(uint vendorId, VendorTradeEntryNative*, int count)
    public IntPtr VendorSellFn;              // uint VendorSell(uint vendorId, uint* itemIds, int count)
    public IntPtr GetVendorTradeStatusFn;    // int  GetVendorTradeStatus(VendorTradeStatusNative*)
    public IntPtr GetPluginInterfaceFn;      // v68: void* GetPluginInterface(const char* plugin, const char* iface, uint version)
    public IntPtr GetLiveObjectIdsFn;        // v69: int GetLiveObjectIds(uint* buffer, int capacity) -> total count
    // v70: action outcomes + wield-to-slot (and the engine raises OnEnchantmentAdded/Removed)
    public IntPtr GetLastUseDoneFn;          // int GetLastUseDone(int* seq, uint* error)
    public IntPtr GetLastWeenieErrorFn;      // int GetLastWeenieError(int* seq, uint* error, uint* eventType, uint* objectId)
    public IntPtr WieldItemFn;               // int WieldItem(uint objectId, uint equipMask)
    // v71: script windows (display list in, events out; RynthSuite Docs/RYNTHLUA_WINDOWS_DESIGN.md §4)
    public IntPtr UiSubmitFn;                // int UiSubmit(const uint8_t* data, int length)
    public IntPtr UiPollEventsFn;            // int UiPollEvents(uint8_t* buffer, int capacity, int* remaining)
    public IntPtr UiGetInfoFn;               // int UiGetInfo(UiInfoNative* info)
    // v72: player-to-player trade (state polled; actions queued for AC's main thread)
    public IntPtr GetTradeStateFn;           // int GetTradeState(TradeStateNative* state)
    public IntPtr GetTradeItemsFn;           // int GetTradeItems(int side, uint* buffer, int capacity) -> total
    public IntPtr TradeOpenFn;               // int TradeOpen(uint targetId)
    public IntPtr TradeAddFn;                // int TradeAdd(uint itemId, uint slot)
    public IntPtr TradeAcceptFn;             // int TradeAccept(void)
    public IntPtr TradeDeclineFn;            // int TradeDecline(void)
    public IntPtr TradeResetFn;              // int TradeReset(void)
    public IntPtr TradeCloseFn;              // int TradeClose(void)
    // v73: every property type off the main thread
    public IntPtr GetObjectInstanceIdPropertyFn; // int GetObjectInstanceIdProperty(uint objectId, uint stype, uint* value)
    // v74: VTank's macro under the Decal bridge (one bot per client)
    public IntPtr GetVTankStateFn;           // int GetVTankState(int* sequence) -> flags (bit0 watching, bit1 running)
    // v75: the character's titles and which server this is
    public IntPtr GetCharacterTitlesFn;      // int GetCharacterTitles(uint* ids, int maxCount, uint* currentTitle) -> count, -1 unknown
    public IntPtr GetServerInfoFn;           // int GetServerInfo(byte* worldName, int capacity) -> flags (bit0 Aelrynth, bit1 staging, bit2 world name known)
    // v76: close an external container (corpse, chest) like the client's window close
    public IntPtr CloseContainerFn;          // int CloseContainer(uint containerId) -> 1 sent or queued
    // v77: outcome of the latest merge-stack request (cdecl thunk)
    public IntPtr GetMergeStackResultFn;     // int GetMergeStackResult(uint src, uint tgt, int* amount, int* ageMs)
}

/// <summary>
/// v72 GetTradeState result (Pack 4, 96 bytes; later versions only append).
/// Mirrors RynthCore.Engine/Plugins/PluginContract.cs TradeStateNative exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct TradeStateNative
{
    /// <summary>In: sizeof(TradeStateNative). Out: bytes the engine wrote.</summary>
    public uint Size;
    /// <summary>bit0 open, bit1 you accepted, bit2 partner accepted, bit3 engine sees trade
    /// events, bit4 all trade actions bound.</summary>
    public uint Flags;
    /// <summary>+1 for every trade that opens.</summary>
    public uint Generation;
    /// <summary>+1 for every trade event.</summary>
    public uint Sequence;
    public uint PartnerId;
    public uint InitiatorId;
    public int SelfItemCount;
    public int PartnerItemCount;
    public uint LastEventType;
    public uint FailureCount;
    public uint LastFailureItemId;
    public uint LastFailureReason;
    public uint LastCloseReason;
    public uint LastAcceptedBy;
    public uint LastDeclinedBy;
    public uint LastResetBy;
    public uint CompletedCount;
    public uint PartnerAcceptCount;
    public fixed uint Reserved[6];
}

/// <summary>
/// v71 UiGetInfo result (Pack 4, 480 bytes in format 1; later versions only append).
/// Mirrors RynthCore.Engine/Plugins/PluginContract.cs UiInfoNative exactly.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct UiInfoNative
{
    /// <summary>In: sizeof(UiInfoNative). Out: bytes the engine wrote.</summary>
    public uint Size;
    /// <summary>bit0 ImGui available, bit1 in world, bit2 pop-outs available.</summary>
    public uint Flags;
    /// <summary>Highest display-list format the engine replays.</summary>
    public ushort MaxFormatVersion;
    /// <summary>The ops replayed within MaxFormatVersion: 2 = + Image, ImageButton, InputInt, InputFloat,
    /// DragInt, DragFloat (2026-09-30). 0 on older engines (the field was reserved) = Phase 1 ops only.</summary>
    public ushort OpLevel;
    public uint MaxOpsPerWindow;
    public uint MaxBytesPerWindow;
    public uint MaxWindowsPerOwner;
    public uint MaxBytesPerSubmit;
    public float DisplayWidth;
    public float DisplayHeight;
    public float UiScale;
    public float TextLineHeight;
    public float FrameHeight;
    public float ItemSpacingX;
    public float ItemSpacingY;
    public float FramePaddingX;
    public float FramePaddingY;
    /// <summary>Default font advance of ' '..'~' (95), pixels at UiScale.</summary>
    public fixed float AsciiAdvance[95];
    public uint FrameCounter;
    public fixed uint Reserved[8];
}
