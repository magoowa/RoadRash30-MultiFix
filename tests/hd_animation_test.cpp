// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Compare isolated original animation code with the actual loaded fix DLL.
// No game entry point, gameplay, window or save code is executed.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <initializer_list>

using Frame = uint8_t* (__fastcall*)(uint8_t*,int,int,int*);
static uintptr_t image;
static void require(bool ok,const char* message) {
    if(!ok){std::fprintf(stderr,"FAILED: %s\n",message);std::exit(1);}
}
template<class T> static T& field(uint8_t* p,int offset){return *reinterpret_cast<T*>(p+offset);}
struct Fault {DWORD code=0;uintptr_t instruction=0,address=0;};
static int capture(EXCEPTION_POINTERS* e,Fault* fault) {
    fault->code=e->ExceptionRecord->ExceptionCode;
    fault->instruction=reinterpret_cast<uintptr_t>(e->ExceptionRecord->ExceptionAddress);
    fault->address=e->ExceptionRecord->NumberParameters>1?e->ExceptionRecord->ExceptionInformation[1]:0;
    return EXCEPTION_EXECUTE_HANDLER;
}
static uint8_t* invoke(Frame fn,uint8_t* object,int sequence,int frame,int* duration,Fault* fault) {
    __try {return fn(object,sequence,frame,duration);}
    __except(capture(GetExceptionInformation(),fault)){return nullptr;}
}
struct Fixture {
    alignas(4) uint8_t resource[512],object[8],runtime[7*28],context[96],limits[32];
    void reset(unsigned kind) {
        std::memset(this,0,sizeof *this);
        field<uint8_t*>(object,0)=resource;field<uint8_t*>(object,4)=runtime;
        field<unsigned>(resource,8)=kind;field<int>(resource,4)=0x100;
        field<uint8_t*>(runtime,8)=limits;field<uint8_t*>(runtime,12)=context;
        field<int>(limits,16)=6;
        for(int s=0;s<2;++s) {
            int block=(kind==1?0x30:0x10)+s*(16+3*28);
            field<int>(resource,block)=s+1;field<int>(resource,block+4)=3;
            field<int>(resource,0x10c+s*16)=3;
            for(int f=0;f<3;++f) {
                int index=s*3+f;
                field<short>(resource,block+f*28+18)=short(index+1);
                field<int>(resource,0x110+s*16+f*4)=7+index;
                field<int>(runtime,(index+1)*28+4)=0x1234+index;
                field<int>(runtime,(index+1)*28+20)=0;
                field<int>(runtime,(index+1)*28+24)=1;
            }
        }
    }
};
static void put(void* where,const void* data,size_t size) {
    DWORD old,ignored;require(VirtualProtect(where,size,PAGE_EXECUTE_READWRITE,&old)!=0,"test import writable");
    std::memcpy(where,data,size);VirtualProtect(where,size,old,&ignored);
}
int wmain(int argc,wchar_t** argv) {
    require(argc==3,"game EXE and fix DLL paths");
    auto game=LoadLibraryExW(argv[1],nullptr,DONT_RESOLVE_DLL_REFERENCES);
    require(game!=nullptr,"map original code without launching game");image=reinterpret_cast<uintptr_t>(game);
    auto original=reinterpret_cast<Frame>(image+0x1be80);
    Fixture fixture;unsigned failures=0,valid=0;
    for(unsigned kind:{0u,5u,0xffffffffu})for(int frame=0;frame<2;++frame) {
        fixture.reset(kind);int duration=-1;Fault fault;
        invoke(original,fixture.object,0,frame,&duration,&fault);
        require(fault.code==EXCEPTION_ACCESS_VIOLATION && fault.instruction-image==0x1bed2,
                "original reproduces the reported crash instruction");
        require(fault.address==uintptr_t(frame*28+0x12),"original dereferences the null metadata result");
        ++failures;
    }
    auto dll=LoadLibraryW(argv[2]);require(dll!=nullptr,"load actual fixed DLL");
    auto init=reinterpret_cast<void(__cdecl*)(HMODULE)>(GetProcAddress(dll,"RRHD_Init"));
    require(init!=nullptr,"DLL initializer");
    auto stretch=&StretchDIBits;auto fill=&FillRect;
    put(reinterpret_cast<void*>(image+0x21c15c),&stretch,sizeof stretch);
    put(reinterpret_cast<void*>(image+0x21c728),&fill,sizeof fill);
    init(game);
    auto fixed=reinterpret_cast<Frame>(image+0x1a2d);
    for(unsigned kind:{0u,5u,0xffffffffu})for(int frame=0;frame<2;++frame) {
        fixture.reset(kind);int duration=-1;Fault fault;
        require(invoke(fixed,fixture.object,0,frame,&duration,&fault)==nullptr && !fault.code && duration==0,
                "actual DLL safely returns the existing no-frame result");
    }
    for(unsigned kind=1;kind<=4;++kind)for(int sequence=0;sequence<2;++sequence)for(int frame=0;frame<3;++frame) {
        fixture.reset(kind);int duration=-1;Fault fault;
        uint8_t* expected=invoke(original,fixture.object,sequence,frame,&duration,&fault);
        require(!fault.code && expected==fixture.runtime+(sequence*3+frame+1)*28 && duration==7+sequence*3+frame,
                "valid original animation fixture");
        uint8_t expectedContext[sizeof fixture.context];std::memcpy(expectedContext,fixture.context,sizeof expectedContext);
        fixture.reset(kind);duration=-1;fault={};
        require(invoke(fixed,fixture.object,sequence,frame,&duration,&fault)==expected && !fault.code && duration==7+sequence*3+frame,
                "fixed valid animations return identical frames and durations");
        require(std::memcmp(expectedContext,fixture.context,sizeof expectedContext)==0,"valid animation writes are unchanged");
        ++valid;
    }
    std::printf("animation: %u original failures at +0x1bed2 prevented; %u valid frame selections unchanged; actual DLL tested\n",failures,valid);
    return 0;
}
