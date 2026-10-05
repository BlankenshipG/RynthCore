using System;
using Avalonia;
using Avalonia.Win32;

namespace RynthCore.App.Avalonia;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        // The uninstaller removes the Decal bridge registration (docs/DECAL_BRIDGE_PLAN.md):
        // no window, just the per-user registry entries this launcher added.
        if (Array.Exists(args, a => string.Equals(a, "--decal-bridge-unregister", StringComparison.OrdinalIgnoreCase)))
        {
            bool ok = RynthCore.App.DecalBridgeRegistration.Unregister(out string report);
            LauncherDiag.Info($"DECALBRIDGE: unregister (command line): {report}");
            // A machine-wide entry (added with administrator rights when AC runs elevated on this
            // PC) goes too: the uninstaller removes the DLL it points at. Ask for the rights only
            // when there is one.
            if (RynthCore.App.DecalBridgeRegistration.MachineEntryIsOurs())
            {
                var outcome = RynthCore.App.DecalBridgeRegistration.UnregisterMachineWide(out string mReport);
                if (outcome == RynthCore.App.DecalBridgeRegistration.Outcome.NeedsAdmin && Environment.ProcessPath is string self)
                    outcome = RynthCore.App.DecalBridgeRegistration.RunElevated(self, "--decal-bridge-unregister-machine", out mReport);
                LauncherDiag.Info($"DECALBRIDGE: unregister machine-wide (command line): {mReport}");
                ok &= outcome == RynthCore.App.DecalBridgeRegistration.Outcome.Registered;
            }
            return ok ? 0 : 1;
        }
        if (Array.Exists(args, a => string.Equals(a, "--decal-bridge-unregister-machine", StringComparison.OrdinalIgnoreCase)))
        {
            var outcome = RynthCore.App.DecalBridgeRegistration.UnregisterMachineWide(out string report);
            LauncherDiag.Info($"DECALBRIDGE: unregister machine-wide (elevated step): {report}");
            return outcome == RynthCore.App.DecalBridgeRegistration.Outcome.Registered ? 0 : 1;
        }
        // The launcher's elevated step for "AC reads Decal's filter list for all users"
        // (MainWindow.EnsureDecalBridgeAsync): --decal-bridge-register-machine "<bridge folder>".
        int rm = Array.FindIndex(args, a => string.Equals(a, "--decal-bridge-register-machine", StringComparison.OrdinalIgnoreCase));
        if (rm >= 0)
        {
            string dir = rm + 1 < args.Length ? args[rm + 1] : RynthCore.App.DecalBridgeRegistration.DefaultBridgeDirectory;
            var outcome = RynthCore.App.DecalBridgeRegistration.RegisterMachineWide(dir, out string report);
            LauncherDiag.Info($"DECALBRIDGE: register machine-wide (elevated step): {report}");
            return outcome == RynthCore.App.DecalBridgeRegistration.Outcome.Registered ? 0 : 1;
        }
        // The installer's per-user half (installer\RynthCore.iss, as the installing user, every
        // install and update): our entry into an EXISTING per-user copy of Decal's filter list.
        int iu = Array.FindIndex(args, a => string.Equals(a, "--decal-bridge-install-user", StringComparison.OrdinalIgnoreCase));
        if (iu >= 0)
        {
            string dir = iu + 1 < args.Length ? args[iu + 1] : RynthCore.App.DecalBridgeRegistration.DefaultBridgeDirectory;
            bool ok = RynthCore.App.DecalBridgeRegistration.InstallPerUser(dir, out string report);
            LauncherDiag.Info($"DECALBRIDGE: install per-user (installer): {report}");
            return ok ? 0 : 1;
        }
        if (Array.Exists(args, a => string.Equals(a, "--decal-bridge-status", StringComparison.OrdinalIgnoreCase)))
        {
            LauncherDiag.Info($"DECALBRIDGE: status (command line): {RynthCore.App.DecalBridgeRegistration.GetStatus()}");
            return 0;
        }
        if (Array.Exists(args, a => string.Equals(a, "--decal-bridge-check", StringComparison.OrdinalIgnoreCase)))
        {
            var check = RynthCore.App.RealDecalCheckHost.Check(RynthCore.App.DecalBridgeRegistration.DefaultBridgeDirectory,
                RynthCore.App.DecalLocator.TryGetDecalAcClientPath(), EngineJsonStore.Path);
            LauncherDiag.Info($"DECALBRIDGE: check (command line):{Environment.NewLine}{check.ToReport()}");
            return check.Blocking ? 1 : 0;
        }
        return BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new Win32PlatformOptions
            {
                RenderingMode =
                [
                    Win32RenderingMode.Software
                ],
                CompositionMode =
                [
                    Win32CompositionMode.RedirectionSurface
                ]
            })
            .WithInterFont()
            .LogToTrace();
}
