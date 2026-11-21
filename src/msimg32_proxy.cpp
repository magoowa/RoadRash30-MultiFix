// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// msimg32.dll proxy: an alternative way to get RoadRashMultiFix.dll into the game without a ddraw.dll in the game folder. The game imports AlphaBlend from
// msimg32.dll, which Windows resolves from the game folder first, so this DLL is mapped before the game's entry point runs. Every export is forwarded to
// the system msimg32.dll. The system ddraw.dll is then loaded by the game in the ordinary way, exactly as without any patch (some systems, for example Windows
// on ARM in a virtual machine, treat the game differently when the DLL named ddraw.dll in the process is not the system one).
// The entry-point hook is the same as in ddraw_proxy.cpp: the stub loads RoadRashMultiFix.dll and calls RRHD_Init once the game's imports are resolved.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <cstdio>
#include <string>

extern "C" __declspec(dllexport) const char RRMF_ProxyTag[]="RRMF_PROXY_TAG msimg32";

static FARPROC g_real[6];
static const char* const g_names[6]={nullptr,"AlphaBlend","DllInitialize","GradientFill","TransparentBlt","vSetDdrawflag"};
static HMODULE g_self, g_exe;
extern "C" HRESULT __cdecl missingExport() {return HRESULT(0x80004005);}
static INIT_ONCE g_realOnce=INIT_ONCE_STATIC_INIT;
static BOOL CALLBACK loadReal(PINIT_ONCE,PVOID,PVOID*) {
    wchar_t system[MAX_PATH]{};
    UINT length=GetSystemDirectoryW(system,MAX_PATH);
    HMODULE real=nullptr;
    if(length && length<MAX_PATH)real=LoadLibraryW((std::wstring(system,length)+L"\\msimg32.dll").c_str());
    for(int i=1;i<=5;++i) {
        FARPROC function=real?GetProcAddress(real,g_names[i]):nullptr;
        g_real[i]=function?function:reinterpret_cast<FARPROC>(&missingExport);
    }
    return TRUE;
}
extern "C" void __cdecl ensureReal() {InitOnceExecuteOnce(&g_realOnce,loadReal,nullptr,nullptr);}
static volatile LONG g_initialized;
static uintptr_t g_originalEntry;
static void initializeRenderer() {
    if(InterlockedExchange(&g_initialized,1))return;
    SetProcessDPIAware();
    wchar_t path[32768]{};
    DWORD length=GetModuleFileNameW(g_self,path,32768);
    if(!length || length>=32768)return;
    std::wstring directory(path,length);
    directory.resize(directory.find_last_of(L"\\/")+1);
    HMODULE renderer=LoadLibraryW((directory+L"RoadRashMultiFix.dll").c_str());
    if(!renderer)return;
    auto init=reinterpret_cast<void(__cdecl*)(HMODULE)>(GetProcAddress(renderer,"RRHD_Init"));
    if(init)init(g_exe);
}
extern "C" void __cdecl runInitializer() {initializeRenderer();}
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
    if(entry[0]!=0xe9)return false;
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
#define FORWARD(ordinal,name) \
    extern "C" __declspec(naked) void __cdecl name() { __asm pushad __asm call ensureReal __asm popad __asm jmp dword ptr g_real[(ordinal)*4] }
FORWARD(1,fwd_AlphaBlend)
FORWARD(2,fwd_DllInitialize)
FORWARD(3,fwd_GradientFill)
FORWARD(4,fwd_TransparentBlt)
FORWARD(5,fwd_vSetDdrawflag)
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID) {
    if(reason==DLL_PROCESS_ATTACH) {
        g_self=instance;
        DisableThreadLibraryCalls(instance);
        g_exe=GetModuleHandleW(nullptr);
        hookEntry(g_exe);
    }
    return TRUE;
}
