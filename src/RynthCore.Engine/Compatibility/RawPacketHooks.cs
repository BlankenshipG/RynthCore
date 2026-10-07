using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using RynthCore.Engine.Hooking;

namespace RynthCore.Engine.Compatibility;

/// <summary>
/// Hooks the game's recvfrom socket function to intercept all S2C UDP packets.
///
/// The acclient.exe data segment holds a pointer to the recvfrom implementation:
///   RecvFrom @ *(int*)0x007935AC
///   SendTo   @ *(int*)0x007935A4   (not hooked — S2C discovery only)
///
/// We read the function address, install a MinHook detour, call through to the
/// original on every receive, then hand the buffer to RawPacketParser for
/// opcode extraction. The hook is purely read-only and never modifies traffic.
/// It also feeds Net.ServerMessageStream (API v78), which copies fragment datagrams
/// for reassembly on the plugin pump while a plugin wants server messages.
/// </summary>
internal static class RawPacketHooks
{
    private const int RecvFromPtrAddr = unchecked((int)0x007935AC);

    // Phase B: resolve the RecvFrom fn-ptr slot's address by code-xref ("call [slot]" =
    // FF 15 <addr>), operand at offset 2; the VA stays as fallback.
    private static readonly byte?[] PatXrefRecvFromSlot = [ 0xFF, 0x15, null, null, null, null, 0x8B, 0xF0, 0x85, 0xF6, 0x7E, 0x67 ];

    private static IntPtr _originalRecvFromPtr;

    public static bool IsInstalled { get; private set; }

    public static void Initialize()
    {
        if (IsInstalled) return;

        int slotAddr = RecvFromPtrAddr;
        if (AcClientModule.TryReadTextSection(out AcClientTextSection text))
            slotAddr = HookResolver.ResolveData(text, "RawPacket.RecvFromSlot", PatXrefRecvFromSlot, 2, RecvFromPtrAddr).Address.ToInt32();

        unsafe
        {
            IntPtr fnPtrAddr = new IntPtr(slotAddr);
            if (!SmartBoxLocator.IsMemoryReadable(fnPtrAddr, 4))
            {
                RynthLog.Compat($"RawPacket: RecvFrom pointer address 0x{slotAddr:X8} not readable — skipping.");
                return;
            }

            IntPtr recvFromAddr = Marshal.ReadIntPtr(fnPtrAddr);
            if (recvFromAddr == IntPtr.Zero)
            {
                RynthLog.Compat("RawPacket: RecvFrom pointer is null — skipping.");
                return;
            }

            try
            {
                delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, IntPtr, IntPtr, int> pDetour = &RecvFromDetour;
                MinHook.Hook(recvFromAddr, (IntPtr)pDetour, out _originalRecvFromPtr);
                IsInstalled = true;
                RynthLog.Verbose($"RawPacket: RecvFrom hook installed @ 0x{recvFromAddr.ToInt32():X8}");
            }
            catch (Exception ex)
            {
                RynthLog.Compat($"RawPacket: MinHook failed — {ex.Message}");
            }
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe int RecvFromDetour(
        IntPtr s, IntPtr buf, int len, int flags, IntPtr from, IntPtr fromlen)
    {
        RecursionGuard.Tick("RawPacketHooks.RecvFrom");
        delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, IntPtr, IntPtr, int> original =
            (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, int, int, IntPtr, IntPtr, int>)_originalRecvFromPtr;

        int bytesRead = original(s, buf, len, flags, from, fromlen);

        if (bytesRead > 0)
        {
            // Managed exceptions (bounds errors, etc.) CAN be caught in NativeAOT.
            // This guard prevents any parser bug from crashing the client.
            try
            {
                // v78 server-message stream: a volatile check, and while a plugin wants
                // messages, one copy into a ring for the pump (never blocks, never allocates).
                // A MSG_PEEK (0x2) read leaves the datagram queued; the real read copies it.
                if ((flags & 0x2) == 0)
                    Net.ServerMessageStream.OnDatagram((byte*)buf, bytesRead);
                RawPacketParser.Parse((byte*)buf, bytesRead);
            }
            catch
            {
                // silently swallow — never affect game traffic
            }
        }

        return bytesRead;
    }
}
