// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// ddraw.dll proxy: lets the game load RoadRashMultiFix.dll without modifying
// RoadRash95.exe. The game resolves ddraw.dll from its own folder before the
// system copy, so this DLL is mapped before the game's entry point runs.
//
//  * Every DirectDraw export is forwarded to the real system ddraw.dll. The real DLL is loaded on the first call (not in
//    DllMain, where the loader lock is held) and its functions are found by name (Wine's ordinals are not Windows' ordinals).
//  * DllMain redirects the game's entry-point jump thunk to a small stub (in
//    memory only). The stub loads RoadRashMultiFix.dll and calls RRHD_Init once the
//    game's imports are resolved, exactly when the old patched EXE did, and
//    then continues into the original startup code.
//  * If the entry point has an unexpected shape, initialization happens on the
//    first DirectDrawCreate call instead.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <string>

// Read by the installer/launcher to tell this proxy from another ddraw.dll wrapper.
extern "C" __declspec(dllexport) const char RRMF_ProxyTag[]="RRMF_PROXY_TAG";

static FARPROC g_real[23];
static const char* const g_names[23]={nullptr,"AcquireDDThreadLock","CompleteCreateSysmemSurface","D3DParseUnknownCommand","DDGetAttachedSurfaceLcl",
    "DDInternalLock","DDInternalUnlock","DSoundHelp","DirectDrawCreate","DirectDrawCreateClipper","DirectDrawCreateEx","DirectDrawEnumerateA",
    "DirectDrawEnumerateExA","DirectDrawEnumerateExW","DirectDrawEnumerateW","DllCanUnloadNow","DllGetClassObject","GetDDSurfaceLocal","GetOLEThunkData",
    "GetSurfaceFromDC","RegisterSpecialCase","ReleaseDDThreadLock","SetAppCompatData"};
static bool g_wine;
static HMODULE g_self, g_exe;
// Under Wine / Proton only: a few lines about how the real ddraw was found, next to the game's log (RoadRashHD_proxy.log).
static void proxyLog(const char* text) {
    if(!g_wine)return;
    wchar_t path[MAX_PATH]{};
    DWORD length=GetModuleFileNameW(g_self,path,MAX_PATH);
    if(!length || length>=MAX_PATH)return;
    std::wstring file(path,length);
    file.resize(file.find_last_of(L"\\/")+1);
    file+=L"RoadRashHD_proxy.log";
    HANDLE h=CreateFileW(file.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(h==INVALID_HANDLE_VALUE)return;
    DWORD written;WriteFile(h,text,DWORD(std::strlen(text)),&written,nullptr);WriteFile(h,"\r\n",2,&written,nullptr);CloseHandle(h);
}
// DirectDraw diagnostics (every platform): when CreateSurface, SetCooperativeLevel or SetDisplayMode of the game's DirectDraw object fail, one line
// with the result code and the request goes to RoadRashHD_proxy.log next to this DLL (at most 24 lines), and the first calls are logged as well. The game
// does not check these results; a surface that could not be created ends as a null pointer a little later.
static volatile LONG g_diagLines;
static void diagLog(const char* text) {
    if(InterlockedIncrement(&g_diagLines)>24)return;
    wchar_t path[MAX_PATH]{};
    DWORD length=GetModuleFileNameW(g_self,path,MAX_PATH);
    if(!length || length>=MAX_PATH)return;
    std::wstring file(path,length);
    file.resize(file.find_last_of(L"\\/")+1);
    file+=L"RoadRashHD_proxy.log";
    HANDLE h=CreateFileW(file.c_str(),FILE_APPEND_DATA,FILE_SHARE_READ|FILE_SHARE_WRITE,nullptr,OPEN_ALWAYS,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(h==INVALID_HANDLE_VALUE)return;
    DWORD written;WriteFile(h,text,DWORD(std::strlen(text)),&written,nullptr);WriteFile(h,"\r\n",2,&written,nullptr);CloseHandle(h);
}
typedef HRESULT (STDMETHODCALLTYPE *CreateSurfaceFn)(void*,void*,void**,void*);
typedef HRESULT (STDMETHODCALLTYPE *CooperativeFn)(void*,HWND,DWORD);
typedef HRESULT (STDMETHODCALLTYPE *DisplayModeFn)(void*,DWORD,DWORD,DWORD);
static CreateSurfaceFn g_createSurface;static CooperativeFn g_cooperative;static DisplayModeFn g_displayMode;
static volatile LONG g_createCalls;
static HRESULT STDMETHODCALLTYPE createSurfaceLogged(void* self,void* desc,void** surface,void* outer) {
    HRESULT hr=g_createSurface(self,desc,surface,outer);
    LONG call=InterlockedIncrement(&g_createCalls);
    if(FAILED(hr) || call<=6) {
        const uint32_t* d=reinterpret_cast<const uint32_t*>(desc);
        char line[300];
        // DDSURFACEDESC: size, flags, height, width at 0..12; pixel format flags / bit count at 76 and 84; caps at 104
        if(d && d[0]==108)std::snprintf(line,sizeof line,"DirectDraw CreateSurface #%ld: result 0x%08lx, flags 0x%lx, %lux%lu, pixel format flags 0x%lx %lu bit, caps 0x%lx, surface %p",
            long(call),unsigned long(hr),unsigned long(d[1]),unsigned long(d[3]),unsigned long(d[2]),unsigned long(d[19]),unsigned long(d[21]),unsigned long(d[26]),surface?*surface:nullptr);
        else std::snprintf(line,sizeof line,"DirectDraw CreateSurface #%ld: result 0x%08lx (description size %lu)",long(call),unsigned long(hr),d?unsigned long(d[0]):0ul);
        diagLog(line);
    }
    return hr;
}
static HRESULT STDMETHODCALLTYPE cooperativeLogged(void* self,HWND window,DWORD flags) {
    HRESULT hr=g_cooperative(self,window,flags);
    char line[160];std::snprintf(line,sizeof line,"DirectDraw SetCooperativeLevel flags 0x%lx: result 0x%08lx",unsigned long(flags),unsigned long(hr));diagLog(line);
    return hr;
}
static HRESULT STDMETHODCALLTYPE displayModeLogged(void* self,DWORD width,DWORD height,DWORD bits) {
    HRESULT hr=g_displayMode(self,width,height,bits);
    char line[160];std::snprintf(line,sizeof line,"DirectDraw SetDisplayMode %lux%lu, %lu bit: result 0x%08lx",unsigned long(width),unsigned long(height),unsigned long(bits),unsigned long(hr));diagLog(line);
    return hr;
}
// Replaces slot `index` of the vtable of a DirectDraw object (the vtable lives in the system ddraw.dll and is shared by all its objects).
static void hookSlot(void** vtable,int index,void* hook,void** original) {
    if(vtable[index]==hook)return;
    DWORD old;
    if(!VirtualProtect(&vtable[index],sizeof(void*),PAGE_READWRITE,&old))return;
    *original=vtable[index];vtable[index]=hook;
    DWORD ignored;VirtualProtect(&vtable[index],sizeof(void*),old,&ignored);
}
static void watchDirectDraw(void* object) {
    if(!object)return;
    void** vtable=*reinterpret_cast<void***>(object);
    if(!vtable)return;
    if(vtable[6]==reinterpret_cast<void*>(&createSurfaceLogged))return;     // already done for this class
    // IDirectDraw: 6 CreateSurface, 20 SetCooperativeLevel, 21 SetDisplayMode
    hookSlot(vtable,6,reinterpret_cast<void*>(&createSurfaceLogged),reinterpret_cast<void**>(&g_createSurface));
    hookSlot(vtable,20,reinterpret_cast<void*>(&cooperativeLogged),reinterpret_cast<void**>(&g_cooperative));
    hookSlot(vtable,21,reinterpret_cast<void*>(&displayModeLogged),reinterpret_cast<void**>(&g_displayMode));
}
// A forwarded export that the system ddraw.dll does not have (or when it cannot be loaded) ends here instead of jumping to a null pointer.
extern "C" HRESULT __cdecl missingExport() {return HRESULT(0x80004005);}
// The system ddraw.dll is loaded exactly once; a thread that arrives while another one is loading waits here until g_real is complete.
static INIT_ONCE g_realOnce=INIT_ONCE_STATIC_INIT;
static BOOL CALLBACK loadReal(PINIT_ONCE,PVOID,PVOID*) {
    char line[200];
    wchar_t system[MAX_PATH]{};
    UINT length=GetSystemDirectoryW(system,MAX_PATH); // SysWOW64 for this 32-bit DLL.
    HMODULE real=nullptr;
    if(length && length<MAX_PATH) {
        std::wstring full=std::wstring(system,length)+L"\\ddraw.dll";
        real=LoadLibraryW(full.c_str());
    }
    if(!real){std::snprintf(line,sizeof line,"proxy: LoadLibrary of the system ddraw.dll failed, error %lu",GetLastError());proxyLog(line);}
    int missing=0;
    for(int i=1;i<=22;++i) {
        FARPROC function=real?GetProcAddress(real,g_names[i]):nullptr;
        if(!function){function=reinterpret_cast<FARPROC>(&missingExport);++missing;}
        g_real[i]=function;
    }
    std::snprintf(line,sizeof line,"proxy: system ddraw.dll at %p, DirectDrawCreate %p, DirectDrawCreateEx %p, %d exports missing",reinterpret_cast<void*>(real),
                  reinterpret_cast<void*>(g_real[8]),reinterpret_cast<void*>(g_real[10]),missing);
    proxyLog(line);
    {   // one line about the platform: x86 game on an ARM64 Windows (Prism) is the case worth knowing
        typedef BOOL (WINAPI* IsWow64Process2Fn)(HANDLE,USHORT*,USHORT*);
        auto isWow64Process2=reinterpret_cast<IsWow64Process2Fn>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"),"IsWow64Process2"));
        USHORT process=0,machine=0;
        if(isWow64Process2 && isWow64Process2(GetCurrentProcess(),&process,&machine)) {
            std::snprintf(line,sizeof line,"proxy: process machine 0x%x, native machine 0x%x, system ddraw.dll %s",unsigned(process),unsigned(machine),real?"loaded":"NOT loaded");diagLog(line);
        }
    }
    return TRUE;
}
extern "C" void __cdecl ensureReal() {InitOnceExecuteOnce(&g_realOnce,loadReal,nullptr,nullptr);}
static volatile LONG g_initialized;
static uintptr_t g_originalEntry;

static void initializeRenderer() {
    if(InterlockedExchange(&g_initialized,1))return;
    // The manifest of the original EXE is not modified; opt in to DPI awareness
    // here, before the game creates any window.
    SetProcessDPIAware();
    proxyLog("proxy: loaded");
    wchar_t path[32768]{};
    DWORD length=GetModuleFileNameW(g_self,path,32768);
    if(!length || length>=32768)return;
    std::wstring directory(path,length);
    directory.resize(directory.find_last_of(L"\\/")+1);
    HMODULE renderer=LoadLibraryW((directory+L"RoadRashMultiFix.dll").c_str());
    if(!renderer)return; // Missing renderer: the game simply runs unpatched.
    auto init=reinterpret_cast<void(__cdecl*)(HMODULE)>(GetProcAddress(renderer,"RRHD_Init"));
    if(init)init(g_exe);
}

extern "C" void __cdecl runInitializer() { initializeRenderer(); }

static __declspec(naked) void entryHook() {
    __asm {
        pushfd
        pushad
        call runInitializer
        popad
        popfd
        jmp dword ptr g_originalEntry
    }
}

static bool hookEntry(HMODULE exe) {
    auto base=reinterpret_cast<uint8_t*>(exe);
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    if(!exe || dos->e_magic!=IMAGE_DOS_SIGNATURE)return false;
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS32*>(base+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE || !nt->OptionalHeader.AddressOfEntryPoint)return false;
    uint8_t* entry=base+nt->OptionalHeader.AddressOfEntryPoint;
    if(entry[0]!=0xe9)return false; // Only a plain jump thunk is relocated safely.
    int32_t relative;std::memcpy(&relative,entry+1,4);
    uintptr_t target=reinterpret_cast<uintptr_t>(entry+5)+relative;
    if(target==reinterpret_cast<uintptr_t>(&entryHook))return true;
    g_originalEntry=target;
    uint8_t patch[5]={0xe9};
    int32_t jump=int32_t(reinterpret_cast<uintptr_t>(&entryHook)-reinterpret_cast<uintptr_t>(entry+5));
    std::memcpy(patch+1,&jump,4);
    DWORD old,ignored;
    if(!VirtualProtect(entry,5,PAGE_EXECUTE_READWRITE,&old))return false;
    std::memcpy(entry,patch,5);
    VirtualProtect(entry,5,old,&ignored);
    FlushInstructionCache(GetCurrentProcess(),entry,5);
    return true;
}

// Used by the tests, which map the game EXE without starting it.
extern "C" BOOL __cdecl RRHD_HookEntry(HMODULE exe) {
    g_exe=exe;
    return hookEntry(exe) ? TRUE : FALSE;
}

extern "C" HRESULT WINAPI DirectDrawCreate(void* guid,void** directDraw,void* outer) {
    initializeRenderer(); // No-op when the entry hook already ran.
    ensureReal();
    if(g_real[8]==reinterpret_cast<FARPROC>(&missingExport)){proxyLog("proxy: DirectDrawCreate of the system ddraw.dll not found");return HRESULT(0x80004005);}
    HRESULT hr=reinterpret_cast<HRESULT(WINAPI*)(void*,void**,void*)>(g_real[8])(guid,directDraw,outer);
    if(SUCCEEDED(hr) && directDraw)watchDirectDraw(*directDraw);
    else if(FAILED(hr)){char line[120];std::snprintf(line,sizeof line,"DirectDrawCreate failed: 0x%08lx",unsigned long(hr));diagLog(line);}
    return hr;
}

#define FORWARD(ordinal,name) \
    extern "C" __declspec(naked) void __cdecl name() { __asm pushad __asm call ensureReal __asm popad __asm jmp dword ptr g_real[(ordinal)*4] }
FORWARD(1,AcquireDDThreadLock)
FORWARD(2,CompleteCreateSysmemSurface)
FORWARD(3,D3DParseUnknownCommand)
FORWARD(4,DDGetAttachedSurfaceLcl)
FORWARD(5,DDInternalLock)
FORWARD(6,DDInternalUnlock)
FORWARD(7,DSoundHelp)
FORWARD(9,DirectDrawCreateClipper)
FORWARD(10,DirectDrawCreateEx)
FORWARD(11,DirectDrawEnumerateA)
FORWARD(12,DirectDrawEnumerateExA)
FORWARD(13,DirectDrawEnumerateExW)
FORWARD(14,DirectDrawEnumerateW)
FORWARD(15,DllCanUnloadNow)
FORWARD(16,DllGetClassObject)
FORWARD(17,GetDDSurfaceLocal)
FORWARD(18,GetOLEThunkData)
FORWARD(19,GetSurfaceFromDC)
FORWARD(20,RegisterSpecialCase)
FORWARD(21,ReleaseDDThreadLock)
FORWARD(22,SetAppCompatData)

BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH) {
        g_self=instance;
        DisableThreadLibraryCalls(instance);
        HMODULE ntdll=GetModuleHandleW(L"ntdll.dll");
        g_wine=ntdll && GetProcAddress(ntdll,"wine_get_version")!=nullptr;
        g_exe=GetModuleHandleW(nullptr);
        hookEntry(g_exe);   // no file or other work under the loader lock: the log line is written when the renderer starts
    }
    return TRUE;
}
