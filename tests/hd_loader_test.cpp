// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <vector>
#include <string>
#include <cstdio>
#include <cstdlib>
#include <cstring>

static uintptr_t image;
template<class T> T& mem(uintptr_t va) {return *reinterpret_cast<T*>(image+va-0x400000);}
static void require(bool value,const char* message) {if(!value){std::fprintf(stderr,"FAILED: %s (%lu)\n",message,GetLastError());std::exit(1);}}
static void put(void* dst,const void* src,size_t size) {DWORD old,ignored;require(VirtualProtect(dst,size,PAGE_EXECUTE_READWRITE,&old)!=0,"test patch protection");std::memcpy(dst,src,size);VirtualProtect(dst,size,old,&ignored);FlushInstructionCache(GetCurrentProcess(),dst,size);}
static int observedCamera;
static int __cdecl isolatedFrame() {
    observedCamera=mem<int>(0x591c0c);
    uint8_t resource[64]{},record[168]{};
    *reinterpret_cast<int*>(resource)=16;*reinterpret_cast<int*>(resource+12)=16;
    auto node=record+104;*reinterpret_cast<uint8_t**>(node+4)=resource;node[60]=73;
    // Full-width source geometry is generated using the engine camera value,
    // proving the loaded module applies it before the frame producer executes.
    double right=observedCamera*2.0;
    double coords[8]={-2,0,right+2,0,right+2,480,-2,480};
    for(int i=0;i<8;++i){int p=int(coords[i]*32768);reinterpret_cast<int*>(record+68)[i]=p;reinterpret_cast<int*>(node+24)[i]=(p>>15)&~1;}
    mem<uint8_t*>(0x6185e0)=node;
    reinterpret_cast<int(__cdecl*)()>(image+0x132f)();return 123;
}

int wmain(int argc,wchar_t** argv) {
    require(argc==3,"original EXE path and ddraw proxy path");
    auto module=LoadLibraryExW(argv[1],nullptr,DONT_RESOLVE_DLL_REFERENCES);
    require(module!=nullptr,"original PE maps without starting the game");image=reinterpret_cast<uintptr_t>(module);
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS*>(image+reinterpret_cast<IMAGE_DOS_HEADER*>(image)->e_lfanew);
    auto entry=reinterpret_cast<uint8_t*>(image+nt->OptionalHeader.AddressOfEntryPoint);
    require(entry[0]==0xe9,"game entry point is a jump thunk");
    auto previous=entry+5+*reinterpret_cast<int32_t*>(entry+1);
    // Replace the game's CRT entry with RET inside this private mapped copy.
    // The actual game initialization and window creation can never execute.
    uint8_t ret=0xc3;put(previous,&ret,1);
    auto proxy=LoadLibraryW(argv[2]);require(proxy!=nullptr,"ddraw proxy loads");
    auto hook=reinterpret_cast<BOOL(__cdecl*)(HMODULE)>(GetProcAddress(proxy,"RRHD_HookEntry"));
    require(hook!=nullptr,"proxy entry hook export");
    require(hook(module)==TRUE,"proxy redirects the entry point in memory");
    require(entry[0]==0xe9 && entry+5+*reinterpret_cast<int32_t*>(entry+1)!=previous,"entry now points to the proxy stub");
    HMODULE renderer=nullptr;
    require(FindResourceW(proxy,MAKEINTRESOURCEW(101),MAKEINTRESOURCEW(5))==nullptr,"renderer resources stay in RoadRashMultiFix.dll");
    auto stretch=&StretchDIBits;put(&mem<decltype(stretch)>(0x61c15c),&stretch,sizeof stretch);
    reinterpret_cast<void(__cdecl*)()>(entry)();
    renderer=GetModuleHandleW(L"RoadRashMultiFix.dll");require(renderer!=nullptr,"proxy stub loaded the native DLL");
    require(FindResourceW(renderer,MAKEINTRESOURCEW(101),MAKEINTRESOURCEW(5))!=nullptr,"in-game resolution selector resource is included");
    require(mem<decltype(stretch)>(0x61c15c)!=stretch,"startup initialized the real native DLL");
    require(mem<uint8_t>(0x45b3c0)==0xe9,"game resolution menu points to the new selector");
    require(mem<uint8_t>(0x463961)==0xe8 && image+0x463961-0x400000+5+mem<int32_t>(0x463962)!=image+0x4634a0-0x400000,"controller scan call is moved off the game thread");
    std::vector<uint8_t> low(640*480,9);uint8_t resource[64]{},record[168]{};
    *reinterpret_cast<int*>(resource)=16;*reinterpret_cast<int*>(resource+12)=16;
    auto node=record+104;*reinterpret_cast<uint8_t**>(node+4)=resource;node[60]=73;
    double coords[8]={10.25,20.75,510.5,50.25,430.125,450.875,25.75,400.125};
    for(int i=0;i<8;++i){int p=int(coords[i]*32768);reinterpret_cast<int*>(record+68)[i]=p;reinterpret_cast<int*>(node+24)[i]=(p>>15)&~1;}
    mem<int>(0x5aa9cc)=1;mem<uint8_t>(0x5ce34c)=1;mem<uint8_t>(0x5a292a)=1;
    mem<int>(0x5a2934)=640;mem<int>(0x5a2938)=480;mem<uint8_t*>(0x6036bc)=low.data();mem<int>(0x6036b8)=640;
    mem<int>(0x6036c0)=0;mem<int>(0x6036c4)=0;mem<int>(0x6036c8)=640;mem<int>(0x6036cc)=479;
    mem<int>(0x5a29f4)=16;mem<uint8_t*>(0x6185e0)=node;mem<int>(0x591c0c)=320;
    // The original frame driver would execute gameplay/camera code. Replace it
    // only in this private test mapping with an isolated primitive producer.
    uint8_t jump[5]={0xe9};int32_t rel=int32_t(reinterpret_cast<uintptr_t>(&isolatedFrame)-(image+0x5ac55));
    std::memcpy(jump+1,&rel,4);put(reinterpret_cast<void*>(image+0x5ac50),jump,5);
    require(reinterpret_cast<int(__cdecl*)()>(image+0x58da)()==123,"loaded native frame hook executes drawing fixture");
    require(mem<int>(0x591c0c)==320,"frame restores simulation camera globals");
    int targetWidth=GetSystemMetrics(SM_CXSCREEN),targetHeight=GetSystemMetrics(SM_CYSCREEN);
    require(observedCamera>=240.0*targetWidth/targetHeight,"dynamic scene culling width reaches frame producer");
    uint8_t palette[1024]{};for(int i=0;i<256;++i)palette[i*4]=palette[i*4+1]=palette[i*4+2]=uint8_t(i);
    mem<HWND>(0x5ce858)=nullptr;
    reinterpret_cast<void(__fastcall*)(const uint8_t*,int,const uint8_t*)>(image+0x492b)(low.data(),640,palette);
    std::vector<uint32_t> oldFrame(640*480);for(size_t i=0;i<oldFrame.size();++i)oldFrame[i]=uint32_t(low[i])*0x010101;
    mem<void*>(0x5ce864)=oldFrame.data();
    BITMAPINFO target{};target.bmiHeader.biSize=40;target.bmiHeader.biWidth=targetWidth;target.bmiHeader.biHeight=-targetHeight;target.bmiHeader.biPlanes=1;target.bmiHeader.biBitCount=32;
    BITMAPINFO source=target;source.bmiHeader.biWidth=640;source.bmiHeader.biHeight=-480;
    HDC dc=CreateCompatibleDC(nullptr);void* pixels=nullptr;HBITMAP bm=CreateDIBSection(dc,&target,DIB_RGB_COLORS,&pixels,nullptr,0);HGDIOBJ old=SelectObject(dc,bm);
    int rows=mem<decltype(stretch)>(0x61c15c)(dc,100,0,640,480,0,0,640,480,oldFrame.data(),&source,DIB_RGB_COLORS,SRCCOPY);
    GdiFlush();require(rows==targetHeight,"native-sized image supplied by loaded DLL");
    for(int yy=0;yy<targetHeight;++yy)for(int xx=0;xx<targetWidth;++xx)
        require(reinterpret_cast<uint32_t*>(pixels)[size_t(yy)*targetWidth+xx]==0x494949,"real DLL fills every output pixel with native scene");
    SelectObject(dc,old);DeleteObject(bm);DeleteDC(dc);
    std::printf("loader: unmodified PE, ddraw proxy entry hook, real DLL, polygon and native GDI output passed (%d source rows)\n",rows);
    // Forwarding: the proxy must hand DirectDrawCreate to the real ddraw.dll.
    auto create=reinterpret_cast<HRESULT(WINAPI*)(void*,void**,void*)>(GetProcAddress(proxy,"DirectDrawCreate"));
    require(create!=nullptr,"proxy exports DirectDrawCreate");
    void* directDraw=nullptr;require(create(nullptr,&directDraw,nullptr)==0 && directDraw,"DirectDrawCreate is forwarded to the system ddraw.dll");
    auto vtable=*reinterpret_cast<void***>(directDraw);reinterpret_cast<ULONG(WINAPI*)(void*)>(vtable[2])(directDraw);
    static const char* names[]={"DirectDrawCreateEx","DirectDrawEnumerateA","DirectDrawEnumerateW","DllGetClassObject","DllCanUnloadNow","DirectDrawCreateClipper"};
    for(auto n:names)require(GetProcAddress(proxy,n)!=nullptr,n);
    std::printf("proxy: DirectDraw exports forwarded to the system ddraw.dll\n");
    return 0;
}
