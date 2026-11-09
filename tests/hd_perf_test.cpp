// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
#include "../src/hd_renderer.cpp"
#include <chrono>
#include <array>
using Clock=std::chrono::steady_clock;
static double ms(Clock::time_point a,Clock::time_point b){return std::chrono::duration<double,std::milli>(b-a).count();}
static int WINAPI discard(HDC,int,int,int,int,int,int,int,int sh,const void*,const BITMAPINFO*,UINT,DWORD){return sh;}
int wmain(int argc,wchar_t** argv){
 if(argc!=2)return 2;
 HMODULE exe=LoadLibraryExW(argv[1],nullptr,DONT_RESOLVE_DLL_REFERENCES);if(!exe)return 3;base=reinterpret_cast<uintptr_t>(exe);
 viewport(3440,1440);ensureCpuWorld();startFrame();
 std::vector<uint8_t> low(640*480,9);at<uint8_t*>(0x6036bc)=low.data();at<int>(0x6036b8)=640;
 horizonWidth=720;horizonRows=320;horizon.resize(720*320);
 for(size_t i=0;i<horizon.size();++i)horizon[i]=uint8_t(i%250+1);
 uint8_t tex[256*256];for(size_t i=0;i<sizeof tex;++i)tex[i]=uint8_t(i%127+1);
 uint8_t pal[1024]{};for(int i=0;i<256;++i)pal[i*4]=pal[i*4+1]=pal[i*4+2]=uint8_t(i);
 std::vector<uint32_t> final(640*480);at<void*>(0x5ce864)=final.data();originalStretch=&discard;
 std::vector<uint16_t> menu(640*480,0x4210);menuBright=menu;menuDim.assign(640*480,0x2108);
 std::array<double,7> sum{};
 for(int frame=0;frame<25;++frame){
  frameJobs.clear();textureSnapshots.clear();
  auto a=Clock::now();paintBackdrop(low.data(),640);auto b=Clock::now();
  for(int n=0;n<489;++n){double y=200+(n%40)*26,x=(n*71)%3440,w=80+(n%20)*16,h=60+(n%10)*20;
   Vertex q[4]={{x,y,0,0},{x+w,y+6,256,0},{x+w-10,y+h,256,256},{x-8,y+h,0,256}};
   enqueueQuad(q,n%3?Texture{}:Texture{tex,256,256,true},uint8_t(n%200+30));
  }
  runRasterJobs();
  auto c=Clock::now();copyLegacyView();copyLegacyView();auto d=Clock::now();
  compose(low.data(),640,pal);auto e=Clock::now();final=beforeText;final[100*640+100]=0xaabbcc;
  stretchHook(nullptr,0,0,3440,1440,0,0,640,480,final.data(),nullptr,0,SRCCOPY);auto f=Clock::now();
  composeMenu(menu.data(),1280,0x7c00,0x3e0,31);auto g=Clock::now();
  if(frame>=5){sum[0]+=ms(a,b);sum[1]+=ms(b,c);sum[2]+=ms(c,d);sum[3]+=ms(d,e);sum[4]+=ms(e,f);sum[5]+=ms(a,f);sum[6]+=ms(f,g);}
 }
 const char* names[]={"backdrop","489_primitives","two_legacy_copies","palette_and_ui","caption_composite","scene_cpu_total","menu_cpu_total"};
 for(int i=0;i<7;++i)std::printf("%s_ms=%.3f\n",names[i],sum[i]/20);
 // Measure the native GDI transfer separately; the comparison above keeps the
 // exact same CPU-only workload used for the pre-fix measurement.
 BITMAPINFO bi{};bi.bmiHeader.biSize=40;bi.bmiHeader.biWidth=width;bi.bmiHeader.biHeight=-height;
 bi.bmiHeader.biPlanes=1;bi.bmiHeader.biBitCount=32;void* dibPixels=nullptr;
 HDC dc=CreateCompatibleDC(nullptr);HBITMAP bm=CreateDIBSection(dc,&bi,DIB_RGB_COLORS,&dibPixels,nullptr,0);
 HGDIOBJ previous=SelectObject(dc,bm);originalStretch=&StretchDIBits;final=beforeText;
 double presentation=0;
 for(int i=0;i<25;++i){auto a=Clock::now();stretchHook(dc,0,0,width,height,0,0,640,480,final.data(),&bi,DIB_RGB_COLORS,SRCCOPY);GdiFlush();auto b=Clock::now();if(i>=5)presentation+=ms(a,b);}
 std::printf("native_gdi_presentation_ms=%.3f\n",presentation/20);
 SelectObject(dc,previous);DeleteObject(bm);DeleteDC(dc);
 std::printf("Synthetic offscreen work only; not a measured gameplay frame rate.\n");return 0;
}
