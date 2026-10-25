//  RynthCore.App — DxvkPackageService.cs
//  Caches an x86 DXVK d3d9.dll under <RynthCore>\Runtime\dxvk\ and copies it
//  into (or removes it from) the AC client folder when the launcher toggle flips.
//  Fetch uses the official GitHub releases API (doitsujin/dxvk); the installer
//  can pre-seed the same cache folder so first enable works offline.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RynthCore.Install;

namespace RynthCore.App;

internal static class DxvkPackageService
{
    public const string DllFileName = "d3d9.dll";
    public const string ConfFileName = "dxvk.conf";
    public const string MarkerFileName = "d3d9.dll.rynthcore-dxvk";
    public const string PreexistingSuffix = ".rynthcore-preexisting";
    public const string VersionFileName = "version.txt";
    public const string DefaultDxvkConf = """
        # Managed by RynthCore (launcher Enable DXVK). Safe to delete with the toggle off.
        dxgi.deferSurfaceCreation = True
        d3d9.maxFrameLatency = 1
        d3d9.presentInterval = 1
        """;

    private static readonly HttpClient Http = CreateHttp();

    private static HttpClient CreateHttp()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RynthCore-Launcher-DxvkFetch/1.0");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Cache folder: &lt;CoreDir&gt;\Runtime\dxvk (d3d9.dll + version.txt).</summary>
    public static string CacheDirectory => Path.Combine(RynthInstallPaths.CoreDir, "Runtime", "dxvk");

    public static string CachedDllPath => Path.Combine(CacheDirectory, DllFileName);

    public static string? CachedVersion
    {
        get
        {
            string path = Path.Combine(CacheDirectory, VersionFileName);
            if (!File.Exists(path))
                return null;
            try { return File.ReadAllText(path).Trim(); }
            catch { return null; }
        }
    }

    public static bool IsCached => File.Exists(CachedDllPath);

    /// <summary>
    /// Copy DXVK into the AC game folder, or remove a Rynth-managed copy.
    /// Never deletes a non-DXVK d3d9 that we did not install (parks as preexisting first).
    /// </summary>
    public static void ApplyToClient(string acClientPath, bool enable, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(acClientPath) || !File.Exists(acClientPath))
        {
            log?.Invoke("DXVK: skipped — AC client path missing.");
            return;
        }

        string gameDir = Path.GetDirectoryName(acClientPath) ?? "";
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
            return;

        string dllPath = Path.Combine(gameDir, DllFileName);
        string confPath = Path.Combine(gameDir, ConfFileName);
        string markerPath = Path.Combine(gameDir, MarkerFileName);
        string preexistingPath = dllPath + PreexistingSuffix;

        if (!enable)
        {
            RemoveManaged(gameDir, dllPath, confPath, markerPath, preexistingPath, log);
            return;
        }

        if (!IsCached)
        {
            log?.Invoke("DXVK: cache empty — use \"Fetch DXVK\" (or reinstall) before enabling, or wait for auto-fetch.");
            return;
        }

        try
        {
            // Park a non-Rynth d3d9 so disable can restore it.
            if (File.Exists(dllPath) && !File.Exists(markerPath) && !LooksLikeDxvk(dllPath))
            {
                if (File.Exists(preexistingPath))
                    File.Delete(preexistingPath);
                File.Move(dllPath, preexistingPath);
                log?.Invoke($"DXVK: parked existing {DllFileName} as {Path.GetFileName(preexistingPath)}.");
            }

            File.Copy(CachedDllPath, dllPath, overwrite: true);
            File.WriteAllText(markerPath, CachedVersion ?? "unknown");
            if (!File.Exists(confPath))
                File.WriteAllText(confPath, DefaultDxvkConf);

            log?.Invoke($"DXVK: installed {CachedVersion ?? "cached"} into {gameDir}.");
        }
        catch (Exception ex)
        {
            log?.Invoke($"DXVK: failed to install into client folder: {ex.Message}");
        }
    }

    private static void RemoveManaged(
        string gameDir,
        string dllPath,
        string confPath,
        string markerPath,
        string preexistingPath,
        Action<string>? log)
    {
        bool managed = File.Exists(markerPath) || (File.Exists(dllPath) && LooksLikeDxvk(dllPath));
        if (!managed && !File.Exists(preexistingPath))
        {
            log?.Invoke("DXVK: already off for this client folder.");
            return;
        }

        try
        {
            if (managed && File.Exists(dllPath))
                File.Delete(dllPath);
            if (File.Exists(markerPath))
                File.Delete(markerPath);
            // Only remove conf if we wrote the marker (ours).
            if (managed && File.Exists(confPath))
            {
                string text = File.ReadAllText(confPath);
                if (text.Contains("Managed by RynthCore", StringComparison.Ordinal))
                    File.Delete(confPath);
            }

            if (File.Exists(preexistingPath))
            {
                File.Move(preexistingPath, dllPath);
                log?.Invoke($"DXVK: removed; restored preexisting {DllFileName} in {gameDir}.");
            }
            else
            {
                log?.Invoke($"DXVK: removed from {gameDir} (stock System32 d3d9 will load).");
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"DXVK: failed to remove from client folder: {ex.Message}");
        }
    }

    public static bool LooksLikeDxvk(string dllPath)
    {
        try
        {
            FileVersionInfo vi = FileVersionInfo.GetVersionInfo(dllPath);
            string blob = $"{vi.ProductName}|{vi.CompanyName}|{vi.FileDescription}|{vi.FileVersion}";
            return blob.Contains("dxvk", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Download the latest DXVK release (x32 d3d9.dll) into the cache.
    /// Returns the version tag (e.g. "3.1.1") or throws.
    /// </summary>
    public static async Task<string> EnsureLatestAsync(
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        progress?.Report("Querying GitHub for the latest DXVK release…");
        using HttpResponseMessage metaResponse = await Http.GetAsync(
            "https://api.github.com/repos/doitsujin/dxvk/releases/latest",
            cancellationToken).ConfigureAwait(false);
        metaResponse.EnsureSuccessStatusCode();
        await using Stream metaStream = await metaResponse.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = await JsonDocument.ParseAsync(metaStream, cancellationToken: cancellationToken).ConfigureAwait(false);

        string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        string version = tag.TrimStart('v');
        if (string.IsNullOrWhiteSpace(version))
            throw new InvalidOperationException("DXVK release JSON had no tag_name.");

        string? tarballUrl = null;
        string? assetName = null;
        foreach (JsonElement asset in doc.RootElement.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            // Official package is dxvk-X.Y.Z.tar.gz (not dxvk-native-*).
            if (name.StartsWith("dxvk-", StringComparison.OrdinalIgnoreCase) &&
                name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("native", StringComparison.OrdinalIgnoreCase))
            {
                tarballUrl = asset.GetProperty("browser_download_url").GetString();
                assetName = name;
                break;
            }
        }

        if (string.IsNullOrWhiteSpace(tarballUrl) || string.IsNullOrWhiteSpace(assetName))
            throw new InvalidOperationException("DXVK release had no dxvk-*.tar.gz asset.");

        if (string.Equals(CachedVersion, version, StringComparison.OrdinalIgnoreCase) && IsCached)
        {
            progress?.Report($"DXVK {version} already cached.");
            return version;
        }

        progress?.Report($"Downloading {assetName}…");
        string stageRoot = Path.Combine(Path.GetTempPath(), "rynthcore-dxvk-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stageRoot);
        string tarballPath = Path.Combine(stageRoot, assetName);
        try
        {
            using (HttpResponseMessage dl = await Http.GetAsync(tarballUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
            {
                dl.EnsureSuccessStatusCode();
                await using FileStream fs = File.Create(tarballPath);
                await dl.Content.CopyToAsync(fs, cancellationToken).ConfigureAwait(false);
            }

            progress?.Report("Extracting x32/d3d9.dll…");
            string extractDir = Path.Combine(stageRoot, "extract");
            Directory.CreateDirectory(extractDir);
            await ExtractTarGzAsync(tarballPath, extractDir, cancellationToken).ConfigureAwait(false);

            string? found = FindX32D3d9(extractDir);
            if (found == null)
                throw new InvalidOperationException("Archive did not contain x32/d3d9.dll.");

            Directory.CreateDirectory(CacheDirectory);
            string dest = CachedDllPath;
            string tempDest = dest + ".partial";
            File.Copy(found, tempDest, overwrite: true);
            if (File.Exists(dest))
                File.Delete(dest);
            File.Move(tempDest, dest);
            File.WriteAllText(Path.Combine(CacheDirectory, VersionFileName), version);
            File.WriteAllText(Path.Combine(CacheDirectory, ConfFileName), DefaultDxvkConf);

            progress?.Report($"DXVK {version} cached at {CacheDirectory}.");
            return version;
        }
        finally
        {
            try { Directory.Delete(stageRoot, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Ensure cache exists: use existing, else fetch latest. Returns version or null on failure.</summary>
    public static async Task<string?> EnsureCachedOrFetchAsync(
        Action<string>? log = null,
        CancellationToken cancellationToken = default)
    {
        if (IsCached)
            return CachedVersion ?? "cached";

        try
        {
            return await EnsureLatestAsync(
                new Progress<string>(m => log?.Invoke(m)),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            log?.Invoke($"DXVK: fetch failed: {ex.Message}");
            return null;
        }
    }

    private static string? FindX32D3d9(string extractRoot)
    {
        foreach (string path in Directory.EnumerateFiles(extractRoot, DllFileName, SearchOption.AllDirectories))
        {
            string? parent = Path.GetFileName(Path.GetDirectoryName(path));
            if (string.Equals(parent, "x32", StringComparison.OrdinalIgnoreCase))
                return path;
        }
        return null;
    }

    private static async Task ExtractTarGzAsync(string tarballPath, string destinationDir, CancellationToken cancellationToken)
    {
        await using FileStream compressed = File.OpenRead(tarballPath);
        await using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        // System.Formats.Tar (net5+): extract the whole tree; we only need x32/d3d9.dll.
        await System.Formats.Tar.TarFile.ExtractToDirectoryAsync(gzip, destinationDir, overwriteFiles: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
