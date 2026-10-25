using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using RynthCore.Engine.Hooking;

namespace RynthCore.Engine.Compatibility;

/// <summary>
/// IAT-hooks acclient.exe's <c>kernel32!CreateFileW</c> and <c>CreateFileA</c>
/// imports to force maximally-permissive share semantics on AC's data files.
///
/// Why this exists:
///   <see cref="MultiClientHooks"/> patches AC's <c>OpenDataFile</c> high-level
///   <c>openFlags</c> bit 0x2, which selects AC's "shared" branch — but that
///   branch still translates to a CreateFile call whose <c>dwShareMode</c> may
///   not be wide enough to coexist with handles already held by Decal-injected
///   clients launched by other launchers (Thwargle etc.). Windows file-sharing
///   compatibility is decided by the kernel at CreateFile time, not by AC's
///   internal flag.
///
///   This hook intercepts CreateFile right before the kernel sees it, and for
///   the known DAT paths rewrites:
///     dwDesiredAccess = GENERIC_READ                                          (drop WRITE — DATs are read-only after patch)
///     dwShareMode     = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE
///   Other paths pass through untouched.
///
/// Why IAT-hook instead of MinHook on the kernel32 export:
///   Only the modules that open AC's data files are hooked: acclient.exe
///   (imports CreateFileA), its CRT MSVCR70.dll (CreateFileA/W, behind
///   fopen/_wfopen), and acclient.exe's private CreateFileW slot, which the
///   2025 client fills on first use and opens every DAT through (it is in no
///   import table; <see cref="LazyImportSlotScanner"/>). Missing that slot let
///   the first client open the DATs read/write, deny-write, and every later
///   client failed with "Can't open the data files" (2026-10-15).
///   A kernel32 export hook also caught every other module,
///   including d3d9.dll and the GPU driver while AC created its device: their
///   CreateFile calls went through managed code, could wait on the runtime
///   while plugins loaded, and on some PCs AC then failed with "Could not
///   initialize Direct3D" (2026-10-14). The detour preserves the Win32 last
///   error, because callers read it after a successful CreateFile too
///   (ERROR_ALREADY_EXISTS).
///   Decal coexistence: our detour calls the pointer the IAT slot held, so any
///   Decal detour on the kernel32 export still runs. Both hooks fire in order.
///   The slots are restored at engine shutdown (<see cref="Uninstall"/>): the
///   detours live in this engine generation, which hot reload unloads.
/// </summary>
internal static class DatFileShareHooks
{
    // We force-share by file *extension* (.dat) rather than an explicit name
    // list. Stock retail AC ships portal.dat / cell.dat / client_*.dat, but
    // private servers commonly add split client-cell DATs (client_cell_1.dat,
    // client_cell_2.dat, ...) and other variants we can't enumerate up front.
    // Filtering on extension catches them all without per-server tuning.
    // AC only opens .dat files via CreateFile for game data; non-DAT writes
    // (logs, prefs, screenshots) pass through unmodified.
    private const string DatExtension = ".dat";

    private const uint GENERIC_READ          = 0x80000000;
    private const uint FILE_SHARE_READ       = 0x00000001;
    private const uint FILE_SHARE_WRITE      = 0x00000002;
    private const uint FILE_SHARE_DELETE     = 0x00000004;
    private const uint FILE_SHARE_ALL        = FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE;

    private const uint PAGE_READWRITE        = 0x04;
    private const uint PAGE_EXECUTE_READWRITE = 0x40;

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Unicode, SetLastError = true)]
    private delegate IntPtr CreateFileWDelegate(
        IntPtr lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [UnmanagedFunctionPointer(CallingConvention.StdCall, CharSet = CharSet.Ansi, SetLastError = true)]
    private delegate IntPtr CreateFileADelegate(
        IntPtr lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, uint dwSize, uint flNewProtect, out uint lpflOldProtect);

    /// <summary>
    /// One patched import slot. <see cref="Slot"/> is the address inside the IAT array (not a
    /// function address); <see cref="Original"/> is what it held (the real kernel32!CreateFile,
    /// or another hooker's detour), written back by <see cref="Uninstall"/>. <see cref="Detour"/>
    /// keeps the delegate reachable: the function pointer handed to native code is only valid
    /// while it is.
    /// </summary>
    private sealed record IatHook(string Module, string Function, IntPtr Slot, IntPtr Original, Delegate Detour);

    private static readonly List<IatHook> _iatHooks = [];

    private static string _statusMessage = "Not probed yet.";

    public static bool IsEnabled { get; private set; }
    public static bool IsInstalled { get; private set; }
    public static string StatusMessage => _statusMessage;

    public static void Initialize()
    {
        if (IsInstalled)
            return;

        // Diagnostic kill-switch. Set "EnableDatShareHook": false in
        // %APPDATA%\RynthCore\engine.json to skip detouring CreateFileA/W
        // entirely. Used to isolate this hook as a crash suspect.
        if (!Plugins.EngineSettings.EnableDatShareHook)
        {
            _statusMessage = "DAT share hook disabled via engine.json (EnableDatShareHook=false).";
            RynthLog.Info($"Compat: {_statusMessage}");
            return;
        }

        // Gate behind AllowMultipleClients. If the user has multi-client off,
        // they're not multiboxing, so we have no reason to widen DAT sharing.
        if (!LauncherSettings.AllowMultipleClientsEnabled)
        {
            _statusMessage = "Multi-client disabled — DAT share hook skipped.";
            RynthLog.Verbose($"Compat: {_statusMessage}");
            return;
        }

        IsEnabled = true;

        try
        {
            IntPtr moduleBase = AcClientGetModuleBase();
            if (moduleBase == IntPtr.Zero)
            {
                _statusMessage = "acclient.exe module base not found.";
                RynthLog.Compat($"Compat: DAT share hook failed - {_statusMessage}");
                return;
            }

            // acclient.exe opens DATs with its own CreateFileA import, or (the 2025 client)
            // through a private CreateFileW slot its Layer-for-Unicode stub fills; its CRT
            // (MSVCR70.dll, statically imported, so loaded by now) does fopen/_wfopen
            // opens through its CreateFileA/W imports. Nothing else is hooked.
            HookModuleImports("acclient.exe", moduleBase);
            HookModuleImports("MSVCR70.dll", GetModuleHandleW("MSVCR70.dll"));
            try
            {
                HookLazySlots(moduleBase);
            }
            catch (Exception ex)
            {
                RynthLog.Compat($"Compat: DAT share - private slot scan failed: {ex.Message}");
            }

            if (_iatHooks.Count == 0)
            {
                _statusMessage = "no CreateFile import slots found in acclient.exe or MSVCR70.dll.";
                RynthLog.Compat($"Compat: DAT share hook failed - {_statusMessage}");
                return;
            }

            IsInstalled = true;
            _statusMessage = $"DAT share hook installed ({_iatHooks.Count} import slot(s): " +
                string.Join(", ", _iatHooks.Select(h => $"{h.Module}!{h.Function}")) +
                "); Direct3D and drivers call CreateFile unhooked.";
            RynthLog.Info($"Compat: {_statusMessage}");
        }
        catch (Exception ex)
        {
            _statusMessage = $"DAT share hook exception: {ex.Message}";
            RynthLog.Compat($"Compat: DAT share hook failed - {ex.Message}");
        }
    }

    /// <summary>
    /// Points <paramref name="moduleName"/>'s kernel32 CreateFileA/W import slots at detours that
    /// call whatever each slot held. A module that isn't loaded, or doesn't import one of them,
    /// is skipped (logged).
    /// </summary>
    private static void HookModuleImports(string moduleName, IntPtr moduleBase)
    {
        if (moduleBase == IntPtr.Zero)
        {
            RynthLog.Compat($"Compat: DAT share - {moduleName} is not loaded; its CreateFile imports are not hooked.");
            return;
        }

        foreach (string function in new[] { "CreateFileW", "CreateFileA" })
        {
            if (TryPatchIatSlot(moduleBase, function, out IntPtr slot, out IntPtr current))
                HookSlot(moduleName, function, "import", slot, current, current);
        }
    }

    /// <summary>
    /// Points acclient.exe's private CreateFileA/W slots (filled on first use by a Layer-for-Unicode
    /// style stub, see <see cref="LazyImportSlotScanner"/>) at the detours. The 2025 client opens
    /// its DAT files through the CreateFileW one; it is not in any import table.
    /// </summary>
    private static unsafe void HookLazySlots(IntPtr moduleBase)
    {
        byte* basePtr = (byte*)moduleBase;
        int peOffset = *(int*)(basePtr + 0x3C);
        uint sizeOfImage = *(uint*)(basePtr + peOffset + 24 + 56);
        var image = new ReadOnlySpan<byte>(basePtr, (int)sizeOfImage);
        uint imageBase = (uint)moduleBase.ToInt32();
        var sections = LazyImportSlotScanner.ReadSections(image);

        foreach (string function in new[] { "CreateFileW", "CreateFileA" })
        {
            foreach (LazyImportSlot lazy in LazyImportSlotScanner.FindSlots(image, imageBase, sections, "kernel32.dll", function))
            {
                var slot = (IntPtr)(int)lazy.SlotVa;
                IntPtr current = Marshal.ReadIntPtr(slot);
                // Not resolved yet: the slot still points at its stub inside acclient.exe. Calling
                // the stub would overwrite the slot with the real function (dropping the detour),
                // so the detour calls the kernel32 export the stub would have resolved.
                IntPtr target = current;
                if ((uint)current.ToInt32() - imageBase < sizeOfImage)
                    target = GetProcAddress(GetModuleHandleW("kernel32.dll"), function);
                if (target == IntPtr.Zero)
                {
                    RynthLog.Compat($"Compat: DAT share - kernel32!{function} not found; private slot 0x{lazy.SlotVa:X8} left alone.");
                    continue;
                }
                HookSlot("acclient.exe", function, $"private slot (stub 0x{lazy.StubVa:X8})", slot, current, target);
            }
        }
    }

    /// <summary>
    /// Builds the detour for <paramref name="function"/> calling <paramref name="callTarget"/>,
    /// and points <paramref name="slot"/> at it. <paramref name="restoreValue"/> is what
    /// <see cref="Uninstall"/> writes back.
    /// </summary>
    private static void HookSlot(string moduleName, string function, string kind, IntPtr slot, IntPtr restoreValue, IntPtr callTarget)
    {
        if (function == "CreateFileW")
        {
            CreateFileWDelegate original = Marshal.GetDelegateForFunctionPointer<CreateFileWDelegate>(callTarget);
            CreateFileWDelegate detour = (name, access, share, security, disposition, flags, template) =>
                CreateFileWDetour(original, name, access, share, security, disposition, flags, template);
            InstallSlot(moduleName, function, kind, slot, restoreValue, detour, Marshal.GetFunctionPointerForDelegate(detour));
        }
        else
        {
            CreateFileADelegate original = Marshal.GetDelegateForFunctionPointer<CreateFileADelegate>(callTarget);
            CreateFileADelegate detour = (name, access, share, security, disposition, flags, template) =>
                CreateFileADetour(original, name, access, share, security, disposition, flags, template);
            InstallSlot(moduleName, function, kind, slot, restoreValue, detour, Marshal.GetFunctionPointerForDelegate(detour));
        }
    }

    /// <summary>
    /// Records the hook (so the delegate stays reachable) before the slot is written: the
    /// detour can run on any thread the moment the pointer lands.
    /// </summary>
    private static void InstallSlot(string moduleName, string function, string kind, IntPtr slot, IntPtr original, Delegate detour, IntPtr detourPtr)
    {
        var hook = new IatHook(moduleName, function, slot, original, detour);
        _iatHooks.Add(hook);
        Thread.MemoryBarrier();

        if (WriteIatPointer(slot, detourPtr))
        {
            RynthLog.Info($"Compat: DAT share - hooked {moduleName}!{function} {kind} (slot 0x{slot.ToInt32():X8}, was 0x{original.ToInt32():X8}).");
            return;
        }

        _iatHooks.Remove(hook);
    }

    /// <summary>
    /// Writes every patched slot back to what it held, at engine shutdown. The detours are in
    /// this engine generation, which hot reload unloads; a slot left pointing at them would
    /// crash the next CreateFile.
    /// </summary>
    public static void Uninstall()
    {
        if (_iatHooks.Count == 0)
            return;

        int restored = 0;
        foreach (IatHook hook in _iatHooks)
        {
            if (WriteIatPointer(hook.Slot, hook.Original))
                restored++;
        }

        RynthLog.Info($"Compat: DAT share - restored {restored}/{_iatHooks.Count} CreateFile import slot(s).");
        _iatHooks.Clear();
        IsInstalled = false;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandleW(string? lpModuleName);

    // Plain import (no SetLastError marshalling): it must leave the value it sets.
    [DllImport("kernel32.dll", EntryPoint = "SetLastError")]
    private static extern void SetWin32LastError(uint dwErrCode);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

    /// <summary>
    /// Diagnostic — log every kernel32 import name we find that looks file- or
    /// memory-mapping-related. Useful when the standard CreateFileW/A hook
    /// doesn't catch DAT opens, because it tells us what API AC actually uses.
    /// </summary>
    private static unsafe void EnumerateKernel32FileImports(IntPtr moduleBase)
    {
        try
        {
            byte* basePtr = (byte*)moduleBase;
            int peOffset = *(int*)(basePtr + 0x3C);
            byte* optHeader = basePtr + peOffset + 24;
            uint importDirRva = *(uint*)(optHeader + 96 + 8 * 1);
            if (importDirRva == 0)
                return;

            byte* descriptor = basePtr + importDirRva;
            for (; ; descriptor += 20)
            {
                uint nameRva = *(uint*)(descriptor + 12);
                uint firstThunk = *(uint*)(descriptor + 16);
                if (nameRva == 0 && firstThunk == 0) break;

                string? dllName = Marshal.PtrToStringAnsi((IntPtr)(basePtr + nameRva));
                if (!string.Equals(dllName, "kernel32.dll", StringComparison.OrdinalIgnoreCase))
                    continue;

                uint origFirstThunk = *(uint*)(descriptor + 0);
                uint nameTableRva = origFirstThunk != 0 ? origFirstThunk : firstThunk;
                uint* names = (uint*)(basePtr + nameTableRva);
                int i = 0;
                var hits = new System.Text.StringBuilder("Compat: DAT share - kernel32 file/mmap imports in acclient.exe IAT:");
                int hitCount = 0;
                while (names[i] != 0)
                {
                    uint thunk = names[i];
                    if ((thunk & 0x80000000u) == 0)
                    {
                        string? candidate = Marshal.PtrToStringAnsi((IntPtr)(basePtr + thunk + 2));
                        if (candidate != null &&
                            (candidate.Contains("File", StringComparison.Ordinal) ||
                             candidate.Contains("Mapping", StringComparison.Ordinal) ||
                             candidate.Contains("MapView", StringComparison.Ordinal) ||
                             candidate.StartsWith("_l", StringComparison.Ordinal) ||
                             candidate.StartsWith("_open", StringComparison.Ordinal)))
                        {
                            hits.Append(' ').Append(candidate);
                            hitCount++;
                        }
                    }
                    i++;
                }
                RynthLog.Info(hitCount > 0 ? hits.ToString() : "Compat: DAT share - no file-related kernel32 imports found in acclient.exe IAT.");
                return;
            }

            RynthLog.Info("Compat: DAT share - kernel32.dll import descriptor not found in acclient.exe.");
        }
        catch (Exception ex)
        {
            RynthLog.Compat($"Compat: DAT share - import enumeration failed: {ex.Message}");
        }
    }

    private static IntPtr AcClientGetModuleBase()
    {
        IntPtr moduleBase = GetModuleHandleW("acclient.exe");
        if (moduleBase == IntPtr.Zero)
            moduleBase = GetModuleHandleW(null);
        return moduleBase;
    }

    /// <summary>
    /// Walks the import directory of <paramref name="moduleBase"/> looking for
    /// kernel32.dll's import descriptor, then locates the named import
    /// <paramref name="importName"/>'s IAT slot. Returns the slot's absolute
    /// address (not the function pointer it contains) plus the current
    /// pointer value.
    /// </summary>
    private static unsafe bool TryPatchIatSlot(IntPtr moduleBase, string importName, out IntPtr iatSlot, out IntPtr currentPtr)
    {
        iatSlot = IntPtr.Zero;
        currentPtr = IntPtr.Zero;

        byte* basePtr = (byte*)moduleBase;
        int peOffset = *(int*)(basePtr + 0x3C);
        if (peOffset <= 0 || *(uint*)(basePtr + peOffset) != 0x00004550) // "PE\0\0"
            return false;

        // OPTIONAL_HEADER starts at peOffset + 24 (4-byte sig + 20-byte FILE_HEADER).
        // PE32 magic is 0x010B; DataDirectory starts at OptionalHeader+96.
        byte* optHeader = basePtr + peOffset + 24;
        if (*(ushort*)optHeader != 0x010B)
            return false;

        uint importDirRva = *(uint*)(optHeader + 96 + 8 * 1); // DataDirectory[1] = IMPORT
        if (importDirRva == 0)
            return false;

        byte* descriptor = basePtr + importDirRva;
        // Each IMAGE_IMPORT_DESCRIPTOR is 20 bytes; terminated by all zeros.
        for (; ; descriptor += 20)
        {
            uint nameRva = *(uint*)(descriptor + 12);
            uint firstThunk = *(uint*)(descriptor + 16);
            if (nameRva == 0 && firstThunk == 0)
                break; // End of descriptor list.

            string? dllName = Marshal.PtrToStringAnsi((IntPtr)(basePtr + nameRva));
            if (!string.Equals(dllName, "kernel32.dll", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dllName, "KERNEL32.dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            uint origFirstThunk = *(uint*)(descriptor + 0);
            // INT (Import Name Table) — prefer OriginalFirstThunk if present;
            // some bound imports may have it zeroed, in which case the IAT
            // itself is still a valid name table pre-binding. Both arrays are
            // parallel, 4-byte entries on PE32.
            uint nameTableRva = origFirstThunk != 0 ? origFirstThunk : firstThunk;
            uint* names = (uint*)(basePtr + nameTableRva);
            int i = 0;
            while (names[i] != 0)
            {
                uint thunk = names[i];
                // High bit set = import by ordinal — no name to match.
                if ((thunk & 0x80000000u) == 0)
                {
                    // IMAGE_IMPORT_BY_NAME = { WORD Hint; CHAR Name[]; }
                    string? candidate = Marshal.PtrToStringAnsi((IntPtr)(basePtr + thunk + 2));
                    if (string.Equals(candidate, importName, StringComparison.Ordinal))
                    {
                        iatSlot = (IntPtr)(basePtr + firstThunk + 4 * i);
                        currentPtr = Marshal.ReadIntPtr(iatSlot);
                        return currentPtr != IntPtr.Zero;
                    }
                }
                i++;
            }
        }

        return false;
    }

    private static bool WriteIatPointer(IntPtr iatSlot, IntPtr newValue)
    {
        // IAT lives in .rdata which is read-only after the loader binds it.
        // Flip protection, write, restore. Use the original-protection value
        // from VirtualProtect so we put the page back exactly the way we
        // found it (some loaders mark IAT pages as PAGE_READWRITE post-binding,
        // some PAGE_READONLY).
        if (!VirtualProtect(iatSlot, sizeof(int), PAGE_READWRITE, out uint oldProtect))
        {
            RynthLog.Compat($"Compat: DAT share - VirtualProtect(RW) on IAT slot 0x{iatSlot.ToInt32():X8} failed (error {Marshal.GetLastWin32Error()}).");
            return false;
        }

        try
        {
            Marshal.WriteIntPtr(iatSlot, newValue);
        }
        finally
        {
            VirtualProtect(iatSlot, sizeof(int), oldProtect, out _);
        }

        return true;
    }

    private static bool IsDatPath(string? path)
    {
        return !string.IsNullOrEmpty(path)
            && path.EndsWith(DatExtension, StringComparison.OrdinalIgnoreCase);
    }

    private static IntPtr CreateFileWDetour(
        CreateFileWDelegate original,
        IntPtr lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile)
    {
        string? path = null;
        bool isDat = false;
        bool isAnyDatExt = false;
        uint origAccess = dwDesiredAccess;
        uint origShare = dwShareMode;

        try
        {
            path = Marshal.PtrToStringUni(lpFileName);
            isDat = IsDatPath(path);
            isAnyDatExt = path != null && path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
            if (isDat)
            {
                dwDesiredAccess = GENERIC_READ;
                dwShareMode = FILE_SHARE_ALL;
            }
        }
        catch
        {
        }

        IntPtr result = original(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes,
            dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
        // Captured by the SetLastError=true delegate; the caller may read it even on success.
        int lastError = Marshal.GetLastPInvokeError();

        if (isAnyDatExt)
        {
            int err = result == new IntPtr(-1) ? lastError : 0;
            string fname = Path.GetFileName(path!);
            string note = isDat ? "filtered" : "passthrough";
            RynthLog.Info($"Compat: DAT share - W [{note}]: {fname} access=0x{origAccess:X8}->0x{dwDesiredAccess:X8} share=0x{origShare:X}->0x{dwShareMode:X} result=0x{result.ToInt32():X8}{(err != 0 ? $" GetLastError={err}" : "")}");
        }

        SetWin32LastError((uint)lastError);
        return result;
    }

    private static IntPtr CreateFileADetour(
        CreateFileADelegate original,
        IntPtr lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile)
    {
        string? path = null;
        bool isDat = false;
        bool isAnyDatExt = false;
        uint origAccess = dwDesiredAccess;
        uint origShare = dwShareMode;

        try
        {
            path = Marshal.PtrToStringAnsi(lpFileName);
            isDat = IsDatPath(path);
            isAnyDatExt = path != null && path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase);
            if (isDat)
            {
                dwDesiredAccess = GENERIC_READ;
                dwShareMode = FILE_SHARE_ALL;
            }
        }
        catch
        {
        }

        IntPtr result = original(lpFileName, dwDesiredAccess, dwShareMode, lpSecurityAttributes,
            dwCreationDisposition, dwFlagsAndAttributes, hTemplateFile);
        int lastError = Marshal.GetLastPInvokeError();

        if (isAnyDatExt)
        {
            int err = result == new IntPtr(-1) ? lastError : 0;
            string fname = Path.GetFileName(path!);
            string note = isDat ? "filtered" : "passthrough";
            RynthLog.Info($"Compat: DAT share - A [{note}]: {fname} access=0x{origAccess:X8}->0x{dwDesiredAccess:X8} share=0x{origShare:X}->0x{dwShareMode:X} result=0x{result.ToInt32():X8}{(err != 0 ? $" GetLastError={err}" : "")}");
        }

        SetWin32LastError((uint)lastError);
        return result;
    }
}
