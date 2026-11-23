using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RynthCore.App;

internal sealed class AcClientLaunchSettingsService
{
    private const string UserPreferencesFileName = "UserPreferences.ini";
    private const string NetSectionName = "Net";
    private const string ComputeUniquePortKey = "ComputeUniquePort";
    private const string IntroVideoFileName = "turbine_logo_ac.avi";
    private const string DisabledIntroVideoSuffix = ".rynthcore-disabled";

    public void Apply(
        string acClientPath,
        bool allowMultipleClients,
        bool skipIntroVideos,
        bool enableDxvk = false,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(acClientPath) || !File.Exists(acClientPath))
            return;

        string gameDirectory = Path.GetDirectoryName(acClientPath) ?? string.Empty;
        UpdateUserPreferences(allowMultipleClients, log, acClientPath);
        UpdateIntroVideoState(gameDirectory, skipIntroVideos, log);
        // Drop-in DXVK d3d9.dll beside acclient (or remove a Rynth-managed copy).
        DxvkPackageService.ApplyToClient(acClientPath, enableDxvk, log);
    }

    /// <summary>
    /// The UserPreferences.ini this AC install reads and writes: the one in the game folder
    /// (beside acclient.exe) when it exists - an install's clients use it, and the Documents copy
    /// goes stale (2026-10-17: the game folder's file changed every session, Documents' hadn't
    /// since April) - else My Documents\Asheron's Call\UserPreferences.ini.
    /// </summary>
    public static string LivePrefsPath(string? acClientPath)
    {
        string? gameDirectory = string.IsNullOrWhiteSpace(acClientPath) ? null : Path.GetDirectoryName(acClientPath);
        if (!string.IsNullOrWhiteSpace(gameDirectory))
        {
            string gamePrefs = Path.Combine(gameDirectory, UserPreferencesFileName);
            if (File.Exists(gamePrefs))
                return gamePrefs;
        }
        return DocumentsPrefsPath;
    }

    /// <summary>Every UserPreferences.ini AC may read that exists (the live one when neither does).</summary>
    public static List<string> AllPrefsPaths(string? acClientPath)
    {
        var paths = new List<string>();
        string live = LivePrefsPath(acClientPath);
        paths.Add(live);
        if (!string.Equals(live, DocumentsPrefsPath, StringComparison.OrdinalIgnoreCase) && File.Exists(DocumentsPrefsPath))
            paths.Add(DocumentsPrefsPath);
        return paths;
    }

    private static string DocumentsPrefsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Asheron's Call", UserPreferencesFileName);

    /// <summary>
    /// Copies a per-account UserPreferences.ini stash over the live file (<see cref="LivePrefsPath"/>:
    /// the game folder's when it has one, else My Documents\Asheron's Call's), then re-applies the
    /// ComputeUniquePort flag.
    /// If <paramref name="stashPath"/> is empty or missing, no swap is performed
    /// and the live file is left as-is.
    /// Returns what was swapped (null when nothing was), so a client that quits at
    /// startup can be traced back to the stash and the swap undone
    /// (<see cref="UserPrefsSwapRecovery"/>).
    /// </summary>
    public UserPrefsSwap? SwapUserPreferences(string? stashPath, bool allowMultipleClients, Action<string>? log = null,
        string? acClientPath = null)
    {
        if (string.IsNullOrWhiteSpace(stashPath))
            return null;

        if (!File.Exists(stashPath))
        {
            log?.Invoke($"User prefs swap skipped: stash file not found at {stashPath}.");
            return null;
        }

        string prefsPath = LivePrefsPath(acClientPath);
        string prefsDirectory = Path.GetDirectoryName(prefsPath) ?? string.Empty;

        byte[] stashBytes;
        byte[]? previousLiveBytes;
        try
        {
            Directory.CreateDirectory(prefsDirectory);
            stashBytes = File.ReadAllBytes(stashPath);
            previousLiveBytes = File.Exists(prefsPath) ? File.ReadAllBytes(prefsPath) : null;
            File.Copy(stashPath, prefsPath, overwrite: true);
        }
        catch (Exception ex)
        {
            log?.Invoke($"User prefs swap failed copying {stashPath}: {ex.Message}");
            return null;
        }

        // Re-apply the ComputeUniquePort flag — the stash may have it set to
        // the wrong value (e.g. user set it up before enabling multi-client).
        UpdateUserPreferences(allowMultipleClients, log: null, acClientPath);
        log?.Invoke($"User prefs swap: applied stash {Path.GetFileName(stashPath)} to {prefsPath}.");
        return new UserPrefsSwap(stashPath, prefsPath, stashBytes, previousLiveBytes);
    }

    /// <summary>Sets ComputeUniquePort in every UserPreferences.ini AC may read (<see cref="AllPrefsPaths"/>).</summary>
    private static void UpdateUserPreferences(bool allowMultipleClients, Action<string>? log, string? acClientPath)
    {
        foreach (string prefsPath in AllPrefsPaths(acClientPath))
        {
            string? prefsDirectory = Path.GetDirectoryName(prefsPath);
            if (!string.IsNullOrEmpty(prefsDirectory))
                Directory.CreateDirectory(prefsDirectory);

            List<string> lines = File.Exists(prefsPath)
                ? File.ReadAllLines(prefsPath).ToList()
                : [];

            bool changed = SetIniValue(lines, NetSectionName, ComputeUniquePortKey, allowMultipleClients ? "True" : "False");
            if (!changed)
                continue;

            File.WriteAllLines(prefsPath, lines);
            log?.Invoke($"Launch setting applied: ComputeUniquePort={(allowMultipleClients ? "True" : "False")} in {prefsPath}.");
        }
    }

    private static void UpdateIntroVideoState(string gameDirectory, bool skipIntroVideos, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(gameDirectory) || !Directory.Exists(gameDirectory))
            return;

        string introVideoPath = Path.Combine(gameDirectory, IntroVideoFileName);
        string parkedIntroVideoPath = introVideoPath + DisabledIntroVideoSuffix;

        if (skipIntroVideos)
        {
            if (File.Exists(parkedIntroVideoPath))
                return;

            if (!File.Exists(introVideoPath))
                return;

            File.Move(introVideoPath, parkedIntroVideoPath);
            log?.Invoke("Launch setting applied: intro video parked so AC opens without the turbine logo movie.");
            return;
        }

        if (!File.Exists(parkedIntroVideoPath) || File.Exists(introVideoPath))
            return;

        File.Move(parkedIntroVideoPath, introVideoPath);
        log?.Invoke("Launch setting applied: intro video restored.");
    }

    private static bool SetIniValue(List<string> lines, string sectionName, string key, string value)
    {
        string sectionHeader = $"[{sectionName}]";
        int sectionIndex = lines.FindIndex(line => string.Equals(line.Trim(), sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            if (lines.Count > 0 && !string.IsNullOrWhiteSpace(lines[^1]))
                lines.Add(string.Empty);

            lines.Add(sectionHeader);
            lines.Add($"{key}={value}");
            return true;
        }

        int nextSectionIndex = sectionIndex + 1;
        while (nextSectionIndex < lines.Count && !IsSectionHeader(lines[nextSectionIndex]))
            nextSectionIndex++;

        for (int i = sectionIndex + 1; i < nextSectionIndex; i++)
        {
            string line = lines[i].Trim();
            if (!line.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase))
                continue;

            string updatedLine = $"{key}={value}";
            if (string.Equals(lines[i], updatedLine, StringComparison.Ordinal))
                return false;

            lines[i] = updatedLine;
            return true;
        }

        lines.Insert(nextSectionIndex, $"{key}={value}");
        return true;
    }

    private static bool IsSectionHeader(string line)
    {
        string trimmed = line.Trim();
        return trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal);
    }
}
