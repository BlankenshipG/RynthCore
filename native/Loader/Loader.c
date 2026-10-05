// ============================================================================
//  RynthCore.Loader.dll - native loader (docs/UNLOADABLE_ENGINE_PLAN.md, Phase 1)
//
//  Injected into acclient.exe by RynthCore.Injector / the launcher, which call the
//  RynthCoreInit export on a remote thread (same contract as the old NativeAOT loader).
//
//  Two engine hosts, chosen by looking at Runtime\RynthCore.Engine.dll:
//    - NativeAOT engine (a native DLL exporting RynthCoreEngineInit): staged to
//      .engine_loads\, LoadLibrary'd, never freed. Today's behaviour.
//    - Managed engine (a PE with a CLR header): the loader starts CoreCLR once from
//      Runtime\dotnet\ and hands the engine to RynthCore.Shim.dll (default ALC), which
//      loads each generation into a collectible AssemblyLoadContext and unloads it on
//      reload, so a reload gives its memory back.
//
//  Either way the loader owns the reload event, the file watcher, the address-space
//  guard and a MinHook facade (Hooks.c): every hook is installed once, through a
//  permanent stub, and a reload only re-points the stub.
// ============================================================================

#include "Loader.h"

#include <stdio.h>
#include <stdarg.h>

#define ENGINE_DLL_NAME        "RynthCore.Engine.dll"
#define ENGINE_INIT_EXPORT     "RynthCoreEngineInit"
#define ENGINE_SHUTDOWN_EXPORT "RynthCoreShutdown"
#define ENGINE_REFUSED_EXPORT  "RynthCoreReloadRefused"
#define LOG_DIR                "C:\\Games\\RynthCore\\Logs"
#define STAGING_SUBDIR         ".engine_loads"
#define MIN_RELOAD_INTERVAL_MS 1500
// A managed engine gives its memory back on reload, so the guard only has to keep
// room for one more generation; the NativeAOT engine leaks every generation.
#define MIN_FREE_MB_AOT        1200
#define MIN_FREE_MB_CLR        500

typedef unsigned int (__stdcall *engine_u32_fn)(void* param);
typedef unsigned int (__stdcall *engine_refused_fn)(int freeMb);

static volatile LONG g_initialized;
static char g_loaderDir[MAX_PATH];
static char g_enginePath[MAX_PATH];
static int g_engineIsManaged;
static HMODULE g_engineModule;          // NativeAOT host only
static int g_initCount;
static HANDLE g_reloadEvent;
static FILETIME g_canonicalWrite;
static volatile LONG g_reloadInFlight;
static volatile ULONGLONG g_lastReloadDone;
static CRITICAL_SECTION g_logLock;

// ---------------------------------------------------------------------------
// Logging: same per-client file and line format as the engine (LogPaths.cs).
// ---------------------------------------------------------------------------

void LoaderLog(const char* level, const char* fmt, ...)
{
    char msg[3072];
    va_list ap;
    va_start(ap, fmt);
    _vsnprintf_s(msg, sizeof msg, _TRUNCATE, fmt, ap);
    va_end(ap);

    SYSTEMTIME t;
    GetLocalTime(&t);
    char line[3300];
    int n = _snprintf_s(line, sizeof line, _TRUNCATE, "[%02d:%02d:%02d.%03d] [pid:%lu] [%s] [loader] %s\r\n",
        t.wHour, t.wMinute, t.wSecond, t.wMilliseconds, GetCurrentProcessId(), level, msg);
    if (n < 0) n = (int)strlen(line);

    char path[MAX_PATH];
    _snprintf_s(path, sizeof path, _TRUNCATE, LOG_DIR "\\RynthCore.%lu.log", GetCurrentProcessId());
    EnterCriticalSection(&g_logLock);
    CreateDirectoryA("C:\\Games\\RynthCore", NULL);
    CreateDirectoryA(LOG_DIR, NULL);
    for (int attempt = 0; attempt < 4; attempt++) {
        HANDLE h = CreateFileA(path, FILE_APPEND_DATA, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
            NULL, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, NULL);
        if (h != INVALID_HANDLE_VALUE) {
            DWORD written;
            WriteFile(h, line, (DWORD)n, &written, NULL);
            CloseHandle(h);
            break;
        }
        Sleep(5);
    }
    LeaveCriticalSection(&g_logLock);
    OutputDebugStringA(line);
}

// ---------------------------------------------------------------------------
// Address space
// ---------------------------------------------------------------------------

long LoaderFreeAddressSpaceMb(long* largestMb)
{
    SYSTEM_INFO si;
    GetSystemInfo(&si);
    unsigned char* p = (unsigned char*)si.lpMinimumApplicationAddress;
    unsigned char* end = (unsigned char*)si.lpMaximumApplicationAddress;
    unsigned long long total = 0, largest = 0;
    MEMORY_BASIC_INFORMATION mbi;
    while (p < end && VirtualQuery(p, &mbi, sizeof mbi) == sizeof mbi) {
        if (mbi.RegionSize == 0) break;
        if (mbi.State == MEM_FREE) {
            total += mbi.RegionSize;
            if (mbi.RegionSize > largest) largest = mbi.RegionSize;
        }
        p = (unsigned char*)mbi.BaseAddress + mbi.RegionSize;
    }
    if (largestMb) *largestMb = (long)(largest / (1024 * 1024));
    return (long)(total / (1024 * 1024));
}

// ---------------------------------------------------------------------------
// Engine file: staging and kind
// ---------------------------------------------------------------------------

static int FileLastWrite(const char* path, FILETIME* out)
{
    WIN32_FILE_ATTRIBUTE_DATA d;
    if (!GetFileAttributesExA(path, GetFileExInfoStandard, &d)) return 0;
    *out = d.ftLastWriteTime;
    return 1;
}

// 1 = the file is a .NET assembly (PE with a CLR header), 0 = native, -1 = unreadable.
static int IsManagedPe(const char* path)
{
    HANDLE h = CreateFileA(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL,
        OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) return -1;
    unsigned char buf[4096];
    DWORD read = 0;
    BOOL ok = ReadFile(h, buf, sizeof buf, &read, NULL);
    CloseHandle(h);
    if (!ok || read < sizeof(IMAGE_DOS_HEADER)) return -1;
    IMAGE_DOS_HEADER* dos = (IMAGE_DOS_HEADER*)buf;
    if (dos->e_magic != IMAGE_DOS_SIGNATURE || dos->e_lfanew <= 0 ||
        (DWORD)dos->e_lfanew + sizeof(IMAGE_NT_HEADERS32) > read) return -1;
    IMAGE_NT_HEADERS32* nt = (IMAGE_NT_HEADERS32*)(buf + dos->e_lfanew);
    if (nt->Signature != IMAGE_NT_SIGNATURE) return -1;
    if (nt->OptionalHeader.Magic != IMAGE_NT_OPTIONAL_HDR32_MAGIC) return 0;
    if (nt->OptionalHeader.NumberOfRvaAndSizes <= IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR) return 0;
    return nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_COM_DESCRIPTOR].VirtualAddress != 0;
}

static void CleanStaleStagingFiles(const char* stagingDir)
{
    char pattern[MAX_PATH];
    _snprintf_s(pattern, sizeof pattern, _TRUNCATE, "%s\\RynthCore.Engine.p*.gen*.*", stagingDir);   // .dll + .pdb
    WIN32_FIND_DATAA fd;
    HANDLE f = FindFirstFileA(pattern, &fd);
    if (f == INVALID_HANDLE_VALUE) return;
    do {
        char p[MAX_PATH];
        _snprintf_s(p, sizeof p, _TRUNCATE, "%s\\%s", stagingDir, fd.cFileName);
        DeleteFileA(p);   // fails for files a live process still maps: fine
    } while (FindNextFileA(f, &fd));
    FindClose(f);
}

// Copies the canonical engine to .engine_loads\RynthCore.Engine.p<pid>.gen<N>.dll so the
// canonical file is never locked (a developer can copy over it) and every generation is
// a distinct module/assembly path.
static int StageEngine(int generation, char* stagedPath, size_t cap)
{
    char stagingDir[MAX_PATH];
    _snprintf_s(stagingDir, sizeof stagingDir, _TRUNCATE, "%s\\" STAGING_SUBDIR, g_loaderDir);
    CreateDirectoryA(stagingDir, NULL);
    CleanStaleStagingFiles(stagingDir);
    _snprintf_s(stagedPath, cap, _TRUNCATE, "%s\\RynthCore.Engine.p%lu.gen%d.dll",
        stagingDir, GetCurrentProcessId(), generation);
    if (!CopyFileA(g_enginePath, stagedPath, FALSE)) {
        LoaderLog("ERR", "StageEngine: copy to %s failed (error %lu)", stagedPath, GetLastError());
        return 0;
    }
    if (g_engineIsManaged) {
        // The debug symbols travel with the assembly so stack traces keep line numbers.
        char srcPdb[MAX_PATH], dstPdb[MAX_PATH];
        _snprintf_s(srcPdb, sizeof srcPdb, _TRUNCATE, "%s\\RynthCore.Engine.pdb", g_loaderDir);
        _snprintf_s(dstPdb, sizeof dstPdb, _TRUNCATE, "%.*s.pdb", (int)(strlen(stagedPath) - 4), stagedPath);
        CopyFileA(srcPdb, dstPdb, FALSE);
    }
    LoaderLog("INF", "Staged engine for gen %d at %s", generation, stagedPath);
    return 1;
}

// ---------------------------------------------------------------------------
// Engine calls, whichever host
// ---------------------------------------------------------------------------

static void* EngineExport(const char* name)
{
    if (g_engineIsManaged) return ClrEngineExport(name);
    return g_engineModule ? (void*)GetProcAddress(g_engineModule, name) : NULL;
}

static unsigned int LoadAndInitEngine(void)
{
    g_initCount++;
    HooksBeginGeneration(g_initCount);

    char staged[MAX_PATH];
    if (!StageEngine(g_initCount, staged, sizeof staged)) return 11;
    if (!FileLastWrite(g_enginePath, &g_canonicalWrite)) GetSystemTimeAsFileTime(&g_canonicalWrite);

    if (g_engineIsManaged) {
        int rc = ClrLoadEngine(g_initCount, staged);
        if (rc != 0) {
            LoaderLog("ERR", "Managed engine gen %d failed to load (rc=%d)", g_initCount, rc);
            return 4;
        }
    } else {
        HMODULE m = LoadLibraryA(staged);
        if (!m) {
            LoaderLog("ERR", "LoadLibrary failed for engine (path=%s, error %lu)", staged, GetLastError());
            return 4;
        }
        g_engineModule = m;
        LoaderLog("INF", "Engine module loaded at 0x%08p from %s", (void*)m, staged);
    }

    engine_u32_fn init = (engine_u32_fn)EngineExport(ENGINE_INIT_EXPORT);
    if (!init) {
        LoaderLog("ERR", "Engine missing export '%s'", ENGINE_INIT_EXPORT);
        return 5;
    }
    unsigned int rc = init((void*)(INT_PTR)g_initCount);
    // The hooks the engine installed during init go in now, in one freeze, before the
    // injector resumes AC's main thread (first launch) or the new generation runs.
    HooksFlush();
    LoaderLog("INF", ENGINE_INIT_EXPORT "(initCount=%d) returned %u.", g_initCount, rc);
    return rc;
}

static void Reload(void)
{
    long largest;
    long freeBefore = LoaderFreeAddressSpaceMb(&largest);
    LoaderLog("INF", "Reload requested (gen %d, %ld MB free, largest %ld MB).", g_initCount, freeBefore, largest);

    engine_u32_fn shutdown = (engine_u32_fn)EngineExport(ENGINE_SHUTDOWN_EXPORT);
    if (shutdown) {
        unsigned int rc = shutdown(NULL);
        LoaderLog("INF", ENGINE_SHUTDOWN_EXPORT " returned %u.", rc);
    } else {
        LoaderLog("WRN", "Engine has no '%s' export; skipping graceful shutdown.", ENGINE_SHUTDOWN_EXPORT);
    }

    // Whatever the engine left enabled now passes straight through to AC.
    HooksPassThroughAll();
    ServicesReleaseAll();
    Sleep(150);

    if (g_engineIsManaged) {
        ClrUnloadEngine();
    } else if (g_engineModule) {
        // A NativeAOT module can't be freed (its runtime keeps threads with frames in it).
        LoaderLog("INF", "Reload: NativeAOT engine 0x%08p left mapped (can't unload).", (void*)g_engineModule);
        g_engineModule = NULL;
    }

    unsigned int rc = LoadAndInitEngine();
    long freeAfter = LoaderFreeAddressSpaceMb(&largest);
    LoaderLog(rc == 0 ? "INF" : "ERR", "Reload: %s (gen %d, %ld MB free, %+ld MB vs before the reload).",
        rc == 0 ? "complete" : "re-init FAILED", g_initCount, freeAfter, freeAfter - freeBefore);
}

// ---------------------------------------------------------------------------
// Reload triggers: the engine's named event, and the canonical DLL changing on disk
// ---------------------------------------------------------------------------

static DWORD WINAPI ReloadWatcher(LPVOID unused)
{
    (void)unused;
    for (;;) {
        if (WaitForSingleObject(g_reloadEvent, INFINITE) != WAIT_OBJECT_0) { Sleep(100); continue; }

        ULONGLONG last = g_lastReloadDone;
        if (last != 0 && GetTickCount64() - last < MIN_RELOAD_INTERVAL_MS) {
            LoaderLog("INF", "ReloadWatcher: signal %llu ms after the last reload; coalesced.", GetTickCount64() - last);
            continue;
        }

        long minFree = g_engineIsManaged ? MIN_FREE_MB_CLR : MIN_FREE_MB_AOT;
        long freeMb = LoaderFreeAddressSpaceMb(NULL);
        if (freeMb < minFree) {
            LoaderLog("WRN", "ReloadWatcher: reload REFUSED - only %ld MB of address space free (need %ld MB). "
                "Restart the client to load the new engine.", freeMb, minFree);
            engine_refused_fn refused = (engine_refused_fn)EngineExport(ENGINE_REFUSED_EXPORT);
            if (refused) refused((int)freeMb);
            continue;
        }

        if (InterlockedCompareExchange(&g_reloadInFlight, 1, 0) != 0) {
            LoaderLog("INF", "ReloadWatcher: reload already in flight; coalesced.");
            continue;
        }
        Reload();
        g_lastReloadDone = GetTickCount64();
        InterlockedExchange(&g_reloadInFlight, 0);

        int drained = 0;
        while (WaitForSingleObject(g_reloadEvent, 0) == WAIT_OBJECT_0) drained++;
        if (drained) LoaderLog("INF", "ReloadWatcher: drained %d signal(s) received during the reload.", drained);
    }
}

static DWORD WINAPI FileWatcher(LPVOID unused)
{
    (void)unused;
    for (;;) {
        Sleep(1000);
        FILETIME now;
        if (!FileLastWrite(g_enginePath, &now) || CompareFileTime(&now, &g_canonicalWrite) <= 0) continue;
        // Let the writer (cp, dotnet publish) finish before staging from it.
        Sleep(500);
        FILETIME stable;
        if (!FileLastWrite(g_enginePath, &stable) || CompareFileTime(&stable, &now) != 0) continue;
        LoaderLog("INF", "FileWatcher: %s changed; signaling reload.", g_enginePath);
        g_canonicalWrite = stable;
        SetEvent(g_reloadEvent);
    }
}

// ---------------------------------------------------------------------------
// Exports
// ---------------------------------------------------------------------------

const char* LoaderDirectory(void) { return g_loaderDir; }
int LoaderGeneration(void) { return g_initCount; }

unsigned int __stdcall RynthCoreInit(void* param)
{
    (void)param;
    if (InterlockedCompareExchange(&g_initialized, 1, 0) != 0) {
        LoaderLog("WRN", "RynthCoreInit called twice; ignoring the second call.");
        return 1;
    }

    HMODULE self = NULL;
    GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
        (LPCSTR)&RynthCoreInit, &self);
    GetModuleFileNameA(self, g_loaderDir, MAX_PATH);
    char* slash = strrchr(g_loaderDir, '\\');
    if (slash) *slash = 0;

    LoaderLog("INF", "RynthCoreInit (native loader %s) starting in %s.", LOADER_VERSION, g_loaderDir);
    {
        // Test clients (tools\TestClient, scripts\Test-Clr*.ps1) say so first, so monitors can skip them.
        char tag[128];
        if (GetEnvironmentVariableA("RYNTHCORE_TEST_CLIENT", tag, sizeof tag) > 0)
            LoaderLog("INF", "TestClient: %s", tag);
    }

    _snprintf_s(g_enginePath, sizeof g_enginePath, _TRUNCATE, "%s\\" ENGINE_DLL_NAME, g_loaderDir);
    int managed = IsManagedPe(g_enginePath);
    if (managed < 0) {
        LoaderLog("ERR", "FATAL: engine DLL not found or unreadable at %s", g_enginePath);
        return 3;
    }
    g_engineIsManaged = managed;
    long largest;
    long freeMb = LoaderFreeAddressSpaceMb(&largest);
    LoaderLog("INF", "Engine is %s; %ld MB of address space free (largest %ld MB).",
        managed ? "a managed assembly: hosting CoreCLR" : "NativeAOT", freeMb, largest);

    if (!HooksInit(g_loaderDir)) {
        LoaderLog("ERR", "FATAL: MinHook could not be loaded from %s", g_loaderDir);
        return 6;
    }
    if (managed) {
        int rc = ClrStart(g_loaderDir);
        if (rc != 0) {
            LoaderLog("ERR", "FATAL: CoreCLR did not start (rc=%d)", rc);
            return 7;
        }
    }

    unsigned int rc = LoadAndInitEngine();
    if (rc != 0) return rc;

    char eventName[128];
    _snprintf_s(eventName, sizeof eventName, _TRUNCATE, "Local\\RynthCore.Engine.RequestReload.p%lu", GetCurrentProcessId());
    g_reloadEvent = CreateEventA(NULL, FALSE, FALSE, eventName);
    if (!g_reloadEvent) {
        LoaderLog("WRN", "CreateEvent failed (error %lu); reload disabled.", GetLastError());
        return 0;
    }
    HANDLE t = CreateThread(NULL, 0, ReloadWatcher, NULL, 0, NULL);
    if (t) CloseHandle(t);
    t = CreateThread(NULL, 0, FileWatcher, NULL, 0, NULL);
    if (t) CloseHandle(t);
    LoaderLog("INF", "Reload watcher armed on '%s'; file watcher on '%s' (poll 1 s).", eventName, g_enginePath);
    return 0;
}

BOOL WINAPI DllMain(HINSTANCE h, DWORD reason, LPVOID reserved)
{
    (void)h; (void)reserved;
    if (reason == DLL_PROCESS_ATTACH) {
        InitializeCriticalSection(&g_logLock);
        DisableThreadLibraryCalls(h);
    }
    return TRUE;
}
