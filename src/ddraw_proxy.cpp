// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// ddraw.dll proxy: lets the game load RoadRashMultiFix.dll without modifying
// RoadRash95.exe. The game resolves ddraw.dll from its own folder before the
// system copy, so this DLL is mapped before the game's entry point runs.
//
//  * Every DirectDraw export is forwarded to the real system ddraw.dll.
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
#include <string>

// Read by the installer/launcher to tell this proxy from another ddraw.dll wrapper.
extern "C" __declspec(dllexport) const char RRMF_ProxyTag[]="RRMF_PROXY_TAG";

static FARPROC g_real[23];
static HMODULE g_self, g_exe;
static volatile LONG g_initialized;
static uintptr_t g_originalEntry;

static void initializeRenderer() {
    if(InterlockedExchange(&g_initialized,1))return;
    // The manifest of the original EXE is not modified; opt in to DPI awareness
    // here, before the game creates any window.
    SetProcessDPIAware();
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
    return reinterpret_cast<HRESULT(WINAPI*)(void*,void**,void*)>(g_real[8])(guid,directDraw,outer);
}

#define FORWARD(ordinal,name) \
    extern "C" __declspec(naked) void __cdecl name() { __asm jmp dword ptr g_real[(ordinal)*4] }
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
        wchar_t system[MAX_PATH]{};
        UINT length=GetSystemDirectoryW(system,MAX_PATH); // SysWOW64 for this 32-bit DLL.
        if(!length || length>=MAX_PATH)return FALSE;
        HMODULE real=LoadLibraryW((std::wstring(system,length)+L"\\ddraw.dll").c_str());
        if(!real)return FALSE;
        for(int i=1;i<=22;++i)g_real[i]=GetProcAddress(real,MAKEINTRESOURCEA(i));
        g_exe=GetModuleHandleW(nullptr);
        hookEntry(g_exe);
    }
    return TRUE;
}
