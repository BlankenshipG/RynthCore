using System;
using RynthCore;

namespace RynthCore.Injector;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            return Run(args);
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine();
            Console.WriteLine($"FATAL EXCEPTION: {ex}");
            try
            {
                DesktopRollingLog.AppendLine(DesktopRollingLog.StemInjector,
                    $"FATAL: {ex.GetType().Name}: {ex.Message}");
            }
            catch
            {
            }
            Console.ResetColor();
            return 99;
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
        }
    }

    private static int Run(string[] args)
    {
        var service = new EngineInjectionService();
        void Line(string s)
        {
            Console.WriteLine(s);
            try
            {
                string t = DateTime.Now.ToString("HH:mm:ss.fff");
                DesktopRollingLog.AppendLine(DesktopRollingLog.StemInjector, $"[{t}] {s}");
            }
            catch
            {
            }
        }

        Line("========================================");
        Line("        RynthCore Injector Console        ");
        Line("========================================");
        Line("");

        string? enginePath = service.TryResolveEnginePath(args.Length > 0 ? args[0] : null);
        if (enginePath == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Line($"Could not locate {EngineInjectionService.EngineDllName}.");
            Line("Copy it next to the injector or pass the full path as the first argument.");
            Console.ResetColor();
            return 1;
        }

        InjectionResult result = service.InjectFirstRunning(enginePath, Line);
        Line("");

        if (result.Success)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Line(result.Summary);
            Line("Check Desktop\\RynthCore.log for in-process engine output. " +
                 "RynthCore-Launcher.log and RynthCore-Injector.log list launcher/injector. " +
                 "Each file rolls at 10 MB; up to 10 roll segments per day per file under Desktop\\RynthLogs.");
            Console.ResetColor();
            return 0;
        }

        Console.ForegroundColor = ConsoleColor.Red;
        Line(result.Summary);
        Console.ResetColor();
        return result.ExitCode;
    }
}
