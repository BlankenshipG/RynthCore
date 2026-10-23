// ============================================================================
//  RynthCore.Engine - Compatibility/AcKeymapLive.cs
//
//  Changing the player's keys so every running client follows (Tom 2026-10-22:
//  the item bar's keys are the player's retail keys, and they change together;
//  with the retail toolbar back, its keys have changed too). Plugin API v90
//  KeyCapture / SetActionKey, for RynthDefective's bar, and /rc keymap.
//
//  AC keeps its keymap in memory: it loads the file when it starts and writes
//  it back when it closes, and its own Configure Keyboard changes only the
//  memory. So a change is, on AC's game thread after its tick:
//    1. this client's keys go to the file first (CInputManager_WIN32::
//       SaveKeyMap, vtable +0x30, what Configure Keyboard's "save as" does), so
//       a key the player changed there isn't lost;
//    2. the file's text is edited (AcKeymapText: the key comes off whatever had
//       it, and off RynthCore's own hotkeys, which would take it before AC);
//    3. it is loaded again the way AC loads it when it starts (gmClient::
//       InitKeymap): InitializeKeymap (+0x08, clears), AddKeyMap_(path) (+0x28,
//       the file), AddKeyMap(0x10000001) and AddKeyMap(1) (+0x2C, the game's
//       fixed maps from the dat);
//    4. the file's new time goes in "<keymap>.rynthcore" beside it.
//  Every other client looks at the file once a second and loads it the same
//  way when it changed with that time noted. A file a closing client wrote (AC's
//  own save on shutdown) isn't loaded: that would throw away keys the others
//  changed in Configure Keyboard and haven't saved, as AC would never do.
//
//  A key for one of RynthCore's own commands (RynthDefective's slots past the
//  game's 18: "/rdef use 2") is an engine hotkey; it takes its key the same way
//  (off AC's actions too, so no key shows on two slots).
//
//  ICIDM::s_cidm (the CInputManager*, by code-xref), the four functions and
//  PStringBase<char>(const char*) (0x00401340) are found by pattern only, and
//  the input manager's vtable must hold exactly those functions, or nothing is
//  called. The first change backs the file up as "<keymap>.before-rynthcore".
// ============================================================================

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using RynthCore.Engine.Hotkeys;

namespace RynthCore.Engine.Compatibility;

internal static unsafe class AcKeymapLive
{
    // ClientUISystem::UseTime: mov ebp,[ICIDM::s_cidm]; push esi; push edi; lea eax,[esp+0x10] (as LogoffOriginProbe).
    private static readonly byte?[] InputManagerXref = [0x8B, 0x2D, null, null, null, null, 0x56, 0x57, 0x8D, 0x44, 0x24, 0x10];
    // CInputManager_WIN32::InitializeKeymap: this->ClearKeyMap(); if (m_actionMap) m_actionMap->vt54(); return 1.
    private static readonly byte?[] InitializePattern =
        [0x56, 0x8B, 0xF1, 0x8B, 0x06, 0xFF, 0x50, 0x24, 0x8B, 0x8E, 0x8C, 0x00, 0x00, 0x00, 0x85, 0xC9, 0x5E, 0x74, 0x05, 0x8B, 0x11, 0xFF, 0x52, 0x54, 0xB0, 0x01, 0xC3];
    // CInputManager_WIN32::AddKeyMap_(PStringBase<char> file): PFileParser::LoadFromFile, CMasterInputMap::Merge.
    private static readonly byte?[] LoadFilePattern =
        [0xA1, null, null, null, null, 0x81, 0xEC, 0x68, 0x01, 0x00, 0x00, 0x85, 0xC0, 0x56, 0x57, 0x8B, 0xF9, 0x75, 0x0A, 0xB9, null, null, null, null, 0xE8];
    // CInputManager_WIN32::AddKeyMap(uint id): DBObj::GetByEnum(id, 10, 0x1D), CMasterInputMap::Merge.
    private static readonly byte?[] LoadDbPattern =
        [0x8B, 0x44, 0x24, 0x04, 0x56, 0x57, 0x6A, 0x1D, 0x6A, 0x0A, 0x50, 0x8B, 0xF9, 0xE8, null, null, null, null, 0x8B, 0xF0, 0x83, 0xC4, 0x0C, 0x32, 0xC0, 0x85, 0xF6, 0x74, 0x1B, 0x53, 0x6A, 0x01, 0x56];
    // CInputManager_WIN32::SaveKeyMap(PStringBase<char> file): CMasterInputMap::ToFileNode, PFileParser::SaveToFile.
    private static readonly byte?[] SavePattern =
        [0xA1, null, null, null, null, 0x83, 0xEC, 0x20, 0x85, 0xC0, 0x56, 0x57, 0x8B, 0xF9, 0x75, 0x0A, 0xB9, null, null, null, null, 0xE8, null, null, null, null, 0x8B, 0x0D];
    // PStringBase<char>::PStringBase<char>(const char*): the string's characters, refcount at -0x10.
    private static readonly byte?[] StringPattern =
        [0x56, 0x8B, 0x74, 0x24, 0x08, 0x85, 0xF6, 0x57, 0x8B, 0xF9, 0x74, 0x35, 0x80, 0x3E, 0x00, 0x74, 0x30, 0x8B, 0xC6, 0x8D, 0x50, 0x01,
         0x8A, 0x08, 0x40, 0x84, 0xC9, 0x75, 0xF9, 0x2B, 0xC2, 0x50, 0x8B, 0xCF, 0xE8, null, null, null, null, 0x8B, 0x17];

    private const int VtInitialize = 0x08, VtLoadFile = 0x28, VtLoadDb = 0x2C, VtSave = 0x30;
    private const uint FixedMapA = 0x10000001, FixedMapB = 1;   // the dat's maps gmClient::InitKeymap merges after the file

    private static IntPtr _cidmSlot, _init, _loadFile, _loadDb, _save, _string;
    private static bool _initialized;
    private static string _status = "Not initialized.";
    public static string Status => _status;
    public static bool CanChange => _cidmSlot != IntPtr.Zero && _init != IntPtr.Zero && _loadFile != IntPtr.Zero
                                    && _loadDb != IntPtr.Zero && _save != IntPtr.Zero && _string != IntPtr.Zero;

    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;
        if (!AcClientModule.TryReadTextSection(out AcClientTextSection text))
        {
            _status = "acclient.exe not available.";
            return;
        }
        IntPtr Fn(string name, byte?[] pattern)
        {
            var r = HookResolver.Resolve(text, "AcKeymapLive." + name, pattern, 0);
            return r.Success && r.Source == HookResolver.ResolveSource.PatternScan ? r.Address : IntPtr.Zero;
        }
        var slot = HookResolver.ResolveData(text, "AcKeymapLive.ICIDM_s_cidm", InputManagerXref, 2, 0);
        _cidmSlot = slot.Success && slot.Source == HookResolver.ResolveSource.PatternScan ? slot.Address : IntPtr.Zero;
        _init = Fn("InitializeKeymap", InitializePattern);
        _loadFile = Fn("AddKeyMap_", LoadFilePattern);
        _loadDb = Fn("AddKeyMap", LoadDbPattern);
        _save = Fn("SaveKeyMap", SavePattern);
        _string = Fn("PStringBase_char_ctor", StringPattern);
        _status = CanChange ? "ready." : "missing " + string.Join(", ", Missing()) + ": keys can't be changed from here.";
        RynthLog.Compat("AcKeymapLive: " + _status);
    }

    private static IEnumerable<string> Missing()
    {
        if (_cidmSlot == IntPtr.Zero) yield return "the input manager";
        if (_init == IntPtr.Zero) yield return "InitializeKeymap";
        if (_loadFile == IntPtr.Zero) yield return "AddKeyMap_";
        if (_loadDb == IntPtr.Zero) yield return "AddKeyMap";
        if (_save == IntPtr.Zero) yield return "SaveKeyMap";
        if (_string == IntPtr.Zero) yield return "PStringBase";
    }

    /// <summary>The CInputManager, when its vtable holds the functions found (else zero: never called).</summary>
    private static IntPtr InputManager()
    {
        if (!CanChange || !ClientObjectHooks.IsReadableSpan(_cidmSlot, 4)) return IntPtr.Zero;
        IntPtr im = Marshal.ReadIntPtr(_cidmSlot);
        if (im == IntPtr.Zero || !ClientObjectHooks.IsReadableSpan(im, 4)) return IntPtr.Zero;
        IntPtr vt = Marshal.ReadIntPtr(im);
        if (vt == IntPtr.Zero || !ClientObjectHooks.IsReadableSpan(vt, VtSave + 4)) return IntPtr.Zero;
        bool ok = Marshal.ReadIntPtr(vt, VtInitialize) == _init && Marshal.ReadIntPtr(vt, VtLoadFile) == _loadFile
                  && Marshal.ReadIntPtr(vt, VtLoadDb) == _loadDb && Marshal.ReadIntPtr(vt, VtSave) == _save;
        if (!ok && !_vtWarned)
        {
            _vtWarned = true;
            RynthLog.Compat($"AcKeymapLive: the input manager's vtable 0x{vt.ToInt32():X8} isn't the expected one; keys won't be reloaded.");
        }
        return ok ? im : IntPtr.Zero;
    }

    private static bool _vtWarned;

    /// <summary>An AC PStringBase&lt;char&gt; of the path (one reference, handed to the callee); zero for a path AC can't take.</summary>
    private static IntPtr AcString(string path)
    {
        string p = path;
        if (!IsAscii(p))
        {
            // AC opens files with the ANSI functions: a name with other letters goes by its short (8.3) form.
            var sb = new StringBuilder(520);
            if (GetShortPathNameW(path, sb, sb.Capacity) > 0) p = sb.ToString();
            if (!IsAscii(p)) return IntPtr.Zero;
        }
        byte[] bytes = new byte[p.Length + 1];
        for (int i = 0; i < p.Length; i++) bytes[i] = (byte)p[i];
        IntPtr s = IntPtr.Zero;
        fixed (byte* b = bytes)
            ((delegate* unmanaged[Thiscall]<IntPtr*, byte*, IntPtr*>)_string)(&s, b);
        return s;
    }

    private static bool IsAscii(string s)
    {
        foreach (char c in s) if (c >= 0x80 || c == 0) return false;
        return true;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetShortPathNameW(string longPath, StringBuilder shortPath, int size);

    // ── Watching (every client) ──────────────────────────────────────────────

    private static DateTime _seenUtc = DateTime.MinValue;
    private static string _seenFile = string.Empty;
    private static long _nextCheckMs;
    private static readonly DateTime ProcessStartUtc = SafeStartUtc();

    private static DateTime SafeStartUtc()
    {
        try { return Process.GetCurrentProcess().StartTime.ToUniversalTime(); }
        catch { return DateTime.UtcNow; }
    }

    private static string MarkerPath(string keymap) => keymap + ".rynthcore";

    private static long ReadMarker(string keymap)
    {
        try
        {
            string m = MarkerPath(keymap);
            return File.Exists(m) && long.TryParse(File.ReadAllText(m).Trim(), out long t) ? t : 0;
        }
        catch { return 0; }
    }

    /// <summary>Game thread, after AC's tick: the queued changes, then (once a second) another client's change.</summary>
    public static void Tick()
    {
        if (!CanChange || !LoginLifecycleHooks.HasObservedLoginComplete) return;
        DrainRequests();
        if (_dumpTo is { } dump)
        {
            _dumpTo = null;
            string said = SaveOwn(dump) ? $"AC's keys in memory written to {dump}." : "AC couldn't write its keys there.";
            RynthLog.Compat("AcKeymapLive: " + said);
            AcMainThreadQueue.EnqueueWriteToChat("[RynthCore] " + said, 1);
        }
        long now = Environment.TickCount64;
        if (now < _nextCheckMs) return;
        _nextCheckMs = now + 1000;
        try
        {
            string file = AcKeymap.FilePath;
            if (file.Length == 0) return;
            DateTime t = File.GetLastWriteTimeUtc(file);
            bool first = !string.Equals(file, _seenFile, StringComparison.OrdinalIgnoreCase);
            if (!first && t == _seenUtc) return;
            _seenFile = file;
            _seenUtc = t;
            // RynthCore's change (the time noted beside it); at the first look, only one made after this client loaded the file.
            if (ReadMarker(file) != t.Ticks) return;
            if (first && t <= ProcessStartUtc.AddSeconds(5)) return;
            Reload(file, "another client changed a key");
        }
        catch (Exception ex)
        {
            RynthLog.Compat($"AcKeymapLive: watching the keymap failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    /// <summary>Loads the file into AC as AC does when it starts. Game thread.</summary>
    private static bool Reload(string file, string why)
    {
        IntPtr im = InputManager();
        if (im == IntPtr.Zero) return false;
        IntPtr path = AcString(file);
        if (path == IntPtr.Zero)
        {
            RynthLog.Compat($"AcKeymapLive: AC can't open '{file}' (its name has letters AC's file functions don't take).");
            return false;
        }
        IntPtr vt = Marshal.ReadIntPtr(im);
        var init = (delegate* unmanaged[Thiscall]<IntPtr, byte>)Marshal.ReadIntPtr(vt, VtInitialize);
        var load = (delegate* unmanaged[Thiscall]<IntPtr, IntPtr, byte>)Marshal.ReadIntPtr(vt, VtLoadFile);
        var db = (delegate* unmanaged[Thiscall]<IntPtr, uint, byte>)Marshal.ReadIntPtr(vt, VtLoadDb);
        init(im);
        bool loaded = load(im, path) != 0;
        if (!loaded) init(im);   // as gmClient::InitKeymap: a file that didn't load leaves the game's own keys
        db(im, FixedMapA);
        db(im, FixedMapB);
        AcKeymap.Invalidate();
        RynthLog.Compat($"AcKeymapLive: keys loaded again from {Path.GetFileName(file)} ({why}){(loaded ? "" : " - the file didn't load: the game's default keys")}.");
        return loaded;
    }

    /// <summary>This client's keys to the file (AC's own writer). Game thread.</summary>
    private static bool SaveOwn(string file)
    {
        IntPtr im = InputManager();
        if (im == IntPtr.Zero) return false;
        IntPtr path = AcString(file);
        if (path == IntPtr.Zero) return false;
        var save = (delegate* unmanaged[Thiscall]<IntPtr, IntPtr, byte>)Marshal.ReadIntPtr(Marshal.ReadIntPtr(im), VtSave);
        return save(im, path) != 0;
    }

    // ── Changing (queued from any thread, done on the game thread) ───────────

    public enum Op { Bind = 1, Unbind = 2 }

    private readonly record struct Request(string Action, string Key, Op Op, long QueuedMs);

    private static readonly ConcurrentQueue<Request> Requests = new();

    /// <summary>The engine hotkeys' commands look like this: "/rdef use 2".</summary>
    public static bool IsCommand(string action) => action.StartsWith('/');

    private static bool ValidAction(string action) =>
        action.Length is > 0 and <= 64 && (IsCommand(action) || System.Text.RegularExpressions.Regex.IsMatch(action, "^[A-Za-z0-9_]+$"));

    /// <summary>
    /// What <paramref name="keyText"/> ("Ctrl+F5") would take from others if bound to <paramref name="action"/>
    /// (a keymap action "UseQuickSlot_5", or an engine command "/rdef use 2"). 0 free, 1 taken (the message
    /// says from what), -1 can't (the message says why). Any thread.
    /// </summary>
    public static int Check(string action, string keyText, out string message)
    {
        message = string.Empty;
        action = action.Trim();
        if (!ValidAction(action)) { message = "Not an action."; return -1; }
        if (!HotkeyKeys.TryParse(keyText, out KeyCombo combo, out string err)) { message = err; return -1; }
        if (!IsCommand(action) && !CanChange) { message = "This client can't change AC's keys (" + _status + ")"; return -1; }

        var takes = new List<string>();
        string file = AcKeymap.FilePath;
        string? dik = AcKeymapText.DikForVk(combo.Vk);
        if (!IsCommand(action) && dik == null) { message = $"AC's keymap can't hold {combo}."; return -1; }
        if (file.Length > 0 && dik != null)
        {
            try
            {
                List<string> lines = AcKeymapText.Split(File.ReadAllText(file), out _);
                if (AcKeymapText.TryMeta(lines, combo.Mods, out uint meta, out string metaErr))
                {
                    foreach (AcKeymapText.Line l in AcKeymapText.Uses(lines, 0, dik, meta))
                        if (!l.Action.Equals(action, StringComparison.OrdinalIgnoreCase)) takes.Add(AcKeymapText.Describe(l.Action) + " (the game)");
                }
                else if (!IsCommand(action)) { message = metaErr; return -1; }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                message = "The keymap can't be read right now; try again.";
                return -1;
            }
        }
        foreach (HotkeyBinding b in HotkeyService.Current.Bindings)
            if (b.Combo == combo && !(b.IsCommand && SameCommand(b.Command, action)))
                takes.Add(HotkeyActions.Describe(b) + " (a RynthCore hotkey)");
        if (takes.Count == 0) return 0;
        message = $"{combo} is " + string.Join(", ", takes) + ".";
        return 1;
    }

    /// <summary>Queues binding or unbinding the key; done on the game thread. False (with why) when it can't be.</summary>
    public static bool Enqueue(string action, string keyText, Op op, out string message)
    {
        message = string.Empty;
        action = action.Trim();
        keyText = keyText.Trim();
        if (!ValidAction(action)) { message = "Not an action."; return false; }
        if (keyText.Length == 0 || keyText.Length > 40) { message = "No key."; return false; }
        if (op == Op.Bind && !HotkeyKeys.TryParse(keyText, out _, out string err)) { message = err; return false; }
        if (!CanChange && (!IsCommand(action) || op == Op.Bind)) { message = "This client can't change AC's keys (" + _status + ")"; return false; }
        if (Requests.Count >= 16) { message = "Too many changes at once."; return false; }
        Requests.Enqueue(new Request(action, keyText, op, Environment.TickCount64));
        return true;
    }

    private static volatile string? _dumpTo;

    /// <summary>/rc keymap dump: AC's keys as it holds them in memory, written by AC to <paramref name="path"/> (game thread, next tick).</summary>
    public static void RequestDump(string path) => _dumpTo = path;

    /// <summary>Logout: nothing queued for the old character.</summary>
    public static void Reset()
    {
        while (Requests.TryDequeue(out _)) { }
    }

    private static void DrainRequests()
    {
        while (Requests.TryDequeue(out Request r))
        {
            if (Environment.TickCount64 - r.QueuedMs > 10000) continue;
            string result;
            try { result = Apply(r); }
            catch (Exception ex) { result = $"Changing the key failed: {ex.GetType().Name}: {ex.Message}"; }
            RynthLog.Compat("AcKeymapLive: " + result);
            if (result.Length > 0) AcMainThreadQueue.EnqueueWriteToChat("[RynthCore] " + result, 1);
        }
    }

    private static string Apply(Request r)
    {
        string file = AcKeymap.FilePath;
        bool command = IsCommand(r.Action);

        if (r.Op == Op.Unbind)
        {
            if (command)
            {
                int n = RemoveHotkeys(b => b.IsCommand && SameCommand(b.Command, r.Action) && KeyMatches(b.Combo, r.Key));
                return n > 0 ? $"{r.Key} no longer does {r.Action}." : $"{r.Action} has no key {r.Key}.";
            }
            if (file.Length == 0) return "There is no AC keymap file to change.";
            Backup(file);
            SaveOwn(file);
            List<string> ul = AcKeymapText.Split(File.ReadAllText(file), out string unl);
            if (!UnbindAc(ul, r.Action, r.Key)) return $"{AcKeymapText.Describe(r.Action)} has no key {r.Key}.";
            WriteAndLoad(file, ul, unl);
            return $"{r.Key} no longer does {AcKeymapText.Describe(r.Action)}.";
        }

        HotkeyKeys.TryParse(r.Key, out KeyCombo combo, out _);
        string? dik = AcKeymapText.DikForVk(combo.Vk);
        if (!command && dik == null) return $"AC's keymap can't hold {combo}.";
        if (!command && file.Length == 0) return "There is no AC keymap file to change.";

        // AC's side: the key comes off AC's other actions (a command's hotkey would take it before AC anyway).
        var from = new List<string>();
        if (file.Length > 0 && dik != null)
        {
            List<string> lines = AcKeymapText.Split(File.ReadAllText(file), out string nl);
            bool metaOk = AcKeymapText.TryMeta(lines, combo.Mods, out uint meta, out string metaErr);
            if (!command && !metaOk) return metaErr;
            if (metaOk && (!command || AcKeymapText.Uses(lines, 0, dik, meta).Count > 0))
            {
                // This client's keys first (a key changed in Configure Keyboard lives only in memory), then the edit.
                Backup(file);
                if (SaveOwn(file)) lines = AcKeymapText.Split(File.ReadAllText(file), out nl);
                else RynthLog.Compat("AcKeymapLive: couldn't save this client's keys first; changing the file as it is.");
                List<AcKeymapText.Line> taken;
                if (command)
                {
                    taken = AcKeymapText.Uses(lines, 0, dik, meta);
                    for (int k = taken.Count - 1; k >= 0; k--) lines.RemoveAt(taken[k].Index);
                }
                else if (!AcKeymapText.Bind(lines, r.Action, 0, dik, meta, out taken, out string bindErr)) return bindErr;
                foreach (AcKeymapText.Line l in taken) from.Add(AcKeymapText.Describe(l.Action));
                WriteAndLoad(file, lines, nl);
            }
        }

        // RynthCore's hotkeys: none other keeps the key; a command gets its binding.
        int hk = RemoveHotkeys(b => b.Combo == combo && !(command && b.IsCommand && SameCommand(b.Command, r.Action)));
        if (hk > 0) from.Add(hk == 1 ? "a RynthCore hotkey" : $"{hk} RynthCore hotkeys");
        if (command && !HotkeyService.Current.Bindings.Exists(b => b.Combo == combo && b.IsCommand && SameCommand(b.Command, r.Action)))
            HotkeyService.Add(new HotkeyBinding(combo, HotkeyActions.Command, r.Action));
        string what = command ? r.Action : AcKeymapText.Describe(r.Action);
        return $"{combo} now does {what}" + (from.Count > 0 ? " (taken from " + string.Join(", ", from) + ")." : ".");
    }

    /// <summary>The edited file written (backed up the first time), its time noted, and loaded here; the others follow within a second.</summary>
    private static void WriteAndLoad(string file, List<string> lines, string newline)
    {
        Backup(file);
        string tmp = file + ".tmp";
        File.WriteAllText(tmp, AcKeymapText.Join(lines, newline), Encoding.ASCII);
        File.Move(tmp, file, overwrite: true);
        DateTime t = File.GetLastWriteTimeUtc(file);
        File.WriteAllText(MarkerPath(file), t.Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        _seenFile = file;
        _seenUtc = t;
        Reload(file, "a key changed here");
    }

    /// <summary>Removes the action's key: by combo ("Ctrl+1") or by the text GetActionKeys showed ("Mouse4").</summary>
    private static bool UnbindAc(List<string> lines, string action, string keyText)
    {
        if (HotkeyKeys.TryParse(keyText, out KeyCombo combo, out _) && AcKeymapText.DikForVk(combo.Vk) is { } dik
            && AcKeymapText.TryMeta(lines, combo.Mods, out uint meta, out _)
            && AcKeymapText.Unbind(lines, action, 0, dik, meta))
            return true;
        foreach (AcKeymapText.Line l in AcKeymapText.Bindings(lines))
        {
            if (!l.Action.Equals(action, StringComparison.OrdinalIgnoreCase)) continue;
            if (AcKeymap.Describe(new AcKeymap.Binding(l.Device, l.Dik, l.Meta)).Equals(keyText, StringComparison.OrdinalIgnoreCase))
                return AcKeymapText.Unbind(lines, action, l.Device, l.Dik, l.Meta);
        }
        return false;
    }

    private static void Backup(string file)
    {
        try
        {
            string b = file + ".before-rynthcore";
            if (!File.Exists(b)) File.Copy(file, b);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            RynthLog.Compat($"AcKeymapLive: couldn't back up {file}: {ex.Message}");
        }
    }

    private static int RemoveHotkeys(Predicate<HotkeyBinding> match)
    {
        var list = new List<HotkeyBinding>(HotkeyService.Current.Bindings);
        int n = list.RemoveAll(match);
        if (n > 0) HotkeyService.SetAll(list);
        return n;
    }

    private static bool SameCommand(string a, string b) => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool KeyMatches(KeyCombo combo, string keyText) =>
        HotkeyKeys.TryParse(keyText, out KeyCombo k, out _) && k == combo;

    // ── Keys of an engine command ───────────────────────────────────────────

    /// <summary>The engine hotkeys running <paramref name="command"/>, "F9, Ctrl+F9" (switched-off ones left out).</summary>
    public static string CommandKeysText(string command)
    {
        var sb = new StringBuilder();
        foreach (HotkeyBinding b in HotkeyService.Current.Bindings)
        {
            if (!b.Enabled || !b.IsCommand || !SameCommand(b.Command, command)) continue;
            if (sb.Length > 0) sb.Append(", ");
            sb.Append(HotkeyKeys.Format(b.Combo));
        }
        return sb.ToString();
    }
}
