/*
 * RynthCore.SehTrampoline.dll  —  x86 native SEH wrapper for dangerous AC API calls.
 *
 * NativeAOT managed try/catch cannot intercept access violations (hardware
 * exceptions / corrupted-state exceptions).  When the bot calls AC APIs on
 * objects that are being torn down by the client's object lifecycle, the AV
 * propagates up and kills acclient.exe.
 *
 * This DLL provides __try/__except wrappers that live entirely in native code.
 * Each export calls a supplied function pointer inside a structured exception
 * handler scoped only to that one call — it does not install any process-wide
 * handler (VEH/SUEF), so it cannot interfere with AC's or Avalonia's own
 * internal exception handling.
 *
 * Conventions:
 *   - All exports are __cdecl so the caller controls stack cleanup.
 *   - Return value: 1 = call completed normally; 0 = AV was caught.
 *   - out_* parameters are always written (safe default on AV).
 *   - __thiscall helpers are separate non-__try functions; MSVC does not allow
 *     inline __asm and __try in the same function on x86.
 *
 * Build:  cl /nologo /O2 /MD /LD SehTrampoline.c /link /MACHINE:X86
 *             /OUT:RynthCore.SehTrampoline.dll /DEF:SehTrampoline.def
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>

/* File version of this native module (bump with every behavioural change). */
#define RC_SEH_TRAMPOLINE_VERSION 3   /* v3: CoreCLR-hosted engine AVs logged immediately */

/* Crash-logger state that the SEH_* wrappers and DllMain also touch (defined below). */
static __declspec(thread) int t_sehDepth;       /* >0 while inside an SEH_* wrapper call   */
static void CrashLoggerFlushOnExit(void);

/* Every SEH_* wrapper brackets its guarded call with these so the crash logger knows an
 * access violation on this thread is about to be caught by our own __except. */
#define RC_SEH_ENTER() (++t_sehDepth)
#define RC_SEH_LEAVE() (--t_sehDepth)

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason, LPVOID lpReserved)
{
    (void)hModule;
    /* lpReserved != NULL on DLL_PROCESS_DETACH means the process is exiting (ExitProcess),
     * which is how AC's own crash handling ends the process: write out any recent
     * first-chance AVs so a crash AC swallowed still leaves a trace. */
    if (ul_reason == DLL_PROCESS_DETACH && lpReserved != NULL) {
        CrashLoggerFlushOnExit();
    }
    return TRUE;
}

/* Diagnostic: native module version so the engine log can confirm which build is loaded. */
__declspec(dllexport) unsigned int __cdecl RC_SehTrampolineVersion(void)
{
    return RC_SEH_TRAMPOLINE_VERSION;
}

/* ── Cdecl helpers ─────────────────────────────────────────────────────── */

/* void* __cdecl fn(unsigned int id)  — e.g. GetWeenieObject */
typedef void* (__cdecl *Fn_CdeclPtrUint)(unsigned int);
__declspec(dllexport) int __cdecl
SEH_CdeclPtrUint(Fn_CdeclPtrUint fn, unsigned int arg, void** out_result)
{
    *out_result = NULL;
    RC_SEH_ENTER();
    __try {
        *out_result = fn(arg);
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        *out_result = NULL;
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/* void* __cdecl fn()  — e.g. GetCombatSystem */
typedef void* (__cdecl *Fn_CdeclPtrVoid)(void);
__declspec(dllexport) int __cdecl
SEH_CdeclPtrVoid(Fn_CdeclPtrVoid fn, void** out_result)
{
    *out_result = NULL;
    RC_SEH_ENTER();
    __try {
        *out_result = fn();
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        *out_result = NULL;
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/* void __cdecl fn(unsigned int, unsigned char)  — e.g. CastSpell(spellId, targetIsSelected) */
typedef void (__cdecl *Fn_CdeclVoidUintByte)(unsigned int, unsigned char);
__declspec(dllexport) int __cdecl
SEH_CdeclVoidUintByte(Fn_CdeclVoidUintByte fn, unsigned int arg1, unsigned char arg2)
{
    RC_SEH_ENTER();
    __try {
        fn(arg1, arg2);
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/* void __thiscall fn(this, unsigned int spellId, unsigned int targetId)
 * e.g. ClientMagicSystem::FreeHandsAndCastSpell(this, spellId, targetId) @ 0x00567C90.
 * Modeled as __fastcall (this in ECX, a dummy EDX slot, spellId+targetId on the stack;
 * callee cleans the 8 stack bytes) — the exact, proven binding RynthSuite2 uses. The
 * target is an EXPLICIT argument, so this never reads AC's global selection and never
 * touches UI state (unlike CastSpell(spellId, targetIsSelected=1) @ 0x00568DE0, which
 * reads [magicSysSingleton+0xF4]). __fastcall is a real declarable convention, so this
 * needs no inline __asm and can host its own __try. */
typedef void (__fastcall *Fn_FreeHandsCast)(void* ecx, void* edx, unsigned int spellId, unsigned int targetId);
__declspec(dllexport) int __cdecl
SEH_FreeHandsCast(void* fn, void* this_ptr, unsigned int spellId, unsigned int targetId)
{
    RC_SEH_ENTER();
    __try {
        ((Fn_FreeHandsCast)fn)(this_ptr, 0, spellId, targetId);
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/* ── Thiscall helpers — inline __asm lives here, NOT in __try functions ── */

/*
 * MSVC x86: __thiscall puts 'this' in ECX.  C has no __thiscall call-site
 * syntax, so we use inline __asm.  These helpers must NOT contain __try;
 * the outer SEH wrappers call them so the SEH frame is established by the
 * caller's stack frame, not by the helper.
 */

/* unsigned char __thiscall fn(unsigned int objectId) */
static unsigned char __cdecl
_helper_thiscall_byte_uint(void* fn_ptr, void* this_ptr, unsigned int arg)
{
    unsigned char result = 0;
    __asm {
        mov  ecx, this_ptr
        push arg
        call fn_ptr
        mov  result, al
    }
    return result;
}

/* unsigned int __thiscall fn()  — no stack args, this in ECX */
static unsigned int __cdecl
_helper_thiscall_uint_noarg(void* fn_ptr, void* this_ptr)
{
    unsigned int result = 0;
    __asm {
        mov  ecx, this_ptr
        call fn_ptr
        mov  result, eax
    }
    return result;
}

/* ── Thiscall SEH wrappers ─────────────────────────────────────────────── */

/*
 * unsigned char __thiscall fn(unsigned int objectId)
 * e.g. ClientCombatSystem::ObjectIsAttackable(objectId)
 */
__declspec(dllexport) int __cdecl
SEH_ThiscallByteUint(void* fn, void* this_ptr, unsigned int arg,
                     unsigned char* out_result)
{
    *out_result = 0;
    RC_SEH_ENTER();
    __try {
        *out_result = _helper_thiscall_byte_uint(fn, this_ptr, arg);
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        *out_result = 0;
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/*
 * unsigned int __thiscall fn()
 * e.g. ACCWeenieObject::InqType()
 */
__declspec(dllexport) int __cdecl
SEH_ThiscallUintNoArg(void* fn, void* this_ptr, unsigned int* out_result)
{
    *out_result = 0;
    RC_SEH_ENTER();
    __try {
        *out_result = _helper_thiscall_uint_noarg(fn, this_ptr);
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        *out_result = 0;
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/* int __thiscall fn(unsigned int stype, void* outStruct)
 * e.g. CACQualities/CWeenieObject::InqAttribute2nd(stype, &SecondaryAttribute).
 * this in ECX; stype + outStruct on the stack (callee cleans, thiscall). Reads a
 * 2nd-level attribute (health/stam/mana) into the caller's struct. Used to pull a
 * MONSTER's real MaxHealth out of its (possibly partially-populated) qualities
 * table without crashing if a sub-table is null — the AV that forced the
 * AllowNonPlayerQualities gate. */
static int __cdecl
_helper_thiscall_int_uint_ptr(void* fn_ptr, void* this_ptr,
                              unsigned int arg1, void* arg2)
{
    int result = 0;
    __asm {
        mov  ecx, this_ptr
        push arg2          /* outStruct (rightmost) */
        push arg1          /* stype */
        call fn_ptr        /* thiscall callee cleans the 8 stack bytes */
        mov  result, eax
    }
    return result;
}

__declspec(dllexport) int __cdecl
SEH_ThiscallIntUintPtr(void* fn, void* this_ptr, unsigned int arg1, void* arg2,
                       int* out_result)
{
    *out_result = 0;
    RC_SEH_ENTER();
    __try {
        *out_result = _helper_thiscall_int_uint_ptr(fn, this_ptr, arg1, arg2);
    }
    __except(GetExceptionCode() == EXCEPTION_ACCESS_VIOLATION
             ? EXCEPTION_EXECUTE_HANDLER : EXCEPTION_CONTINUE_SEARCH) {
        RC_SEH_LEAVE();
        *out_result = 0;
        return 0;
    }
    RC_SEH_LEAVE();
    return 1;
}

/* ── Process-wide native crash logger (fatal-only, v2) ────────────────────
 * Goal: native-crash.log holds FATAL crashes only. v1 logged every first-chance AV
 * (including ones AC or our own SEH_* wrappers handle) plus its own stack-sweep faults,
 * so its 24-entry cap filled with noise before a real crash could be recorded.
 *
 * All paths are PURE NATIVE (no managed callback / reverse-P/Invoke — that could itself
 * trigger a NativeAOT fail-fast on an AC-owned thread) and observe-only: they always
 * return EXCEPTION_CONTINUE_SEARCH or chain, so AC's SEH and the runtime proceed exactly
 * as before. Only kernel32 APIs + hand-rolled, bounds-checked formatting (no CRT stdio).
 *
 *  1. Unhandled-exception filter (SetUnhandledExceptionFilter, chained to the previous
 *     filter): an exception nobody handled is written immediately as FATAL.
 *  2. First-chance VEH, immediate: AVs whose faulting address is inside a NativeAOT
 *     module (engine / plugins). The runtime fail-fasts on those via
 *     RaiseFailFastException, which bypasses every handler including the filter in (1),
 *     so first chance is the only chance to record them. 0xC0000602 / 0xC0000409 too.
 *  2b. (v3) Same for the CoreCLR-hosted engine (RynthCore.Shim): non-null AVs in JIT'd
 *     code (private executable memory, no module), coreclr.dll / clrjit.dll, or the
 *     framework's precompiled System.* / Microsoft.* images. CoreCLR treats a non-null
 *     AV in managed code as uncatchable and fail-fasts with HandleFatalError (the WER
 *     signature coreclr.dll+0x2c1c6e c0000005 is that fail-fast site, not the bug), so
 *     the deferred ring below was never flushed and native-crash.log stayed empty. AVs
 *     with a data address under 64K are skipped there: CoreCLR turns those into a
 *     catchable NullReferenceException.
 *  3. First-chance VEH, deferred: every other AV (acclient.exe, drivers, DINPUT8, …) is
 *     formatted into an in-memory ring and written ONLY if the process then dies — via
 *     the filter in (1), or at ExitProcess (DllMain detach) when it happened within the
 *     last CL_EXIT_FLUSH_WINDOW_MS (AC's own crash handling ends with ExitProcess).
 *     AVs that AC handles and survives never reach the file.
 *
 * Skipped entirely: AVs raised inside our own SEH_* wrappers (t_sehDepth > 0 — our
 * __except catches them) and any fault raised while the logger itself is running on
 * that thread (t_inLogger — v1 logged its own sweep faults as SehTrampoline.dll+0x13E8).
 *
 * The stack sweep is bounded to [Esp, NtCurrentTeb()->NtTib.StackBase); v1 read 4096
 * slots past ESP unconditionally and ran off the top of the stack.
 * x86 (32-bit) context fields only — matches the WOW64 target.
 */
#define CL_SLOT_BYTES           8192   /* one formatted record                        */
#define CL_RING_SLOTS           8      /* deferred first-chance records kept in memory */
#define CL_MAX_IMMEDIATE        16     /* NativeAOT-module / fail-fast records per run */
#define CL_MAX_FATAL            8      /* unhandled-exception records per run          */
#define CL_MAX_SWEEP_SLOTS      4096   /* stack slots scanned (also capped by StackBase) */
#define CL_MAX_SWEEP_LINES      96     /* code pointers emitted per record             */
#define CL_EXIT_FLUSH_WINDOW_MS 30000  /* deferred AVs this recent are flushed at exit */

#define CL_SLOT_EMPTY   0
#define CL_SLOT_WRITING 1
#define CL_SLOT_PENDING 2   /* deferred first-chance AV, not in the file (yet)   */
#define CL_SLOT_DONE    3   /* written to the file, or discarded                 */

typedef struct {
    volatile LONG state;
    DWORD         tid;
    DWORD         tick;
    unsigned int  exAddr;
    int           len;
    char          text[CL_SLOT_BYTES];
} CL_SLOT;

static wchar_t       g_crashLogPath[MAX_PATH];
static PVOID         g_vehHandle       = NULL;
static CL_SLOT       g_ring[CL_RING_SLOTS];
static volatile LONG g_ringNext        = 0;
static volatile LONG g_immediateCount  = 0;
static volatile LONG g_fatalCount      = 0;
static LPTOP_LEVEL_EXCEPTION_FILTER g_prevFilter = NULL;
static __declspec(thread) int t_inLogger;   /* logger running on this thread       */
static __declspec(thread) int t_inUef;      /* filter running (guards chain cycles) */

/* ── Bounds-checked text writer ── */
typedef struct { char* p; char* end; } CL_W;

static void cl_str(CL_W* w, const char* s)
{
    while (*s && w->p < w->end) { *w->p++ = *s++; }
}

static void cl_hex(CL_W* w, unsigned int v)
{
    const char* H = "0123456789ABCDEF";
    int i;
    cl_str(w, "0x");
    for (i = 28; i >= 0 && w->p < w->end; i -= 4) { *w->p++ = H[(v >> i) & 0xF]; }
}

static void cl_u(CL_W* w, unsigned int v)
{
    char tmp[12]; int n = 0;
    if (v == 0) { cl_str(w, "0"); return; }
    while (v) { tmp[n++] = (char)('0' + (v % 10)); v /= 10; }
    while (n && w->p < w->end) { *w->p++ = tmp[--n]; }
}

/* Leaf file name of a loaded module, as narrow ASCII (lower-cased when lower != 0). */
static int cl_module_leaf(HMODULE hm, char* out, int cap, int lower)
{
    wchar_t wpath[MAX_PATH];
    DWORD n = GetModuleFileNameW(hm, wpath, MAX_PATH);
    DWORD i, leaf = 0;
    int k = 0;
    for (i = 0; i < n; i++) { if (wpath[i] == L'\\' || wpath[i] == L'/') { leaf = i + 1; } }
    for (i = leaf; i < n && k < cap - 1; i++) {
        char ch = (char)wpath[i];
        if (lower && ch >= 'A' && ch <= 'Z') { ch = (char)(ch - 'A' + 'a'); }
        out[k++] = ch;
    }
    out[k] = 0;
    return k;
}

/* Append "module.dll+0xRVA", or "0xADDR" if the address is in no module. */
static void cl_sym(CL_W* w, unsigned int addr)
{
    HMODULE hm = NULL;
    /* GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS(0x4) | UNCHANGED_REFCOUNT(0x2) */
    if (GetModuleHandleExW(0x4 | 0x2, (LPCWSTR)(UINT_PTR)addr, &hm) && hm) {
        char leaf[MAX_PATH];
        cl_module_leaf(hm, leaf, MAX_PATH, 0);
        cl_str(w, leaf);
        cl_str(w, "+");
        cl_hex(w, addr - (unsigned int)(UINT_PTR)hm);
    } else {
        cl_hex(w, addr);
    }
}

static int cl_starts_with(const char* s, const char* prefix)
{
    while (*prefix) { if (*s++ != *prefix++) { return 0; } }
    return 1;
}

/* ── NativeAOT module detection (cached per module base) ── */
#define CL_MOD_CACHE 16
static volatile UINT_PTR g_modBase[CL_MOD_CACHE];
static volatile LONG     g_modManaged[CL_MOD_CACHE];
static volatile LONG     g_modNext = 0;

/* True when addr lies in a NativeAOT image (engine / plugin). NativeAOT images export
 * DotNetRuntimeDebugHeader; RynthCore.* names are a fallback (minus our native DLLs). */
static int cl_is_nativeaot_address(unsigned int addr)
{
    HMODULE hm = NULL;
    int i, managed;
    if (!GetModuleHandleExW(0x4 | 0x2, (LPCWSTR)(UINT_PTR)addr, &hm) || !hm) { return 0; }

    for (i = 0; i < CL_MOD_CACHE; i++) {
        if (g_modBase[i] == (UINT_PTR)hm) { return (int)g_modManaged[i]; }
    }

    managed = GetProcAddress(hm, "DotNetRuntimeDebugHeader") != NULL;
    if (!managed) {
        char leaf[MAX_PATH];
        cl_module_leaf(hm, leaf, MAX_PATH, 1);
        managed = cl_starts_with(leaf, "rynthcore.")
               && !cl_starts_with(leaf, "rynthcore.sehtrampoline")
               && !cl_starts_with(leaf, "rynthcore.cimgui");
    }

    /* Benign race: base is cleared first and published last, so a concurrent reader
     * either misses the entry (and recomputes) or sees a matching flag. */
    i = (int)((unsigned int)InterlockedIncrement(&g_modNext) % CL_MOD_CACHE);
    g_modBase[i] = 0;
    g_modManaged[i] = managed;
    g_modBase[i] = (UINT_PTR)hm;
    return managed;
}

/* True when addr is code of the CoreCLR-hosted engine: JIT'd code (committed private
 * executable memory outside any module), the runtime itself (coreclr.dll, clrjit.dll),
 * or a precompiled framework image (System.* / Microsoft.*, e.g. Marshal.ReadIntPtr in
 * System.Private.CoreLib.dll). Not cached: the JIT path has no module base to key on,
 * and VirtualQuery is cheap next to formatting a record. */
static int cl_is_coreclr_address(unsigned int addr)
{
    HMODULE hm = NULL;
    if (!GetModuleHandleExW(0x4 | 0x2, (LPCWSTR)(UINT_PTR)addr, &hm) || !hm) {
        MEMORY_BASIC_INFORMATION mbi;
        if (VirtualQuery((LPCVOID)(UINT_PTR)addr, &mbi, sizeof(mbi)) == 0) { return 0; }
        return mbi.State == MEM_COMMIT && mbi.Type == MEM_PRIVATE
            && (mbi.Protect & (PAGE_EXECUTE | PAGE_EXECUTE_READ | PAGE_EXECUTE_READWRITE
                               | PAGE_EXECUTE_WRITECOPY)) != 0;
    }
    {
        char leaf[MAX_PATH];
        cl_module_leaf(hm, leaf, MAX_PATH, 1);
        return cl_starts_with(leaf, "coreclr.dll")
            || cl_starts_with(leaf, "clrjit.dll")
            || cl_starts_with(leaf, "system.")
            || cl_starts_with(leaf, "microsoft.");
    }
}

/* ── Record formatting ── */

/* Formats one crash record (header, registers, bounded stack sweep) into buf.
 * Returns the byte count. Reads of stack memory are guarded by __try. */
static int cl_format(char* buf, int cap, PEXCEPTION_POINTERS ep, const char* banner)
{
    CL_W w;
    CONTEXT* c = ep->ContextRecord;
    DWORD code = ep->ExceptionRecord->ExceptionCode;
    unsigned int exAddr = (unsigned int)(UINT_PTR)ep->ExceptionRecord->ExceptionAddress;

    w.p = buf;
    w.end = buf + cap - 64;   /* reserve room for the END line */

    cl_str(&w, "\r\n==== NATIVE CRASH LOGGER: ");
    cl_str(&w, banner);
    cl_str(&w, " ====\r\n  code=");
    cl_hex(&w, code);
    cl_str(&w, " exAddr=");
    cl_sym(&w, exAddr);
    cl_str(&w, " tid=");
    cl_u(&w, GetCurrentThreadId());
    {
        SYSTEMTIME st; GetLocalTime(&st);
        cl_str(&w, " time=");
        cl_u(&w, st.wHour);   cl_str(&w, ":");
        if (st.wMinute < 10) { cl_str(&w, "0"); } cl_u(&w, st.wMinute); cl_str(&w, ":");
        if (st.wSecond < 10) { cl_str(&w, "0"); } cl_u(&w, st.wSecond);
    }
    if ((code == 0xC0000005 || code == 0xC0000409) && ep->ExceptionRecord->NumberParameters >= 2) {
        unsigned int acc      = (unsigned int)ep->ExceptionRecord->ExceptionInformation[0];
        unsigned int dataAddr = (unsigned int)ep->ExceptionRecord->ExceptionInformation[1];
        cl_str(&w, " access=");
        cl_str(&w, acc == 1 ? "WRITE" : (acc == 8 ? "EXEC" : "READ"));
        cl_str(&w, " dataAddr=");
        cl_hex(&w, dataAddr);   /* the [null+X] value, e.g. 0xC for the parser race */
    }
    cl_str(&w, "\r\n  eip="); cl_sym(&w, c->Eip);
    cl_str(&w, " esp=");      cl_hex(&w, c->Esp);
    cl_str(&w, " ebp=");      cl_hex(&w, c->Ebp);
    cl_str(&w, "\r\n  eax="); cl_hex(&w, c->Eax);
    cl_str(&w, " ebx=");      cl_hex(&w, c->Ebx);
    cl_str(&w, " ecx=");      cl_hex(&w, c->Ecx);
    cl_str(&w, " edx=");      cl_hex(&w, c->Edx);
    cl_str(&w, " esi=");      cl_hex(&w, c->Esi);
    cl_str(&w, " edi=");      cl_hex(&w, c->Edi);
    cl_str(&w, "\r\n  stack code pointers (esp -> StackBase):\r\n");

    {
        /* The handler runs on the faulting thread, so this TEB's stack bounds apply to
         * the faulting ESP. Never read at or past StackBase (top of the stack). */
        NT_TIB*  tib   = (NT_TIB*)NtCurrentTeb();
        UINT_PTR base  = (UINT_PTR)tib->StackBase;
        UINT_PTR limit = (UINT_PTR)tib->StackLimit;
        UINT_PTR esp   = (UINT_PTR)c->Esp;

        if (esp < limit || esp >= base) {
            cl_str(&w, "    <esp outside this thread's stack; sweep skipped>\r\n");
        } else {
            unsigned int slots = (unsigned int)((base - esp) / sizeof(unsigned int));
            if (slots > CL_MAX_SWEEP_SLOTS) { slots = CL_MAX_SWEEP_SLOTS; }
            __try {
                const unsigned int* sp = (const unsigned int*)esp;
                unsigned int i;
                int emitted = 0;
                /* 320 bytes of headroom = one full "+0xOFF  module+0xRVA" line. */
                for (i = 0; i < slots && emitted < CL_MAX_SWEEP_LINES && (w.end - w.p) > 320; i++) {
                    unsigned int v = sp[i];
                    if (v > 0x00401000u && v < 0x7FFF0000u) {
                        HMODULE hm = NULL;
                        if (GetModuleHandleExW(0x4 | 0x2, (LPCWSTR)(UINT_PTR)v, &hm) && hm) {
                            cl_str(&w, "    +");
                            cl_hex(&w, i * 4u);
                            cl_str(&w, "  ");
                            cl_sym(&w, v);
                            cl_str(&w, "\r\n");
                            emitted++;
                        }
                    }
                }
            }
            __except (EXCEPTION_EXECUTE_HANDLER) {
                cl_str(&w, "    <stack sweep faulted>\r\n");
            }
        }
    }

    w.end = buf + cap;
    cl_str(&w, "==== END NATIVE CRASH ====\r\n");
    return (int)(w.p - buf);
}

/* ── Output ── */

static void cl_append_file(const char* data, int len)
{
    HANDLE h;
    DWORD wr;
    if (len <= 0 || g_crashLogPath[0] == 0) { return; }
    h = CreateFileW(g_crashLogPath, FILE_APPEND_DATA,
        FILE_SHARE_READ | FILE_SHARE_WRITE, NULL, OPEN_ALWAYS,
        FILE_ATTRIBUTE_NORMAL, NULL);
    if (h == INVALID_HANDLE_VALUE) { return; }
    SetFilePointer(h, 0, NULL, FILE_END);
    WriteFile(h, data, (DWORD)len, &wr, NULL);
    CloseHandle(h);
}

/* Claims a ring slot for writing. Rotates through the ring, overwriting the oldest
 * PENDING/DONE record; never steals a slot another thread is mid-write on. */
static CL_SLOT* cl_claim_slot(void)
{
    int tries;
    for (tries = 0; tries < CL_RING_SLOTS * 2; tries++) {
        CL_SLOT* s = &g_ring[(unsigned int)InterlockedIncrement(&g_ringNext) % CL_RING_SLOTS];
        LONG st = s->state;
        if (st != CL_SLOT_WRITING &&
            InterlockedCompareExchange(&s->state, CL_SLOT_WRITING, st) == st) {
            return s;
        }
    }
    return NULL;
}

/* Writes PENDING deferred records to the file. Skips the record matching skipTid/skipAddr
 * (the same fault the caller is about to log) and, when maxAgeMs != 0, records older
 * than maxAgeMs (handled AVs from long before the exit). */
static void cl_flush_pending(DWORD skipTid, unsigned int skipAddr, DWORD maxAgeMs)
{
    DWORD now = GetTickCount();
    int i;
    for (i = 0; i < CL_RING_SLOTS; i++) {
        CL_SLOT* s = &g_ring[i];
        if (InterlockedCompareExchange(&s->state, CL_SLOT_WRITING, CL_SLOT_PENDING) != CL_SLOT_PENDING) {
            continue;
        }
        if ((skipTid != 0 && s->tid == skipTid && s->exAddr == skipAddr) ||
            (maxAgeMs != 0 && now - s->tick > maxAgeMs)) {
            InterlockedExchange(&s->state, CL_SLOT_DONE);
            continue;
        }
        cl_append_file(s->text, s->len);
        InterlockedExchange(&s->state, CL_SLOT_DONE);
    }
}

/* ── Handlers ── */

static LONG CALLBACK CrashVeh(PEXCEPTION_POINTERS ep)
{
    DWORD code = ep->ExceptionRecord->ExceptionCode;
    unsigned int exAddr;
    int immediate;
    int coreclr = 0;
    CL_SLOT* s;

    if (code != 0xC0000005 && code != 0xC0000602 && code != 0xC0000409) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    if (t_inLogger) {
        return EXCEPTION_CONTINUE_SEARCH;   /* our own fault — the logger's __try handles it */
    }
    if (code == 0xC0000005 && t_sehDepth > 0) {
        return EXCEPTION_CONTINUE_SEARCH;   /* an SEH_* wrapper's __except will catch it */
    }

    t_inLogger = 1;
    exAddr = (unsigned int)(UINT_PTR)ep->ExceptionRecord->ExceptionAddress;
    immediate = (code != 0xC0000005) || cl_is_nativeaot_address(exAddr);
    if (!immediate) {
        /* v3: CoreCLR-hosted engine. A data address under 64K is a null-ref that CoreCLR
         * hands to managed code as NullReferenceException, so it stays deferred. */
        unsigned int dataAddr = ep->ExceptionRecord->NumberParameters >= 2
            ? (unsigned int)ep->ExceptionRecord->ExceptionInformation[1] : 0;
        coreclr = dataAddr >= 0x10000 && cl_is_coreclr_address(exAddr);
        immediate = coreclr;
    }

    if (!immediate || InterlockedIncrement(&g_immediateCount) <= CL_MAX_IMMEDIATE) {
        s = cl_claim_slot();
        if (s) {
            s->tid    = GetCurrentThreadId();
            s->tick   = GetTickCount();
            s->exAddr = exAddr;
            s->len    = cl_format(s->text, CL_SLOT_BYTES, ep,
                code != 0xC0000005
                    ? "FAIL-FAST / STACK CHECK (always fatal)"
                    : (coreclr
                        ? "AV IN CORECLR-HOSTED CODE (JIT / runtime; fail-fast, non-null address)"
                    : immediate
                        ? "AV IN NATIVEAOT CODE (runtime fail-fasts unless it is a null-ref)"
                        : "FIRST-CHANCE AV (deferred; written because the process died or exited soon after)"));
            if (immediate) {
                cl_append_file(s->text, s->len);
                InterlockedExchange(&s->state, CL_SLOT_DONE);
            } else {
                InterlockedExchange(&s->state, CL_SLOT_PENDING);
            }
        }
    }

    t_inLogger = 0;
    return EXCEPTION_CONTINUE_SEARCH;
}

/* Unhandled-exception filter: nobody caught this exception, so it is fatal. */
static LONG WINAPI CrashUef(PEXCEPTION_POINTERS ep)
{
    LONG verdict = EXCEPTION_CONTINUE_SEARCH;

    /* A replaced-then-reasserted filter chain can loop back here; bail out of the cycle. */
    if (t_inUef) {
        return EXCEPTION_CONTINUE_SEARCH;
    }
    t_inUef = 1;

    if (!t_inLogger && InterlockedIncrement(&g_fatalCount) <= CL_MAX_FATAL) {
        unsigned int exAddr = (unsigned int)(UINT_PTR)ep->ExceptionRecord->ExceptionAddress;
        CL_SLOT* s;
        t_inLogger = 1;
        /* Earlier first-chance AVs (other threads, earlier faults) are context for this
         * crash; the first-chance record of THIS fault is skipped as a duplicate. Flushed
         * before claiming a slot so the claim can't overwrite one of them. */
        cl_flush_pending(GetCurrentThreadId(), exAddr, 0);
        s = cl_claim_slot();
        if (s) {
            s->len = cl_format(s->text, CL_SLOT_BYTES, ep, "UNHANDLED EXCEPTION (fatal)");
            cl_append_file(s->text, s->len);
            InterlockedExchange(&s->state, CL_SLOT_DONE);
        }
        t_inLogger = 0;
    }

    if (g_prevFilter) {
        verdict = g_prevFilter(ep);
    }
    t_inUef = 0;
    return verdict;
}

/* DllMain (process exit): write deferred AVs from the last CL_EXIT_FLUSH_WINDOW_MS. */
static void CrashLoggerFlushOnExit(void)
{
    if (g_crashLogPath[0] == 0) { return; }
    cl_flush_pending(0, 0, CL_EXIT_FLUSH_WINDOW_MS);
}

/* Installs the VEH once and (re-)asserts the unhandled-exception filter.
 * logPath = wide path to the dedicated crash log. Safe to call more than once:
 * a later call re-installs the filter if AC or a runtime replaced it. */
__declspec(dllexport) void __cdecl
RC_InstallCrashLogger(const wchar_t* logPath)
{
    LPTOP_LEVEL_EXCEPTION_FILTER prev;
    if (logPath) {
        int i = 0;
        for (; logPath[i] && i < MAX_PATH - 1; i++) { g_crashLogPath[i] = logPath[i]; }
        g_crashLogPath[i] = 0;
    }
    if (!g_vehHandle) {
        g_vehHandle = AddVectoredExceptionHandler(1, CrashVeh);
    }
    prev = SetUnhandledExceptionFilter(CrashUef);
    if (prev != CrashUef) {
        g_prevFilter = prev;
    }
}

/* ── Tagged-text parser guard (native MinHook detour body) ────────────────
 * Null-guards AC's tag-parser consumer FUN_0067D3C0 (live VA 0x0067D3C0)
 * against a null parser-context singleton. DAT_008F881C (AC's "current tag
 * parser context") is unsynchronized; when our off-thread pump triggers a
 * text-generation path while AC's main thread is mid-parse, it races to null
 * and the consumer does `MOV EDI,[EAX+0xC]` with EAX=0 at 0x0067D3DA -> AV
 * READ [null+0xC]. That is the recurring text-parser-race crash (captured
 * 2026-06-03 native-crash.log: code=0xC0000005 exAddr=acclient.exe+0x27D3DA).
 *
 * MUST be native: the consumer is called by AC's main thread for EVERY parsed
 * tag. A MANAGED MinHook detour here (reverse-P/Invoke per tag) reintroduced a
 * NativeAOT fail-fast and crashed in ~7s (2026-05-25, pid 20852). This detour
 * has NO managed transition. The engine installs the hook via MinHook and
 * hands us the trampoline (original) through RC_SetTagParserOriginal BEFORE it
 * enables the hook. If the parser context is null we drop ONE parse run
 * (return 0) instead of dereferencing it; the caller (FUN_0067E150) discards
 * the return value, so dropping it is safe.
 *
 * The hooked function is __cdecl: void* FUN_0067D3C0(void* parserContext).
 */
typedef void* (__cdecl *Fn_TagParserConsume)(void*);
static Fn_TagParserConsume g_tagParserOriginal = NULL;
static volatile LONG       g_tagParserSkips    = 0;

static void* __cdecl RC_TagParserGuard(void* parserContext)
{
    if (parserContext == NULL) {
        InterlockedIncrement(&g_tagParserSkips);
        return NULL;                 /* drop this parse run — no null deref */
    }
    if (g_tagParserOriginal == NULL) {
        return NULL;                 /* not wired yet (pre-SetOriginal) — fail safe */
    }
    return g_tagParserOriginal(parserContext);
}

/* Returns the address of the native detour for the engine to pass to MinHook. */
__declspec(dllexport) void* __cdecl RC_GetTagParserGuardAddress(void)
{
    return (void*)(UINT_PTR)&RC_TagParserGuard;
}

/* Engine calls this with the MinHook trampoline (original) BEFORE enabling. */
__declspec(dllexport) void __cdecl RC_SetTagParserOriginal(void* original)
{
    g_tagParserOriginal = (Fn_TagParserConsume)original;
}

/* Diagnostic: how many null-context parse runs we've dropped. */
__declspec(dllexport) unsigned int __cdecl RC_TagParserSkipCount(void)
{
    return (unsigned int)g_tagParserSkips;
}
