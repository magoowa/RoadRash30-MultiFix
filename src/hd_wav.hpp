// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Validator for the WAV files the game is allowed to stream as music (see hd_renderer.cpp, tests/hd_wav_test.cpp).
#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <string>
// A WAV the game can stream: RIFF/WAVE, a complete PCM fmt chunk (8 or 16 bit, mono or stereo, consistent block size and byte rate, 1-192 kHz)
// and a data chunk that lies inside the file. The chunks are walked in the file itself with 64-bit positions (nothing is kept in memory), so a long
// metadata chunk before fmt or data is fine and a chunk size taken from the file cannot wrap the position.
inline bool wavUsableForGame(const std::string& path) {
    HANDLE f=CreateFileA(path.c_str(),GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(f==INVALID_HANDLE_VALUE)return false;
    struct Closer {HANDLE h;~Closer(){CloseHandle(h);}} closer{f};
    LARGE_INTEGER sizeInfo{};if(!GetFileSizeEx(f,&sizeInfo))return false;
    const uint64_t fileSize=uint64_t(sizeInfo.QuadPart);
    auto readAt=[&](uint64_t offset,void* to,DWORD count)->bool {
        LARGE_INTEGER at{};at.QuadPart=LONGLONG(offset);DWORD got=0;
        return SetFilePointerEx(f,at,nullptr,FILE_BEGIN) && ReadFile(f,to,count,&got,nullptr) && got==count;
    };
    uint8_t riff[12];
    if(fileSize<44 || !readAt(0,riff,12) || std::memcmp(riff,"RIFF",4)!=0 || std::memcmp(riff+8,"WAVE",4)!=0)return false;
    bool formatOk=false,dataOk=false;
    uint64_t pos=12;
    for(int chunks=0;chunks<256 && pos+8<=fileSize && !(formatOk&&dataOk);++chunks) {
        uint8_t head[8];if(!readAt(pos,head,8))return false;
        uint32_t size;std::memcpy(&size,head+4,4);
        const uint64_t body=pos+8;
        if(std::memcmp(head,"fmt ",4)==0) {
            uint8_t fmt[16];
            if(size<16 || body+16>fileSize || !readAt(body,fmt,16))return false;
            uint16_t tag,channels,align,bits;uint32_t rate,bytesPerSecond;
            std::memcpy(&tag,fmt,2);std::memcpy(&channels,fmt+2,2);std::memcpy(&rate,fmt+4,4);std::memcpy(&bytesPerSecond,fmt+8,4);std::memcpy(&align,fmt+12,2);std::memcpy(&bits,fmt+14,2);
            formatOk=tag==1 && (bits==8 || bits==16) && (channels==1 || channels==2) && align==channels*bits/8 && rate>=1000 && rate<=192000 && uint64_t(bytesPerSecond)==uint64_t(rate)*align;
            if(!formatOk)return false;
        } else if(std::memcmp(head,"data",4)==0) {
            if(size==0 || body+size>fileSize)return false;            // empty, or longer than the file
            dataOk=true;
        }
        pos=body+uint64_t(size)+(size&1);
    }
    return formatOk && dataOk;
}
