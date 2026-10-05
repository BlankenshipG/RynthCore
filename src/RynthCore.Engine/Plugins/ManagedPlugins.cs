// ============================================================================
//  RynthCore.Engine - Plugins/ManagedPlugins.cs
//
//  Managed plugins (docs/UNLOADABLE_ENGINE_PLAN.md, Phase 4). Under the CoreCLR engine
//  a plugin can be a plain managed assembly instead of a NativeAOT DLL: the same
//  source, built with -p:PublishAot=false. It keeps its C ABI: the
//  [UnmanagedCallersOnly(EntryPoint = "RynthPluginInit")] etc. methods the NativeAOT
//  build exports are found by reflection instead of GetProcAddress, so PluginLoader,
//  PluginManager and the panels' export lookups work unchanged through a stand-in
//  module handle.
//
//  Each plugin loads into its own collectible AssemblyLoadContext (its dependencies,
//  e.g. RynthCore.PluginSdk or MoonSharp, from its own folder), unloaded after its
//  RynthPluginShutdown. Unlike a NativeAOT plugin (its own runtime, never freed) it
//  shares the engine's runtime and GC and goes away with a reload.
// ============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
#if ENGINE_CORECLR
using System.Runtime.Loader;
#endif

namespace RynthCore.Engine.Plugins;

internal static class ManagedPlugins
{
    // Stand-in handles: real module bases are 64 KB aligned, these never are.
    private static int _nextHandle = 0x7F000001;
    private static readonly object Sync = new();
    private static readonly Dictionary<IntPtr, Entry> Loaded = new();

    private sealed class Entry
    {
        public required string Name;
        public required Dictionary<string, IntPtr> Exports;
#if ENGINE_CORECLR
        public required PluginLoadContext Context;
#endif
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true, ExactSpelling = true, EntryPoint = "GetProcAddress")]
    private static extern IntPtr Kernel32GetProcAddress(IntPtr hModule, string lpProcName);

    /// <summary>GetProcAddress for plugin handles: a managed plugin's export map, else kernel32.</summary>
    public static IntPtr GetProcAddress(IntPtr handle, string name)
    {
        lock (Sync)
        {
            if (Loaded.TryGetValue(handle, out Entry? e))
                return e.Exports.TryGetValue(name, out IntPtr fn) ? fn : IntPtr.Zero;
        }
        return Kernel32GetProcAddress(handle, name);
    }

    public static bool IsManagedHandle(IntPtr handle)
    {
        lock (Sync) return Loaded.ContainsKey(handle);
    }

    /// <summary>True when <paramref name="path"/> is a .NET assembly (PE with a CLR header).</summary>
    public static bool IsManagedAssembly(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new System.Reflection.PortableExecutable.PEReader(fs);
            return pe.HasMetadata && pe.PEHeaders.CorHeader != null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Loads a managed plugin; returns its stand-in module handle, or zero.</summary>
    public static IntPtr Load(string path)
    {
        string fileName = Path.GetFileName(path);
#if ENGINE_CORECLR
        try
        {
            var context = new PluginLoadContext(fileName, Path.GetDirectoryName(path)!);
            Assembly assembly = context.LoadFromFile(path);
            var exports = new Dictionary<string, IntPtr>(StringComparer.Ordinal);
            foreach (Type type in assembly.GetTypes())
            {
                foreach (MethodInfo m in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.DeclaredOnly))
                {
                    if (m.GetCustomAttribute<UnmanagedCallersOnlyAttribute>()?.EntryPoint is { Length: > 0 } entry)
                        exports[entry] = m.MethodHandle.GetFunctionPointer();
                }
            }
            IntPtr handle;
            lock (Sync)
            {
                handle = (IntPtr)_nextHandle;
                _nextHandle += 2;
                Loaded[handle] = new Entry { Name = fileName, Exports = exports, Context = context };
            }
            RynthLog.Plugin($"ManagedPlugins: {fileName} loaded into its own collectible context ({exports.Count} exports).");
            return handle;
        }
        catch (Exception ex)
        {
            RynthLog.Plugin($"ManagedPlugins: FAILED to load {fileName}: {ex}");
            return IntPtr.Zero;
        }
#else
        RynthLog.Plugin($"ManagedPlugins: {fileName} is a managed plugin; it needs the CoreCLR engine (this is the NativeAOT one). Skipping.");
        return IntPtr.Zero;
#endif
    }

    private static readonly List<Entry> PendingUnload = new();

    /// <summary>
    /// Retires a managed plugin (after its RynthPluginShutdown). Its context is only
    /// unloaded by <see cref="UnloadRetired"/> at the very end of the engine's shutdown:
    /// until then the plugin pump, the pop-outs' last frames and cached export pointers
    /// can still call into it, and once a context is unloaded any GC may free its code
    /// (2026-09-29: RynthAi's shutdown GC freed the others mid-teardown -> runtime abort).
    /// </summary>
    public static void Unload(IntPtr handle)
    {
        lock (Sync)
        {
            // Stays in Loaded (its exports still resolve) until UnloadRetired.
            if (Loaded.TryGetValue(handle, out Entry? e) && !PendingUnload.Contains(e))
                PendingUnload.Add(e);
        }
    }

    /// <summary>Unloads every retired plugin's context. Last step of EngineLifecycle.Shutdown.</summary>
    public static void UnloadRetired()
    {
        List<Entry> retired;
        lock (Sync)
        {
            retired = new List<Entry>(PendingUnload);
            PendingUnload.Clear();
            foreach (var kv in new List<KeyValuePair<IntPtr, Entry>>(Loaded))
                if (retired.Contains(kv.Value)) Loaded.Remove(kv.Key);
        }
#if ENGINE_CORECLR
        foreach (Entry e in retired)
            e.Context.Unload();
        if (retired.Count > 0)
            RynthLog.Plugin($"ManagedPlugins: {retired.Count} plugin context(s) unloading: {string.Join(", ", retired.ConvertAll(e => e.Name))}.");
#endif
    }

#if ENGINE_CORECLR
    /// <summary>A plugin and its private dependencies (from the plugin's folder).</summary>
    private sealed class PluginLoadContext : AssemblyLoadContext
    {
        private readonly string _dir;

        public PluginLoadContext(string name, string dir) : base("plugin " + name, isCollectible: true) => _dir = dir;

        public Assembly LoadFromFile(string path)
        {
            // From streams, so the plugin's folder stays unlocked (rebuilds overwrite it).
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            string pdb = Path.ChangeExtension(path, ".pdb");
            if (!File.Exists(pdb)) return LoadFromStream(fs);
            using var ps = new FileStream(pdb, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return LoadFromStream(fs, ps);
        }

        private static readonly HashSet<string> PrivateFramework = new(StringComparer.OrdinalIgnoreCase)
        {
            "System.Text.Json", "System.Text.Encodings.Web", "System.IO.Pipelines",
        };

        protected override Assembly? Load(AssemblyName name)
        {
            // The plugin's own dependencies (SDK, MoonSharp...) load privately; so do the
            // framework libraries that cache the types they see process-wide
            // (System.Text.Json roots a collectible context from the default one: Phase 0).
            // The rest of the framework comes from the default context.
            if (PrivateFramework.Contains(name.Name!))
            {
                string fx = Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, name.Name + ".dll");
                return File.Exists(fx) ? LoadFromAssemblyPath(fx) : null;
            }
            string candidate = Path.Combine(_dir, name.Name + ".dll");
            return File.Exists(candidate) ? LoadFromFile(candidate) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            string candidate = Path.Combine(_dir, unmanagedDllName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                ? unmanagedDllName
                : unmanagedDllName + ".dll");
            return File.Exists(candidate) ? LoadUnmanagedDllFromPath(candidate) : IntPtr.Zero;
        }
    }
#endif
}
