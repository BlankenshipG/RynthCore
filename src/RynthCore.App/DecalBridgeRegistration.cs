using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace RynthCore.App;

/// <summary>
/// Registers (and cleanly unregisters) the RynthCore Decal bridge - the small Decal
/// network filter in src\RynthCore.DecalBridge - in Decal's filter list. See
/// docs\DECAL_BRIDGE_PLAN.md, "Where Decal reads its filters".
///
/// <see cref="RegisterForClients"/> picks the place: the per-user copy below when the AC
/// client will be virtualized (the usual, non-elevated case), the machine-wide HKLM list
/// (administrator rights, once) when it won't be. An elevated or unvirtualized client never
/// reads the per-user copy (2026-10-03: a player's Decal + RynthCore client never loaded the
/// bridge). <see cref="DecalBridgeCheck"/> verifies the result.
///
/// Per-user. The launcher runs without admin rights, so it can't write Decal's own
/// key (HKLM\SOFTWARE\WOW6432Node\Decal\NetworkFilters). acclient.exe is a legacy 32-bit
/// program that Windows "virtualizes": its reads of that HKLM key are served from the
/// per-user copy under HKCU\Software\Classes\VirtualStore when one exists. Registering
/// there makes every Decal client this user starts load the bridge (it stays idle unless
/// RynthCore is in the same client).
///
/// The trap (measured 2026-09-29): once the per-user NetworkFilters key exists, Decal
/// sees ONLY its entries - HKLM's filters (World Object Filter, Echo Filter, ...) vanish,
/// Decal's WorldFilter is null and VTank refuses to start. So when this class has to
/// create that key, it first mirrors every HKLM filter entry into it, and on each
/// Register call it copies over HKLM entries added since.
///
/// What it adds is recorded in %APPDATA%\RynthCore\decal-bridge-registration.json;
/// Unregister removes exactly that: the bridge's own entry, the mirrored copies nobody has
/// changed since, and the per-user key itself only if this class created it and nothing
/// else is left in it. Register is idempotent.
///
/// Opt-in only: nothing calls Register unless the user picked "Decal + RynthCore" for an
/// account (or ran the injector's --decal-bridge register).
/// </summary>
internal static class DecalBridgeRegistration
{
    /// <summary>The bridge's fixed filter id (one entry, whatever the install path).</summary>
    public const string FilterGuid = "{5B3E0D57-2C41-4F8A-9D6E-8C1B70DECA11}";
    public const string FilterObject = "RynthCore.DecalBridge.BridgeFilter";
    public const string AssemblyFile = "RynthCore.DecalBridge.dll";
    public const string FilterName = "RynthCore Decal Bridge";
    /// <summary>Decal's .NET surrogate, the value every Decal.Adapter filter registers with.</summary>
    private const string DotNetSurrogate = "{71A69713-6593-47EC-0002-0000000DECA1}";

    private const string DecalFiltersSubKey = @"SOFTWARE\Decal\NetworkFilters";   // seen through the 32-bit view
    private const RegistryView MachineView = RegistryView.Registry32;   // what the 32-bit client reads

    private static string StatePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore", "decal-bridge-registration.json");

    /// <summary>HKCU path of the per-user copy the virtualized 32-bit client reads.</summary>
    public static string VirtualStoreFiltersSubKey => Environment.Is64BitOperatingSystem
        ? @"Software\Classes\VirtualStore\MACHINE\SOFTWARE\WOW6432Node\Decal\NetworkFilters"
        : @"Software\Classes\VirtualStore\MACHINE\SOFTWARE\Decal\NetworkFilters";

    /// <summary>The folder the launcher registers: &lt;install&gt;\DecalBridge.</summary>
    public static string DefaultBridgeDirectory => Path.Combine(AppContext.BaseDirectory, "DecalBridge");

    public sealed class Status
    {
        public bool DecalInstalled;
        public bool PerUserKeyExists;
        public bool PerUserKeyCreatedByUs;
        public bool RegisteredPerUser;
        public bool RegisteredMachineWide;
        public bool Enabled;
        public bool MachineEnabled;
        public string? RegisteredPath;
        public bool DllPresent;
        /// <summary>A client started by this process gets UAC registry virtualization (reads the per-user copy when it exists).</summary>
        public bool ClientVirtualized = true;
        public string ClientReason = string.Empty;
        /// <summary>HKLM filters a Decal client of this user can't see (hidden by the per-user key).</summary>
        public List<string> HiddenMachineFilters = new();

        /// <summary>What a Decal client started now will load (see DecalBridgeCheck for the full check).</summary>
        public bool EffectiveForClients => ClientVirtualized && PerUserKeyExists
            ? RegisteredPerUser && Enabled
            : RegisteredMachineWide && MachineEnabled;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append($"Decal installed: {DecalInstalled}. ");
            sb.Append($"Bridge registered for clients started now: {(EffectiveForClients ? "yes" : "no")}");
            if (RegisteredPerUser) sb.Append(" (per-user entry present)");
            if (RegisteredMachineWide) sb.Append(" (machine-wide entry present)");
            if (RegisteredPath != null) sb.Append($", path {RegisteredPath}{(DllPresent ? "" : " (DLL MISSING)")}");
            sb.Append('.');
            if (ClientReason.Length > 0) sb.Append($" Client: {ClientReason}.");
            if (PerUserKeyExists)
                sb.Append($" Per-user filter list: {(PerUserKeyCreatedByUs ? "created by RynthCore" : "existed before RynthCore")}.");
            if (HiddenMachineFilters.Count > 0)
                sb.Append($" Hidden from Decal clients (in HKLM, not in the per-user list): {string.Join(", ", HiddenMachineFilters)}.");
            return sb.ToString();
        }
    }

    private sealed class State
    {
        public bool CreatedFiltersKey { get; set; }
        public string BridgePath { get; set; } = string.Empty;
        public DateTime RegisteredUtc { get; set; }
        /// <summary>Mirrored HKLM subkey -> fingerprint of the copy as written.</summary>
        public Dictionary<string, string> MirroredKeys { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Mirrored values of the NetworkFilters key itself -> fingerprint.</summary>
        public Dictionary<string, string> MirroredValues { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    // ── public API ───────────────────────────────────────────────────────────

    public static Status GetStatus(string? acClientPath = null)
    {
        bool virt = RealDecalCheckHost.PredictClientVirtualized(acClientPath ?? DecalLocator.TryGetDecalAcClientPath(), out string reason, out _);
        Status s;
        using (RealRegistryView.Enter()) s = GetStatusCore();
        s.ClientVirtualized = virt;
        s.ClientReason = reason;
        return s;
    }

    private static Status GetStatusCore()
    {
        var s = new Status { DecalInstalled = DecalLocator.IsInstalled() };
        State? state = LoadState();
        using RegistryKey? machine = OpenMachineFilters();
        using RegistryKey? user = OpenUserFilters(writable: false);
        s.PerUserKeyExists = user != null;
        s.PerUserKeyCreatedByUs = user != null && state?.CreatedFiltersKey == true;

        using (RegistryKey? m = machine?.OpenSubKey(FilterGuid))
        {
            if (m != null && IsOurs(m))
            {
                s.RegisteredMachineWide = true;
                s.MachineEnabled = Enabled(m);
                if (user == null) ReadEntry(m, s);
            }
        }
        using (RegistryKey? u = user?.OpenSubKey(FilterGuid))
        {
            if (u != null && IsOurs(u))
            {
                s.RegisteredPerUser = true;
                ReadEntry(u, s);
            }
        }
        if (machine != null && user != null)
        {
            HashSet<string> seen = new(user.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
            foreach (string name in machine.GetSubKeyNames())
                if (!seen.Contains(name))
                    s.HiddenMachineFilters.Add(DescribeFilter(machine, name));
        }
        return s;
    }

    public enum Outcome { Registered, NeedsAdmin, Failed }

    /// <summary>
    /// Registers the bridge where a Decal client started now reads its filter list, then
    /// verifies it with <see cref="DecalBridgeCheck"/> (reading back from that place, and
    /// through this process's own view when it is the client's).
    ///   - Client virtualized (non-elevated AC, the usual case): the per-user VirtualStore copy,
    ///     as before (no administrator rights needed).
    ///   - Client not virtualized (RynthCore/AC run as administrator, UAC off, a manifest,
    ///     virtualization off by policy): Decal reads the real
    ///     HKLM\SOFTWARE\(WOW6432Node\)Decal\NetworkFilters, where a per-user entry is
    ///     invisible. Written directly when this process may; otherwise <see cref="Outcome.NeedsAdmin"/>,
    ///     and the caller runs <c>--decal-bridge-register-machine</c> elevated (<see cref="RunElevated"/>).
    /// Idempotent. Also removes a "downloaded from the internet" mark from the bridge DLL.
    /// </summary>
    public static Outcome RegisterForClients(string bridgeDirectory, string? acClientPath, string? engineJsonPath,
        out string report, out DecalBridgeCheck.Result? check)
    {
        check = null;
        var log = new List<string>();
        string dir;
        try { dir = Path.GetFullPath(bridgeDirectory).TrimEnd('\\'); }
        catch (Exception ex) { report = $"Bad bridge folder '{bridgeDirectory}': {ex.Message}"; return Outcome.Failed; }

        string dll = Path.Combine(dir, AssemblyFile);
        if (File.Exists(dll) && File.Exists(dll + ":Zone.Identifier"))
            log.Add(RealDecalCheckHost.RemoveZoneIdentifier(dll)
                ? "removed the 'downloaded from the internet' mark from the bridge DLL"
                : "could not remove the 'downloaded from the internet' mark from the bridge DLL");

        bool virtualized = RealDecalCheckHost.PredictClientVirtualized(acClientPath, out string why, out _);
        bool ok;
        string step;
        if (virtualized)
        {
            ok = RegisterPerUser(dir, out step);
        }
        else
        {
            Outcome mw = RegisterMachineWide(dir, out step);
            if (mw == Outcome.NeedsAdmin)
            {
                log.Add($"AC reads Decal's filter list for all users on this PC ({why}); adding the bridge there needs administrator rights once.");
                report = string.Join(" ", log);
                return Outcome.NeedsAdmin;
            }
            ok = mw == Outcome.Registered;
            step = $"({why}) " + step;
        }
        log.Add(step);

        check = RealDecalCheckHost.Check(dir, acClientPath, engineJsonPath);
        log.Add(check.Summary);
        report = string.Join(" ", log);
        if (!ok) return Outcome.Failed;
        if (check.Blocking) return check.NeedsMachineWideRegistration ? Outcome.NeedsAdmin : Outcome.Failed;
        return Outcome.Registered;
    }

    /// <summary>
    /// <see cref="RegisterForClients"/> for Decal's own acclient.exe (PortalPath). False (with
    /// the reason) when it needs administrator rights or the check fails.
    /// </summary>
    public static bool Register(string bridgeDirectory, out string report) =>
        RegisterForClients(bridgeDirectory, DecalLocator.TryGetDecalAcClientPath(), null, out report, out _) == Outcome.Registered;

    /// <summary>
    /// Adds the bridge to the machine-wide list, HKLM\SOFTWARE\(WOW6432Node\)Decal\NetworkFilters
    /// (32-bit view). Needs administrator rights: run elevated (--decal-bridge-register-machine).
    /// Virtualization is off while it writes, so a non-elevated process gets "access denied"
    /// (NeedsAdmin) instead of a silent write into the per-user copy. Idempotent; nothing to mirror
    /// (the machine list is the one every other filter is in).
    /// </summary>
    public static Outcome RegisterMachineWide(string bridgeDirectory, out string report)
    {
        var log = new List<string>();
        using (RealRegistryView.Enter())
        {
            try
            {
                if (!DecalLocator.IsInstalled())
                {
                    report = "Decal is not installed (HKLM\\SOFTWARE\\Decal\\Agent) - nothing registered.";
                    return Outcome.Failed;
                }
                string dir = Path.GetFullPath(bridgeDirectory).TrimEnd('\\');
                if (!File.Exists(Path.Combine(dir, AssemblyFile)))
                {
                    report = $"{AssemblyFile} not found in {dir} - nothing registered.";
                    return Outcome.Failed;
                }
                using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, MachineView);
                using RegistryKey filters = hklm.CreateSubKey(DecalFiltersSubKey, writable: true);
                using (RegistryKey? existing = filters.OpenSubKey(FilterGuid))
                {
                    if (existing != null && !IsOurs(existing))
                    {
                        report = $"another filter uses the bridge's id {FilterGuid} in the machine-wide list - nothing changed.";
                        return Outcome.Failed;
                    }
                }
                using (RegistryKey entry = filters.CreateSubKey(FilterGuid, writable: true))
                {
                    SetIfDifferent(entry, "", FilterName, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Enabled", 1, RegistryValueKind.DWord, log);
                    SetIfDifferent(entry, "Assembly", AssemblyFile, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Path", dir, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Object", FilterObject, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Surrogate", DotNetSurrogate, RegistryValueKind.String, log);
                }
                report = log.Count == 0
                    ? $"Decal bridge already registered for all users ({dir}); nothing changed."
                    : $"Decal bridge registered for all users ({dir}): {string.Join("; ", log)}.";
                return Outcome.Registered;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                report = "Registering the Decal bridge for all users needs administrator rights.";
                return Outcome.NeedsAdmin;
            }
            catch (Exception ex)
            {
                report = $"Decal bridge registration for all users failed: {Describe(ex)}";
                return Outcome.Failed;
            }
        }
    }

    /// <summary>
    /// The installer's per-user half (installer\RynthCore.iss, run as the installing user on
    /// every install and update; the elevated half is <see cref="RegisterMachineWide"/>).
    /// Writes our entry into this user's VirtualStore copy of Decal's filter list ONLY when
    /// that copy already exists - see <see cref="DecalBridgeCheck.PlanInstall"/> for why both
    /// places are written and why a missing copy is never created here. The entry is ours
    /// (Object = <see cref="FilterObject"/>), so <see cref="Unregister"/> removes it.
    /// </summary>
    public static bool InstallPerUser(string bridgeDirectory, out string report)
    {
        try
        {
            var plan = RealDecalCheckHost.PlanInstall();
            if (!plan.Machine)
            {
                report = "Decal is not installed - nothing to do for this user.";
                return true;
            }
            if (!plan.PerUser)
            {
                report = "This user has no per-user copy of Decal's filter list - the machine-wide entry is the one AC reads; nothing written.";
                return true;
            }
            string dir = Path.GetFullPath(bridgeDirectory).TrimEnd('\\');
            if (!File.Exists(Path.Combine(dir, AssemblyFile)))
            {
                report = $"{AssemblyFile} not found in {dir} - nothing written.";
                return false;
            }
            var log = new List<string>();
            using (RealRegistryView.Enter())
            {
                using RegistryKey? user = OpenUserFilters(writable: true);
                if (user == null)
                {
                    report = "The per-user copy disappeared - nothing written.";
                    return true;
                }
                using (RegistryKey? existing = user.OpenSubKey(FilterGuid))
                {
                    if (existing != null && !IsOurs(existing))
                    {
                        report = $"another filter uses the bridge's id {FilterGuid} in the per-user copy - nothing changed.";
                        return false;
                    }
                }
                using RegistryKey entry = user.CreateSubKey(FilterGuid, writable: true);
                SetIfDifferent(entry, "", FilterName, RegistryValueKind.String, log);
                SetIfDifferent(entry, "Enabled", 1, RegistryValueKind.DWord, log);
                SetIfDifferent(entry, "Assembly", AssemblyFile, RegistryValueKind.String, log);
                SetIfDifferent(entry, "Path", dir, RegistryValueKind.String, log);
                SetIfDifferent(entry, "Object", FilterObject, RegistryValueKind.String, log);
                SetIfDifferent(entry, "Surrogate", DotNetSurrogate, RegistryValueKind.String, log);
            }
            report = log.Count == 0
                ? $"Decal bridge already in this user's per-user copy ({dir}); nothing changed."
                : $"Decal bridge added to this user's existing per-user copy ({dir}): {string.Join("; ", log)}.";
            return true;
        }
        catch (Exception ex)
        {
            report = $"Per-user Decal bridge entry failed: {Describe(ex)}";
            return false;
        }
    }

    /// <summary>The machine-wide list holds our entry (the launcher/injector never adds it without being asked).</summary>
    public static bool MachineEntryIsOurs()
    {
        using (RealRegistryView.Enter())
        {
            try
            {
                using RegistryKey? machine = OpenMachineFilters();
                using RegistryKey? m = machine?.OpenSubKey(FilterGuid);
                return m != null && IsOurs(m);
            }
            catch { return false; }
        }
    }

    /// <summary>Removes our machine-wide entry (only ours). Needs administrator rights.</summary>
    public static Outcome UnregisterMachineWide(out string report)
    {
        using (RealRegistryView.Enter())
        {
            try
            {
                using RegistryKey hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, MachineView);
                using RegistryKey? probe = hklm.OpenSubKey(DecalFiltersSubKey);
                using RegistryKey? m = probe?.OpenSubKey(FilterGuid);
                if (m == null || !IsOurs(m))
                {
                    report = "No machine-wide Decal bridge entry; nothing to remove.";
                    return Outcome.Registered;
                }
                using RegistryKey filters = hklm.OpenSubKey(DecalFiltersSubKey, writable: true)!;
                filters.DeleteSubKeyTree(FilterGuid, throwOnMissingSubKey: false);
                report = "Removed the machine-wide Decal bridge entry.";
                return Outcome.Registered;
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
            {
                report = "Removing the machine-wide Decal bridge entry needs administrator rights.";
                return Outcome.NeedsAdmin;
            }
            catch (Exception ex)
            {
                report = $"Removing the machine-wide Decal bridge entry failed: {Describe(ex)}";
                return Outcome.Failed;
            }
        }
    }

    /// <summary>
    /// Runs <paramref name="exePath"/> elevated (Windows shows its administrator prompt) and
    /// waits for it. NeedsAdmin when the person said no at the prompt.
    /// </summary>
    public static Outcome RunElevated(string exePath, string arguments, out string report)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(exePath, arguments)
            {
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            };
            using System.Diagnostics.Process? p = System.Diagnostics.Process.Start(psi);
            if (p == null)
            {
                report = "Windows did not start the administrator step.";
                return Outcome.Failed;
            }
            if (!p.WaitForExit(120_000))
            {
                report = "The administrator step did not finish within 2 minutes.";
                return Outcome.Failed;
            }
            report = p.ExitCode == 0 ? "The administrator step finished." : $"The administrator step failed (exit code {p.ExitCode}).";
            return p.ExitCode == 0 ? Outcome.Registered : Outcome.Failed;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            report = "Cancelled at the Windows administrator prompt.";
            return Outcome.NeedsAdmin;
        }
        catch (Exception ex)
        {
            report = $"Could not start the administrator step: {ex.Message}";
            return Outcome.Failed;
        }
    }

    /// <summary>
    /// Makes Decal clients of this user load the bridge from <paramref name="bridgeDirectory"/>
    /// through the per-user VirtualStore copy (virtualized clients only: see
    /// <see cref="RegisterForClients"/>). Idempotent. Returns false (with the reason in
    /// <paramref name="report"/>) when Decal isn't installed or the bridge DLL isn't there.
    /// </summary>
    public static bool RegisterPerUser(string bridgeDirectory, out string report)
    {
        using (RealRegistryView.Enter()) return RegisterCore(bridgeDirectory, out report);
    }

    private static bool RegisterCore(string bridgeDirectory, out string report)
    {
        var log = new List<string>();
        try
        {
            if (!DecalLocator.IsInstalled())
            {
                report = "Decal is not installed (HKLM\\SOFTWARE\\Decal\\Agent) - nothing registered.";
                return false;
            }
            string dir = Path.GetFullPath(bridgeDirectory).TrimEnd('\\');
            if (!File.Exists(Path.Combine(dir, AssemblyFile)))
            {
                report = $"{AssemblyFile} not found in {dir} - nothing registered.";
                return false;
            }

            State state = LoadState() ?? new State();
            using RegistryKey? machine = OpenMachineFilters();
            using RegistryKey? existingUser = OpenUserFilters(writable: true);
            if (existingUser == null && state.CreatedFiltersKey)
            {
                // Someone removed the key we created; start the record over.
                state = new State();
            }

            RegistryKey? user = existingUser;
            try
            {
                if (user == null)
                {
                    // No per-user list yet. If an administrator already registered the bridge in
                    // HKLM with this path, clients see it without us creating anything.
                    using (RegistryKey? m = machine?.OpenSubKey(FilterGuid))
                    {
                        if (m != null && IsOurs(m) && Enabled(m) &&
                            string.Equals((m.GetValue("Path") as string)?.TrimEnd('\\'), dir, StringComparison.OrdinalIgnoreCase))
                        {
                            report = "Already registered machine-wide (HKLM) with this path; nothing changed.";
                            return true;
                        }
                    }
                    user = UserHive().CreateSubKey(VirtualStoreFiltersSubKey, writable: true);
                    state.CreatedFiltersKey = true;
                    log.Add($"created HKCU\\{VirtualStoreFiltersSubKey}");
                }

                if (state.CreatedFiltersKey && machine != null)
                    SyncMirror(machine, user, state, log);

                using (RegistryKey entry = user.CreateSubKey(FilterGuid, writable: true))
                {
                    SetIfDifferent(entry, "", FilterName, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Enabled", 1, RegistryValueKind.DWord, log);
                    SetIfDifferent(entry, "Assembly", AssemblyFile, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Path", dir, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Object", FilterObject, RegistryValueKind.String, log);
                    SetIfDifferent(entry, "Surrogate", DotNetSurrogate, RegistryValueKind.String, log);
                }
            }
            finally
            {
                if (!ReferenceEquals(user, existingUser)) user?.Dispose();
            }

            state.BridgePath = dir;
            state.RegisteredUtc = DateTime.UtcNow;
            SaveState(state);
            report = log.Count == 0
                ? $"Decal bridge already registered for this user ({dir}); nothing changed."
                : $"Decal bridge registered for this user ({dir}): {string.Join("; ", log)}.";
            return true;
        }
        catch (Exception ex)
        {
            report = $"Decal bridge registration failed: {ex.GetType().Name}: {ex.Message}" +
                (log.Count > 0 ? $" (done before the failure: {string.Join("; ", log)})" : "");
            return false;
        }
    }

    /// <summary>
    /// Removes what <see cref="Register"/> added: the bridge's entry, the mirrored copies of
    /// HKLM filters, and the per-user key if this class created it and nothing foreign is
    /// left in it. A mirrored copy that changed since (Decal plugins write to "HKLM" through
    /// UAC virtualization, so their writes land in our per-user copy) is removed as well when
    /// RynthCore created the key: HKLM still holds the original, and a per-user list left with
    /// a few entries would hide every other HKLM filter from Decal clients (VTank then has no
    /// WorldFilter). Anything RynthCore didn't add is left in place and reported, and the
    /// record (%APPDATA%\RynthCore\decal-bridge-registration.json) is kept until nothing of
    /// ours remains, so a later unregister can finish the job.
    /// </summary>
    public static bool Unregister(out string report)
    {
        using (RealRegistryView.Enter()) return UnregisterCore(out report);
    }

    private static bool UnregisterCore(out string report)
    {
        var log = new List<string>();
        var failed = new List<string>();
        try
        {
            State? state = LoadState();
            bool done = true;
            using RegistryKey? user = OpenUserFilters(writable: true);
            if (user != null)
            {
                foreach (string name in user.GetSubKeyNames())
                {
                    try
                    {
                        bool ours;
                        using (RegistryKey? k = user.OpenSubKey(name))
                            ours = k != null && IsOurs(k);
                        if (!ours) continue;
                        user.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                        log.Add($"removed bridge entry {name}");
                    }
                    catch (Exception ex) { failed.Add($"{name}: {Describe(ex)}"); }
                }

                if (state?.CreatedFiltersKey == true)
                {
                    int same = 0, changed = 0;
                    foreach (KeyValuePair<string, string> kv in state.MirroredKeys)
                    {
                        try
                        {
                            bool unchanged;
                            using (RegistryKey? k = user.OpenSubKey(kv.Key))
                            {
                                if (k == null) continue;
                                unchanged = Fingerprint(k) == kv.Value;
                            }
                            user.DeleteSubKeyTree(kv.Key, throwOnMissingSubKey: false);
                            if (unchanged) same++; else changed++;
                        }
                        catch (Exception ex) { failed.Add($"{kv.Key}: {Describe(ex)}"); }
                    }
                    foreach (KeyValuePair<string, string> kv in state.MirroredValues)
                    {
                        try { user.DeleteValue(kv.Key, throwOnMissingValue: false); }
                        catch (Exception ex) { failed.Add($"value {kv.Key}: {Describe(ex)}"); }
                    }
                    log.Add($"removed {same + changed} mirrored filter entr{(same + changed == 1 ? "y" : "ies")}" +
                        (changed > 0 ? $" ({changed} changed by Decal plugins since; HKLM keeps the originals)" : ""));

                    var foreign = new List<string>();
                    foreach (string n in user.GetSubKeyNames()) foreign.Add(n);
                    foreach (string n in user.GetValueNames()) if (n.Length > 0) foreign.Add(n);
                    if (foreign.Count == 0 && failed.Count == 0)
                    {
                        user.Dispose();
                        UserHive().DeleteSubKey(VirtualStoreFiltersSubKey, throwOnMissingSubKey: false);
                        log.Add($"removed HKCU\\{VirtualStoreFiltersSubKey} (created by RynthCore)");
                    }
                    else
                    {
                        done = false;
                        log.Add($"left HKCU\\{VirtualStoreFiltersSubKey} in place" +
                            (foreign.Count > 0 ? $": it holds entries RynthCore didn't add ({string.Join(", ", foreign)})" : ""));
                    }
                }
            }

            if (failed.Count > 0)
                done = false;
            if (done && File.Exists(StatePath))
                File.Delete(StatePath);
            else if (!done)
                log.Add($"kept {StatePath} for the next unregister");
            if (failed.Count > 0)
            {
                report = $"Decal bridge unregistration incomplete: {string.Join("; ", failed)}" +
                    (log.Count > 0 ? $" (done: {string.Join("; ", log)})" : "");
                return false;
            }
            report = log.Count == 0 ? "Decal bridge was not registered for this user; nothing to remove." : string.Join("; ", log) + ".";
            return true;
        }
        catch (Exception ex)
        {
            report = $"Decal bridge unregistration failed: {Describe(ex)}" +
                (log.Count > 0 ? $" (done before the failure: {string.Join("; ", log)})" : "");
            return false;
        }
    }

    // Exception type, message and the innermost frame, for a one-line report.
    private static string Describe(Exception ex)
    {
        string? frame = ex.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0);
        return $"{ex.GetType().Name}: {ex.Message}" + (frame != null ? $" [{frame}]" : "");
    }

    // ── mirror ───────────────────────────────────────────────────────────────

    // Copies HKLM entries the per-user list lacks, and refreshes copies nobody changed
    // since we wrote them (so a filter the user enables/disables machine-wide follows).
    private static void SyncMirror(RegistryKey machine, RegistryKey user, State state, List<string> log)
    {
        int added = 0, refreshed = 0;
        foreach (string name in machine.GetValueNames())
        {
            if (name.Length == 0) continue;
            object? v = machine.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v == null) continue;
            RegistryValueKind kind = machine.GetValueKind(name);
            object? have = user.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            bool ours = state.MirroredValues.TryGetValue(name, out string? print);
            if (have == null || (ours && ValuePrint(user.GetValueKind(name), have) == print))
            {
                user.SetValue(name, v, kind);
                state.MirroredValues[name] = ValuePrint(kind, v);
            }
        }
        foreach (string name in machine.GetSubKeyNames())
        {
            if (string.Equals(name, FilterGuid, StringComparison.OrdinalIgnoreCase)) continue;
            using RegistryKey? src = machine.OpenSubKey(name);
            if (src == null) continue;
            string srcPrint = Fingerprint(src);
            using RegistryKey? existing = user.OpenSubKey(name);
            if (existing == null)
            {
                using RegistryKey dst = user.CreateSubKey(name, writable: true);
                CopyTree(src, dst);
                state.MirroredKeys[name] = Fingerprint(dst);
                added++;
            }
            else if (state.MirroredKeys.TryGetValue(name, out string? written) &&
                     Fingerprint(existing) == written && srcPrint != written)
            {
                existing.Dispose();
                user.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                using RegistryKey dst = user.CreateSubKey(name, writable: true);
                CopyTree(src, dst);
                state.MirroredKeys[name] = Fingerprint(dst);
                refreshed++;
            }
        }
        if (added > 0) log.Add($"mirrored {added} HKLM filter entr{(added == 1 ? "y" : "ies")} (so Decal still sees them)");
        if (refreshed > 0) log.Add($"refreshed {refreshed} mirrored entr{(refreshed == 1 ? "y" : "ies")} from HKLM");
    }

    private static void CopyTree(RegistryKey src, RegistryKey dst)
    {
        foreach (string name in src.GetValueNames())
        {
            object? v = src.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v != null) dst.SetValue(name, v, src.GetValueKind(name));
        }
        foreach (string sub in src.GetSubKeyNames())
        {
            using RegistryKey? s = src.OpenSubKey(sub);
            if (s == null) continue;
            using RegistryKey d = dst.CreateSubKey(sub, writable: true);
            CopyTree(s, d);
        }
    }

    /// <summary>A stable text form of a key's values and subkeys, to tell "still as we wrote it".</summary>
    private static string Fingerprint(RegistryKey key)
    {
        var sb = new StringBuilder();
        foreach (string name in key.GetValueNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            object? v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (v == null) continue;
            RegistryValueKind kind;
            try { kind = key.GetValueKind(name); }
            catch (IOException) { continue; }   // removed between the listing and the read
            sb.Append(name.ToLowerInvariant()).Append('=').Append(ValuePrint(kind, v)).Append(';');
        }
        foreach (string sub in key.GetSubKeyNames().OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            using RegistryKey? s = key.OpenSubKey(sub);
            if (s != null) sb.Append('[').Append(sub.ToLowerInvariant()).Append(':').Append(Fingerprint(s)).Append(']');
        }
        return sb.ToString();
    }

    private static string ValuePrint(RegistryValueKind kind, object v) => kind + ":" + v switch
    {
        byte[] b => Convert.ToHexString(b),
        string[] a => string.Join("\u0001", a),
        _ => Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
    };

    // ── registry helpers ─────────────────────────────────────────────────────

    private static RegistryKey UserHive() => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser,
        Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);

    private static RegistryKey? OpenUserFilters(bool writable) => UserHive().OpenSubKey(VirtualStoreFiltersSubKey, writable);

    private static RegistryKey? OpenMachineFilters() =>
        RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, MachineView).OpenSubKey(DecalFiltersSubKey);

    private static bool IsOurs(RegistryKey k) =>
        string.Equals(k.GetValue("Object") as string, FilterObject, StringComparison.Ordinal);

    private static bool Enabled(RegistryKey k) => k.GetValue("Enabled") is int e && e != 0;

    private static void ReadEntry(RegistryKey k, Status s)
    {
        s.Enabled = Enabled(k);
        s.RegisteredPath = k.GetValue("Path") as string;
        s.DllPresent = s.RegisteredPath != null && File.Exists(Path.Combine(s.RegisteredPath, AssemblyFile));
    }

    private static string DescribeFilter(RegistryKey parent, string name)
    {
        using RegistryKey? k = parent.OpenSubKey(name);
        return k?.GetValue("") is string friendly && friendly.Length > 0 ? friendly : name;
    }

    private static void SetIfDifferent(RegistryKey key, string name, object value, RegistryValueKind kind, List<string> log)
    {
        object? have = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (have != null && key.GetValueKind(name) == kind && Equals(have, value))
            return;
        key.SetValue(name, value, kind);
        log.Add($"set {(name.Length == 0 ? "(default)" : name)}");
    }

    private static State? LoadState()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            State? s = JsonSerializer.Deserialize<State>(File.ReadAllText(StatePath));
            if (s == null) return null;
            // Dictionary comparers don't survive serialization.
            s.MirroredKeys = new Dictionary<string, string>(s.MirroredKeys, StringComparer.OrdinalIgnoreCase);
            s.MirroredValues = new Dictionary<string, string>(s.MirroredValues, StringComparer.OrdinalIgnoreCase);
            return s;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveState(State s)
    {
        string? dir = Path.GetDirectoryName(StatePath);
        if (dir != null) Directory.CreateDirectory(dir);
        File.WriteAllText(StatePath, JsonSerializer.Serialize(s, new JsonSerializerOptions { WriteIndented = true }));
    }
}
