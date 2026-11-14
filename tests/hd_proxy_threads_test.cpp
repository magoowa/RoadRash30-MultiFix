// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Many threads call forwarded ddraw.dll exports at the same moment, before the proxy has loaded the system ddraw.dll. Each call must reach
// the real function (never a null or half-filled table entry). DllCanUnloadNow takes no arguments and starts nothing, so the renderer is not initialised.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdio>
#include <cstdlib>
static void require(bool value,const char* message) {if(!value){std::fprintf(stderr,"FAILED: %s (%lu)\n",message,GetLastError());std::exit(1);}}
static HANDLE startGate;
static volatile LONG badResults;
static HRESULT (WINAPI* canUnload)();
static DWORD WINAPI worker(LPVOID) {
    WaitForSingleObject(startGate,INFINITE);
    for(int i=0;i<200;++i) {HRESULT hr=canUnload();if(hr!=S_OK && hr!=S_FALSE)InterlockedIncrement(&badResults);}
    return 0;
}
int wmain(int argc,wchar_t** argv) {
    require(argc==2,"ddraw proxy path");
    HMODULE proxy=LoadLibraryW(argv[1]);require(proxy!=nullptr,"load the ddraw proxy");
    canUnload=reinterpret_cast<HRESULT(WINAPI*)()>(GetProcAddress(proxy,"DllCanUnloadNow"));require(canUnload!=nullptr,"DllCanUnloadNow export");
    startGate=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    HANDLE threads[16];
    for(auto& t:threads){t=CreateThread(nullptr,0,worker,nullptr,0,nullptr);require(t!=nullptr,"thread");}
    SetEvent(startGate);
    require(WaitForMultipleObjects(16,threads,TRUE,30000)==WAIT_OBJECT_0,"threads finished");
    require(badResults==0,"every forwarded call reached the real function");
    std::puts("proxy threads: 16 threads called a forwarded export during the first load, all reached the real ddraw.dll");
    return 0;
}
