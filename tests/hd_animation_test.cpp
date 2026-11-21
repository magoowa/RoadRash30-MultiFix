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
    // The first selector with a valid kind but a garbage table header (runtime+8): the original crashes at +0x1bf0c, the fixed thunk returns no frame.
    for(unsigned kind=1;kind<=4;++kind) {
        fixture.reset(kind);field<uint32_t>(fixture.runtime,8)=0x14;int duration=-1;Fault fault;
        invoke(original,fixture.object,0,0,&duration,&fault);
        require(fault.code==EXCEPTION_ACCESS_VIOLATION && fault.instruction-image==0x1bf0c,"original reproduces the table header crash of the first selector");
        fixture.reset(kind);field<uint32_t>(fixture.runtime,8)=0x14;duration=-1;fault={};
        require(invoke(fixed,fixture.object,0,0,&duration,&fault)==nullptr && !fault.code && duration==0,"fixed first selector returns no frame for an unusable table");
        ++failures;
    }
    // The second frame selector (0x41bf50 through the thunk 0x4012fd): a frame table whose header pointer is garbage (0x14 in the report) crashed
    // at +0x1bf8e reading 0x24; the fixed thunk returns null, valid tables give the same result as the original.
    {
        using Table=uint8_t* (__fastcall*)(uint8_t*,int);
        auto originalTable=reinterpret_cast<Table>(image+0x1bf50);auto fixedTable=reinterpret_cast<Table>(image+0x12fd);
        alignas(4) uint8_t table[8*28],header[32],target[0x60];
        auto build=[&](uint32_t headerPointer) {
            std::memset(table,0,sizeof table);std::memset(header,0,sizeof header);std::memset(target,0,sizeof target);
            field<uint8_t*>(table,12)=target;field<uint32_t>(table,8)=headerPointer;field<int>(header,16)=6;
            for(int i=1;i<=2;++i){field<int>(table,i*28+4)=0x500+i;field<int>(table,i*28+20)=0;field<int>(table,i*28+24)=1;}
        };
        auto call=[&](Table fn,uint8_t* t,int frame,Fault* fault)->uint8_t* {
            __try {return fn(t,frame);} __except(capture(GetExceptionInformation(),fault)){return nullptr;}
        };
        for(uint32_t bad:{0x14u,0u,0xfffffff0u}) {
            build(bad);Fault fault;call(originalTable,table,1,&fault);
            require(fault.code==EXCEPTION_ACCESS_VIOLATION && fault.instruction-image==0x1bf8e,"original reproduces the reported frame table crash");
            build(bad);Fault none;
            require(call(fixedTable,table,1,&none)==nullptr && !none.code,"fixed frame selector returns no frame for an unusable table");
            ++failures;
        }
        build(uint32_t(reinterpret_cast<uintptr_t>(header)));Fault f1;
        uint8_t* expected=call(originalTable,table,1,&f1);
        require(!f1.code && expected==table+2*28,"valid frame table fixture");
        uint8_t expectedTarget[sizeof target];std::memcpy(expectedTarget,target,sizeof target);
        build(uint32_t(reinterpret_cast<uintptr_t>(header)));Fault f2;
        require(call(fixedTable,table,1,&f2)==expected && !f2.code && std::memcmp(expectedTarget,target,sizeof target)==0,"valid frame tables give identical results and writes");
        require(call(fixedTable,nullptr,1,&f2)==nullptr,"a null table still gives no frame");
        ++valid;
    }
    std::printf("animation: %u original failures (+0x1bed2, +0x1bf8e) prevented; %u valid frame selections unchanged; actual DLL tested\n",failures,valid);
    return 0;
}
