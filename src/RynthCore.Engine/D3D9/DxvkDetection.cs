//  RynthCore.Engine — D3D9/DxvkDetection.cs
//  Detects a drop-in DXVK (or other non-system) d3d9.dll so overlay / texture
//  paths that need D3D9Ex shared HANDLEs can stay on CreateTexture+LockRect.
//  AC is 32-bit: only an x86 DXVK d3d9.dll next to acclient.exe is relevant.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using RynthCore.Engine.UI;

namespace RynthCore.Engine.D3D9;

/// <summary>
/// One-shot probe of the loaded <c>d3d9.dll</c>. Safe to call before or after
/// EndScene install; logs a Milestone line the smoke checklist greps for.
/// </summary>
internal static class DxvkDetection
{
    private static int _probed;
    private static bool _isDxvk;
    private static bool _isNonSystemD3d9;
    private static string _modulePath = "";
    private static string _productHint = "";

    /// <summary>True when the in-process d3d9 looks like DXVK (version resource / product name).</summary>
    public static bool IsDxvk
    {
        get { EnsureProbed(); return _isDxvk; }
    }

    /// <summary>True when d3d9.dll is not the Windows System32/SysWOW64 copy (DXVK, dgVoodoo, etc.).</summary>
    public static bool IsNonSystemD3d9
    {
        get { EnsureProbed(); return _isNonSystemD3d9; }
    }

    public static string ModulePath
    {
        get { EnsureProbed(); return _modulePath; }
    }

    public static string ProductHint
    {
        get { EnsureProbed(); return _productHint; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetModuleHandleA(string? lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetModuleFileNameW(IntPtr hModule, char[] lpFilename, uint nSize);

    /// <summary>
    /// Probe once and, when DXVK (or any non-system d3d9) is present, force the
    /// software Avalonia overlay path so shared-HANDLE uploads are never attempted.
    /// </summary>
    public static void ApplyOverlayPolicy()
    {
        EnsureProbed();
        if (!_isDxvk && !_isNonSystemD3d9)
            return;

        // Shared D3D9Ex HANDLEs are not reliable under translation layers; LockRect is.
        OverlaySurfaceBridge.UseSoftwareFallback();
        OverlaySurfaceBridge.NotifySharedTextureUnavailable();
        RynthLog.Milestone(
            _isDxvk
                ? $"DxvkDetection: DXVK d3d9 active ({_productHint}) — forcing software overlay textures (path={_modulePath})."
                : $"DxvkDetection: non-system d3d9 active ({_productHint}) — forcing software overlay textures (path={_modulePath}).");
    }

    /// <summary>Idempotent probe; returns whether DXVK was identified.</summary>
    public static bool EnsureProbed()
    {
        if (Interlocked.CompareExchange(ref _probed, 1, 0) != 0)
            return _isDxvk;

        try
        {
            IntPtr hMod = GetModuleHandleA("d3d9.dll");
            if (hMod == IntPtr.Zero)
            {
                RynthLog.D3D9("DxvkDetection: d3d9.dll not loaded yet — probe deferred.");
                Interlocked.Exchange(ref _probed, 0);
                return false;
            }

            char[] buf = new char[520];
            uint n = GetModuleFileNameW(hMod, buf, (uint)buf.Length);
            _modulePath = n > 0 ? new string(buf, 0, (int)n) : "";

            string dir = Path.GetDirectoryName(_modulePath) ?? "";
            string dirLower = dir.ToLowerInvariant();
            bool inSystem =
                dirLower.Contains("\\windows\\system32") ||
                dirLower.Contains("\\windows\\syswow64") ||
                dirLower.EndsWith("\\system32") ||
                dirLower.EndsWith("\\syswow64");
            _isNonSystemD3d9 = !inSystem && !string.IsNullOrEmpty(_modulePath);

            string product = "";
            string company = "";
            string fileDesc = "";
            string fileVer = "";
            try
            {
                if (!string.IsNullOrEmpty(_modulePath) && File.Exists(_modulePath))
                {
                    FileVersionInfo vi = FileVersionInfo.GetVersionInfo(_modulePath);
                    product = vi.ProductName ?? "";
                    company = vi.CompanyName ?? "";
                    fileDesc = vi.FileDescription ?? "";
                    fileVer = vi.FileVersion ?? vi.ProductVersion ?? "";
                }
            }
            catch (Exception ex)
            {
                RynthLog.D3D9($"DxvkDetection: FileVersionInfo failed: {ex.GetType().Name}: {ex.Message}");
            }

            string blob = $"{product}|{company}|{fileDesc}|{fileVer}|{_modulePath}";
            _isDxvk =
                blob.Contains("dxvk", StringComparison.OrdinalIgnoreCase) ||
                File.Exists(Path.Combine(dir, "dxvk.conf")) ||
                File.Exists(Path.Combine(dir, "d3d9.dll.conf"));

            _productHint = string.IsNullOrWhiteSpace(product)
                ? (string.IsNullOrWhiteSpace(fileDesc) ? (fileVer.Length > 0 ? fileVer : "unknown") : fileDesc)
                : product;
            if (!string.IsNullOrWhiteSpace(fileVer) && !_productHint.Contains(fileVer, StringComparison.Ordinal))
                _productHint = $"{_productHint} {fileVer}".Trim();

            if (_isDxvk)
            {
                RynthLog.Milestone($"DxvkDetection: DXVK detected — {_productHint} @ {_modulePath}");
            }
            else if (_isNonSystemD3d9)
            {
                RynthLog.Milestone($"DxvkDetection: non-system d3d9.dll (not DXVK by name) — {_productHint} @ {_modulePath}");
            }
            else
            {
                RynthLog.D3D9($"DxvkDetection: stock system d3d9.dll — {_productHint} @ {_modulePath}");
            }

            return _isDxvk;
        }
        catch (Exception ex)
        {
            RynthLog.D3D9($"DxvkDetection: probe threw {ex.GetType().Name}: {ex.Message}");
            Interlocked.Exchange(ref _probed, 0);
            return false;
        }
    }
}
