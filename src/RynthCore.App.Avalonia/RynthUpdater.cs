using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RynthCore.App.Avalonia;

/// <summary>
/// Signed updates for RynthCore and the RynthSuite plugins.
///
/// The feed is a signed envelope — {"payload": base64(manifest JSON), "signatures": [{"key": id,
/// "sig": base64}]}, ECDSA P-256 / SHA-256 over the payload bytes (IEEE P1363). Nothing in it is
/// used until a signature from one of <see cref="UpdateTrust.PublicKeys"/> verifies. The signing
/// key lives on the publisher's PC, never on the server, so a compromised aelrynth.com cannot push
/// code to players. Every download is then checked against the signed size and SHA-256.
///
/// Plugins update in place. The engine runs plugins from shadow copies, so swapping the DLL never
/// touches a running bot: the new version loads the next time AC starts, or on RL if the player
/// chooses — the updater never reloads anything. The core (engine, Loader, launcher) updates by
/// running the release's installer, and only while no RynthCore client is running.
/// </summary>
internal sealed class RynthUpdater
{
    public const string DefaultFeedUrl = "https://aelrynth.com/downloads/rynth/update.json";

    private const long MaxFeedBytes = 256 * 1024;
    private const long MaxPluginBytes = 64L << 20;
    private const long MaxInstallerBytes = 256L << 20;

    public sealed record PluginEntry(string Name, string File, string Url, long Size, string Sha256);
    public sealed record InstallerEntry(int Release, string Version, string Url, long Size, string Sha256);
    public sealed record Manifest(int Release, string Version, DateTime PublishedUtc, string Notes,
                                  InstallerEntry Core, IReadOnlyList<PluginEntry> Plugins);

    /// <summary>One installed copy of a plugin the feed knows.</summary>
    public sealed record PluginStatus(PluginEntry Entry, string Path, PluginState State);
    public enum PluginState { UpToDate, NeedsUpdate, LocalBuild }

    public sealed record CheckResult(Manifest Manifest, int InstalledCoreRelease, IReadOnlyList<PluginStatus> Plugins)
    {
        public bool CoreUpdateAvailable => Manifest.Core.Release > InstalledCoreRelease;
        public IEnumerable<PluginStatus> PluginsToUpdate => Plugins.Where(p => p.State == PluginState.NeedsUpdate);
    }

    private static readonly HttpClient Http = CreateHttp();

    private readonly string _feedUrl;
    private readonly string _allowedPrefix;
    private readonly IReadOnlyList<string> _trustedKeys;
    private readonly string _stateDir;
    private readonly string _appDir;

    public RynthUpdater(string feedUrl = DefaultFeedUrl, IReadOnlyList<string>? trustedKeys = null,
                        string? stateDir = null, string? appDir = null)
    {
        _feedUrl = feedUrl;
        // Downloads must come from the feed's own folder — the signature already covers the URLs,
        // this just refuses to follow a signed manifest anywhere else.
        _allowedPrefix = feedUrl[..(feedUrl.LastIndexOf('/') + 1)];
        _trustedKeys = trustedKeys ?? UpdateTrust.PublicKeys;
        _stateDir = stateDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RynthCore");
        _appDir = appDir ?? AppContext.BaseDirectory;
    }

    /// <summary>False until the signing keys exist: with no trusted key nothing can verify.</summary>
    public bool IsConfigured => _trustedKeys.Count > 0;

    /// <summary>The core release this install came from (release.txt, written by the installer); 0 when unknown.</summary>
    public int InstalledCoreRelease
    {
        get
        {
            try
            {
                string path = Path.Combine(_appDir, "release.txt");
                if (!File.Exists(path)) return 0;
                string first = File.ReadLines(path).FirstOrDefault() ?? "";
                return int.TryParse(first.Trim(), out int r) ? r : 0;
            }
            catch { return 0; }
        }
    }

    // ── Check ─────────────────────────────────────────────────────────────────

    public async Task<CheckResult> CheckAsync(IEnumerable<string> pluginPaths, CancellationToken ct = default)
    {
        if (!IsConfigured)
            throw new InvalidOperationException("updates are not set up in this build (no signing key)");

        byte[] envelope = await DownloadBytesAsync(_feedUrl, MaxFeedBytes, ct);
        byte[] payload = VerifyEnvelope(envelope, _trustedKeys);
        Manifest manifest = ParseManifest(payload);

        // Replay guard: a signed but older feed (an attacker re-serving last month's) is refused.
        int floor = Math.Max(ReadHighestRelease(), InstalledCoreRelease);
        if (manifest.Release < floor)
            throw new InvalidDataException($"the feed is older ({manifest.Release}) than one already seen ({floor})");
        if (manifest.Release > ReadHighestRelease())
            WriteHighestRelease(manifest.Release);

        var statuses = new List<PluginStatus>();
        var byFile = manifest.Plugins.ToDictionary(p => p.File, StringComparer.OrdinalIgnoreCase);
        foreach (string path in pluginPaths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!byFile.TryGetValue(Path.GetFileName(path), out PluginEntry? entry) || !File.Exists(path))
                continue;   // not a plugin this feed ships (a private or third-party one): never touched

            PluginState state;
            if (string.Equals(Sha256File(path), entry.Sha256, StringComparison.OrdinalIgnoreCase))
                state = PluginState.UpToDate;
            else if (File.GetLastWriteTimeUtc(path) > manifest.PublishedUtc.AddMinutes(1))
                state = PluginState.LocalBuild;   // newer than the release: someone's own build, leave it
            else
                state = PluginState.NeedsUpdate;
            statuses.Add(new PluginStatus(entry, path, state));
        }

        return new CheckResult(manifest, InstalledCoreRelease, statuses);
    }

    // ── Plugins ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Swaps each outdated plugin DLL for the verified release build. The previous file is kept
    /// beside it as .previous. Running clients keep the version they started with.
    /// </summary>
    public async Task<List<string>> UpdatePluginsAsync(CheckResult check, CancellationToken ct = default)
    {
        var done = new List<string>();
        foreach (var group in check.PluginsToUpdate.GroupBy(p => p.Entry))
        {
            PluginEntry e = group.Key;
            string temp = Path.Combine(Path.GetTempPath(), $"rynth-{Guid.NewGuid():N}.dll");
            try
            {
                await DownloadVerifiedAsync(e.Url, e.Size, e.Sha256, MaxPluginBytes, temp, ct);
                foreach (PluginStatus p in group)
                {
                    string staged = p.Path + ".new";
                    File.Copy(temp, staged, overwrite: true);
                    // Stamp the release time so a later check never mistakes it for a local build.
                    File.SetLastWriteTimeUtc(staged, check.Manifest.PublishedUtc);
                    File.Copy(p.Path, p.Path + ".previous", overwrite: true);
                    File.Move(staged, p.Path, overwrite: true);
                    done.Add($"{e.Name} → {check.Manifest.Version}");
                }
            }
            finally
            {
                try { File.Delete(temp); } catch { }
            }
        }
        return done;
    }

    // ── Core ──────────────────────────────────────────────────────────────────

    /// <summary>Downloads (or reuses) the release installer, verified, and returns its path.</summary>
    public async Task<string> DownloadInstallerAsync(CheckResult check, CancellationToken ct = default)
    {
        InstallerEntry core = check.Manifest.Core;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                  "RynthCore", "updates", core.Release.ToString());
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, "RynthCore-Setup.exe");
        if (File.Exists(path) && new FileInfo(path).Length == core.Size &&
            string.Equals(Sha256File(path), core.Sha256, StringComparison.OrdinalIgnoreCase))
            return path;

        string temp = path + ".part";
        await DownloadVerifiedAsync(core.Url, core.Size, core.Sha256, MaxInstallerBytes, temp, ct);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    /// <summary>Runs the installer silently; it closes the launcher, upgrades in place and relaunches it.</summary>
    public static void RunInstaller(string installerPath)
    {
        // DownloadInstallerAsync verified it moments ago, into a per-user folder.
        Process.Start(new ProcessStartInfo(installerPath,
            "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RELAUNCH") { UseShellExecute = true });
    }

    // ── Envelope + manifest ───────────────────────────────────────────────────

    /// <summary>Returns the payload bytes if any trusted key signed them; throws otherwise.</summary>
    public static byte[] VerifyEnvelope(byte[] envelope, IReadOnlyList<string> trustedKeys)
    {
        using var doc = JsonDocument.Parse(envelope);
        byte[] payload = Convert.FromBase64String(doc.RootElement.GetProperty("payload").GetString() ?? "");
        var sigs = doc.RootElement.GetProperty("signatures");

        foreach (string key in trustedKeys)
        {
            byte[] spki = Convert.FromBase64String(key);
            string id = KeyId(spki);
            using var ec = ECDsa.Create();
            ec.ImportSubjectPublicKeyInfo(spki, out _);
            foreach (JsonElement s in sigs.EnumerateArray())
            {
                if (!string.Equals(s.GetProperty("key").GetString(), id, StringComparison.OrdinalIgnoreCase))
                    continue;
                byte[] sig = Convert.FromBase64String(s.GetProperty("sig").GetString() ?? "");
                if (ec.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation))
                    return payload;
            }
        }
        throw new InvalidDataException("the update feed's signature is not valid");
    }

    /// <summary>First 8 bytes of SHA-256 over the public key (SubjectPublicKeyInfo), as hex.</summary>
    public static string KeyId(byte[] spki) => Convert.ToHexString(SHA256.HashData(spki).AsSpan(0, 8)).ToLowerInvariant();

    private Manifest ParseManifest(byte[] payload)
    {
        using var doc = JsonDocument.Parse(payload);
        JsonElement r = doc.RootElement;
        if (r.GetProperty("schema").GetInt32() != 1)
            throw new InvalidDataException("unknown update feed format — update RynthCore by hand");

        JsonElement c = r.GetProperty("core");
        var core = new InstallerEntry(c.GetProperty("release").GetInt32(), c.GetProperty("version").GetString() ?? "",
            CheckUrl(c.GetProperty("url").GetString()), c.GetProperty("size").GetInt64(), c.GetProperty("sha256").GetString() ?? "");

        var plugins = new List<PluginEntry>();
        foreach (JsonElement p in r.GetProperty("plugins").EnumerateArray())
        {
            string file = p.GetProperty("file").GetString() ?? "";
            if (file.Length == 0 || file != Path.GetFileName(file) || !file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"bad plugin file name '{file}'");
            plugins.Add(new PluginEntry(p.GetProperty("name").GetString() ?? file, file,
                CheckUrl(p.GetProperty("url").GetString()), p.GetProperty("size").GetInt64(), p.GetProperty("sha256").GetString() ?? ""));
        }

        return new Manifest(r.GetProperty("release").GetInt32(), r.GetProperty("version").GetString() ?? "",
            r.GetProperty("published").GetDateTime().ToUniversalTime(),
            r.TryGetProperty("notes", out JsonElement n) ? n.GetString() ?? "" : "", core, plugins);
    }

    private string CheckUrl(string? url)
    {
        if (url == null || !url.StartsWith(_allowedPrefix, StringComparison.Ordinal) || url.Contains(".."))
            throw new InvalidDataException($"download outside the update folder: '{url}'");
        return url;
    }

    // ── Downloads ─────────────────────────────────────────────────────────────

    private static async Task<byte[]> DownloadBytesAsync(string url, long max, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        await using var s = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        byte[] buf = new byte[16384];
        int n;
        while ((n = await s.ReadAsync(buf, ct)) > 0)
        {
            if (ms.Length + n > max) throw new InvalidDataException($"{url} is larger than expected");
            ms.Write(buf, 0, n);
        }
        return ms.ToArray();
    }

    private static async Task DownloadVerifiedAsync(string url, long size, string sha256, long max, string dest, CancellationToken ct)
    {
        if (size <= 0 || size > max) throw new InvalidDataException($"{url}: size {size} out of range");
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        await using (var s = await resp.Content.ReadAsStreamAsync(ct))
        await using (var f = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            byte[] buf = new byte[81920];
            int n;
            while ((n = await s.ReadAsync(buf, ct)) > 0)
            {
                total += n;
                if (total > size) throw new InvalidDataException($"{url} is larger than the signed size");
                hash.AppendData(buf, 0, n);
                await f.WriteAsync(buf.AsMemory(0, n), ct);
            }
        }
        string got = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (total != size || !string.Equals(got, sha256, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(dest); } catch { }
            throw new InvalidDataException($"{Path.GetFileName(url)} does not match the signed hash — not installed");
        }
    }

    public static string Sha256File(string path)
    {
        using var f = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(f)).ToLowerInvariant();
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("RynthCore-Launcher");
        return http;
    }

    // ── State ─────────────────────────────────────────────────────────────────

    private string StatePath => Path.Combine(_stateDir, "update-state.json");

    private int ReadHighestRelease()
    {
        try
        {
            if (!File.Exists(StatePath)) return 0;
            using var doc = JsonDocument.Parse(File.ReadAllText(StatePath));
            return doc.RootElement.TryGetProperty("highestRelease", out JsonElement v) ? v.GetInt32() : 0;
        }
        catch { return 0; }
    }

    private void WriteHighestRelease(int release)
    {
        try
        {
            Directory.CreateDirectory(_stateDir);
            string tmp = StatePath + ".tmp";
            File.WriteAllText(tmp, $"{{\"highestRelease\": {release}}}", Encoding.UTF8);
            File.Move(tmp, StatePath, overwrite: true);
        }
        catch { }
    }
}
