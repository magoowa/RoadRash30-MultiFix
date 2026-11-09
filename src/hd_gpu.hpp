// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
// Direct3D 11 back end for the HD renderer. It lives inside RoadRashMultiFix.dll (loaded through the
// ddraw.dll proxy), so the game executable is still untouched. Everything here is optional: when
// the device, the swap chain or a shader cannot be created, ready() stays false and the caller
// keeps using the CPU/GDI path.
//
// Per frame (gameplay):  backdrop (panorama / legacy picture)  ->  textured/flat quads in draw
// order (scan-conversion identical to rrhd::quad, with optional bilinear filtering)  ->  smoothed
// UI layer  ->  scaled to the window and presented. Menus: a CPU-composed RGBA picture replaces the
// first two steps. Textures live in R8_UINT atlases and the palette lookup happens in the shader.
#pragma once
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi1_2.h>
#include <d3dcompiler.h>
#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <cstring>
#include <unordered_map>
#include <vector>

namespace rrhd {

struct GpuQuad {            // 80 bytes, mirrored by the HLSL structured buffer
    float x[4], y[4], u[4], v[4];
    uint32_t atlasXY;       // texture rect origin in its atlas: x | y << 16
    uint32_t sizeWH;        // texture rect size: w | h << 16
    uint32_t flags;         // bit0 transparent(index 0), bit1 bilinear, bit2 flat, bit3 per-frame atlas, bit4 anisotropic averaging, bits 8-15 opacity (255 = solid)
    uint32_t color;         // flat colour palette index
};
static_assert(sizeof(GpuQuad) == 80, "GpuQuad must match the HLSL layout");

struct GpuFrameInfo {       // constant buffer (b0), 16-byte multiples
    float viewW, viewH, sceneScale, halfW;
    int32_t horizonW, horizonRows, horizonScroll, horizonY;
    float uiScale, uiOx, uiOy, uiFlat;
    float uiX0, uiX1, uiY0, uiY1;      // fitted 640x480 area in output pixels
    float backdropFilter, presentW, presentH, padC; // backdropFilter: 1 = bilinear sky/backdrop; presentW/H: back buffer size while presenting
};

class GpuRenderer {
public:
    GpuFrameInfo lastInfo{};
    void (*log)(const char*) = nullptr;
    bool failed = false;

    bool ready() const { return device && swap && !failed; }
    HWND windowHandle() const { return window; }
    int sceneWidth() const { return sceneW; }
    int sceneHeight() const { return sceneH; }

    ~GpuRenderer() { shutdown(); }

    // Creates the device and swap chain for the game window. Safe to call repeatedly.
    // Another component (the video player) may need the window: drop only the swap chain.
    void releaseWindow() {
        if (ctx) ctx->ClearState();
        rel(backRTV); rel(swap);
        if (ctx) ctx->Flush();              // make sure DXGI really destroyed the swap chain first
        if (child) { DestroyWindow(child); child = nullptr; }
        if (window && IsWindow(window)) InvalidateRect(window, nullptr, FALSE);   // let the game repaint what it drew with GDI
        window = nullptr;
    }

    bool init(HWND hwnd, int sceneWidthIn, int sceneHeightIn) {
        if (failed) return false;
        if (device && swap) { if (hwnd == window) return resizeScene(sceneWidthIn, sceneHeightIn); releaseWindow(); }
        if (device) {                       // device kept, swap chain was released
            window = hwnd;
            if (!createSwapChain(hwnd)) return false;
            return resizeScene(sceneWidthIn, sceneHeightIn);
        }
        window = hwnd;
        HMODULE d3d = LoadLibraryW(L"d3d11.dll"), dxgi = LoadLibraryW(L"dxgi.dll");
        compileFn = reinterpret_cast<pD3DCompile>(GetProcAddress(LoadLibraryW(L"d3dcompiler_47.dll"), "D3DCompile"));
        if (!d3d || !dxgi || !compileFn) return fail("d3d11/dxgi/d3dcompiler_47 not available");
        auto create = reinterpret_cast<PFN_D3D11_CREATE_DEVICE>(GetProcAddress(d3d, "D3D11CreateDevice"));
        typedef HRESULT(WINAPI* CreateFactory1)(REFIID, void**);
        auto factoryFn = reinterpret_cast<CreateFactory1>(GetProcAddress(dxgi, "CreateDXGIFactory1"));
        if (!create || !factoryFn) return fail("D3D11 entry points missing");
        D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0 }, got{};
        HRESULT hr = create(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, 1, D3D11_SDK_VERSION, &device, &got, &ctx);
        if (FAILED(hr)) return fail("D3D11CreateDevice failed (hardware, feature level 11.0)", hr);
        { // adapter name for the log
            IDXGIDevice* dd = nullptr; IDXGIAdapter* ad = nullptr;
            if (SUCCEEDED(device->QueryInterface(__uuidof(IDXGIDevice), (void**)&dd)) && SUCCEEDED(dd->GetAdapter(&ad))) {
                DXGI_ADAPTER_DESC desc{}; ad->GetDesc(&desc);
                char name[160]; WideCharToMultiByte(CP_UTF8, 0, desc.Description, -1, name, sizeof name, nullptr, nullptr);
                char msg[256]; std::snprintf(msg, sizeof msg, "Direct3D 11 renderer on %s", name); say(msg);
            }
            if (ad) ad->Release(); if (dd) dd->Release();
        }
        factoryProc = reinterpret_cast<void*>(factoryFn);
        if (!createSwapChain(hwnd)) return false;
        if (!createPipeline()) return false;
        if (!createAtlases()) return false;
        return resizeScene(sceneWidthIn, sceneHeightIn);
    }

    bool createSwapChain(HWND hwnd) {
        typedef HRESULT(WINAPI* CreateFactory1)(REFIID, void**);
        auto factoryFn = reinterpret_cast<CreateFactory1>(factoryProc);
        IDXGIFactory1* factory = nullptr;
        if (FAILED(factoryFn(__uuidof(IDXGIFactory1), (void**)&factory))) return fail("CreateDXGIFactory1 failed");
        IDXGIFactory2* factory2 = nullptr;
        if (FAILED(factory->QueryInterface(__uuidof(IDXGIFactory2), (void**)&factory2))) { factory->Release(); return fail("DXGI 1.2 factory unavailable"); }
        RECT rc{}; GetClientRect(hwnd, &rc);
        backW = std::max<LONG>(1, rc.right); backH = std::max<LONG>(1, rc.bottom);
        DXGI_SWAP_CHAIN_DESC1 sd{};
        sd.Width = backW; sd.Height = backH; sd.Format = DXGI_FORMAT_B8G8R8A8_UNORM;
        sd.SampleDesc.Count = 1; sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT | DXGI_USAGE_SHADER_INPUT;
        sd.BufferCount = 2; sd.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD; sd.Scaling = DXGI_SCALING_STRETCH;
        sd.AlphaMode = DXGI_ALPHA_MODE_IGNORE;
        // The swap chain lives on its own child window that covers the game window and is destroyed when the
        // race ends: the game window itself never gets a flip-model chain, so whatever GDI drew there reappears.
        static ATOM cls = 0;
        if (!cls) {
            WNDCLASSW wc{}; wc.lpfnWndProc = childProc; wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = L"RRHD_GpuSurface";
            wc.hCursor = nullptr; cls = RegisterClassW(&wc);
        }
        child = CreateWindowExW(0, L"RRHD_GpuSurface", L"", WS_CHILD | WS_VISIBLE | WS_CLIPSIBLINGS, 0, 0, backW, backH, hwnd, nullptr, GetModuleHandleW(nullptr), nullptr);
        if (!child) { factory2->Release(); factory->Release(); return fail("could not create the Direct3D surface window"); }
        SetWindowPos(child, HWND_TOP, 0, 0, backW, backH, SWP_NOACTIVATE | SWP_SHOWWINDOW);
        HRESULT hr = factory2->CreateSwapChainForHwnd(device, child, &sd, nullptr, nullptr, &swap);
        if (SUCCEEDED(hr)) factory->MakeWindowAssociation(child, DXGI_MWA_NO_ALT_ENTER | DXGI_MWA_NO_WINDOW_CHANGES);
        factory2->Release(); factory->Release();
        if (FAILED(hr)) return fail("CreateSwapChainForHwnd failed", hr);
        return createBackBufferView();
    }
    void shutdown() {
        if (ctx) ctx->ClearState();
        rel(backRTV); rel(swap);
        if (child) { DestroyWindow(child); child = nullptr; }
        rel(sceneRT); rel(sceneRTV); rel(sceneSRV); rel(cpuTex); rel(cpuSRV); rel(backRTV);
        rel(swap); rel(ctx); rel(device);
        rel(paletteTex); rel(paletteSRV); rel(legacyTex); rel(legacySRV); rel(horizonTex); rel(horizonSRV);
        rel(staticAtlas); rel(staticSRV); rel(frameAtlas); rel(frameSRV); rel(uiTex); rel(uiSRV);
        rel(quadBuf); rel(quadSRV); rel(frameCB);
        rel(vsFull); rel(psBackdrop); rel(vsQuad); rel(psQuad); rel(psCopyCpu); rel(psUi); rel(psPresent);
        rel(blendOpaque); rel(blendUi); rel(blendQuad); rel(rast); rel(pointSampler); rel(linearSampler); rel(readStage);
        window = nullptr;
    }

    // ---------------------------------------------------------------- per-frame uploads
    void setPalette(const uint32_t* colors256) {      // 0x00RRGGBB entries
        uint32_t tmp[256];
        for (int i = 0; i < 256; ++i) tmp[i] = 0xff000000u | colors256[i];
        ctx->UpdateSubresource(paletteTex, 0, nullptr, tmp, 256 * 4, 0);
    }
    void setLegacyBackdrop(const uint8_t* pixels640x480) {
        ctx->UpdateSubresource(legacyTex, 0, nullptr, pixels640x480, 640, 0);
    }
    void setHorizon(const uint8_t* pixels, int w, int h) {
        rel(horizonTex); rel(horizonSRV);
        horizonW = w; horizonRows = h;
        if (!pixels || w <= 0 || h <= 0) { horizonW = horizonRows = 0; return; }
        D3D11_TEXTURE2D_DESC td{}; td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1;
        td.Format = DXGI_FORMAT_R8_UINT; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        D3D11_SUBRESOURCE_DATA sr{ pixels, UINT(w), 0 };
        if (FAILED(device->CreateTexture2D(&td, &sr, &horizonTex)) || FAILED(device->CreateShaderResourceView(horizonTex, nullptr, &horizonSRV))) {
            rel(horizonTex); rel(horizonSRV); horizonW = horizonRows = 0;
        }
    }
    // The static atlas caches level textures by pointer; call when a level (re)loads.
    void flushTextures() { staticMap.clear(); staticShelves.clear(); staticTop = 0; }
    void beginFrame() { frameShelves.clear(); frameTop = 0; }

    // Uploads a W x H indexed texture (or finds it in the cache) and returns its atlas origin.
    bool texture(const uint8_t* pixels, int w, int h, bool transient, uint32_t& xy, uint32_t& flagsOut) {
        if (!pixels || w <= 0 || h <= 0 || w > 1024 || h > 1024) return false;
        if (transient) {
            int x, y;
            if (!allocate(frameShelves, frameTop, FRAME_ATLAS, FRAME_ATLAS, w, h, x, y)) return false;
            D3D11_BOX box{ UINT(x), UINT(y), 0, UINT(x + w), UINT(y + h), 1 };
            ctx->UpdateSubresource(frameAtlas, 0, &box, pixels, UINT(w), 0);
            xy = uint32_t(x) | (uint32_t(y) << 16); flagsOut = 8; return true;
        }
        uint64_t key = (uint64_t(reinterpret_cast<uintptr_t>(pixels)) << 24) ^ (uint64_t(w) << 12) ^ uint64_t(h);
        uint32_t sum = checksum(pixels, w, h);
        auto it = staticMap.find(key);
        if (it != staticMap.end()) {
            if (it->second.sum == sum) { xy = it->second.xy; flagsOut = 0; return true; }
            int x = it->second.xy & 0xffff, y = it->second.xy >> 16; // same size: refresh in place
            D3D11_BOX box{ UINT(x), UINT(y), 0, UINT(x + w), UINT(y + h), 1 };
            ctx->UpdateSubresource(staticAtlas, 0, &box, pixels, UINT(w), 0);
            it->second.sum = sum; xy = it->second.xy; flagsOut = 0; return true;
        }
        int x, y;
        if (!allocate(staticShelves, staticTop, STATIC_ATLAS, STATIC_ATLAS, w, h, x, y)) return false;
        D3D11_BOX box{ UINT(x), UINT(y), 0, UINT(x + w), UINT(y + h), 1 };
        ctx->UpdateSubresource(staticAtlas, 0, &box, pixels, UINT(w), 0);
        staticMap[key] = { uint32_t(x) | (uint32_t(y) << 16), sum };
        xy = uint32_t(x) | (uint32_t(y) << 16); flagsOut = 0; return true;
    }
    bool staticAtlasFull() const { return staticFull; }
    void clearStaticFull() { staticFull = false; }

    // ---------------------------------------------------------------- drawing
    // (Re)create the scene render target when the internal render size changes.
    bool resizeScene(int w, int h) {
        w = std::max(1, w); h = std::max(1, h);
        if (sceneRT && w == sceneW && h == sceneH) return true;
        rel(sceneRT); rel(sceneRTV); rel(sceneSRV); rel(cpuTex); rel(cpuSRV); rel(readStage);
        sceneW = w; sceneH = h;
        D3D11_TEXTURE2D_DESC td{}; td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1;
        td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT;
        td.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        if (FAILED(device->CreateTexture2D(&td, nullptr, &sceneRT)) || FAILED(device->CreateRenderTargetView(sceneRT, nullptr, &sceneRTV)) ||
            FAILED(device->CreateShaderResourceView(sceneRT, nullptr, &sceneSRV))) return fail("scene render target failed");
        td.Usage = D3D11_USAGE_DYNAMIC; td.BindFlags = D3D11_BIND_SHADER_RESOURCE; td.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        if (FAILED(device->CreateTexture2D(&td, nullptr, &cpuTex)) || FAILED(device->CreateShaderResourceView(cpuTex, nullptr, &cpuSRV)))
            return fail("CPU frame texture failed");
        return true;
    }

    // Draw a picture composed on the CPU (menus, fallback) into the scene target.
    void drawCpuFrame(const uint32_t* rgba) {
        D3D11_MAPPED_SUBRESOURCE m{};
        if (FAILED(ctx->Map(cpuTex, 0, D3D11_MAP_WRITE_DISCARD, 0, &m))) return;
        for (int y = 0; y < sceneH; ++y) std::memcpy(static_cast<uint8_t*>(m.pData) + size_t(y) * m.RowPitch, rgba + size_t(y) * sceneW, size_t(sceneW) * 4);
        ctx->Unmap(cpuTex, 0);
        bindScene();
        ctx->PSSetShader(psCopyCpu, nullptr, 0);
        ctx->PSSetShaderResources(7, 1, &cpuSRV);
        ctx->OMSetBlendState(blendOpaque, nullptr, 0xffffffff);
        ctx->Draw(3, 0);
        ID3D11ShaderResourceView* none = nullptr; ctx->PSSetShaderResources(7, 1, &none);
    }

    // Draw the 3D scene: backdrop then quads in order.
    void drawScene(const GpuFrameInfo& info, const GpuQuad* quads, size_t count) {
        updateFrameInfo(info);
        bindScene();
        ctx->OMSetBlendState(blendOpaque, nullptr, 0xffffffff);
        ID3D11ShaderResourceView* srvs[5] = { legacySRV, horizonSRV ? horizonSRV : legacySRV, paletteSRV, staticSRV, frameSRV };
        ctx->PSSetShaderResources(0, 5, srvs);
        ctx->PSSetShader(psBackdrop, nullptr, 0);
        ctx->Draw(3, 0);
        if (count) {
            if (count > quadCapacity && !growQuads(count)) return;
            D3D11_MAPPED_SUBRESOURCE m{};
            if (FAILED(ctx->Map(quadBuf, 0, D3D11_MAP_WRITE_DISCARD, 0, &m))) return;
            std::memcpy(m.pData, quads, count * sizeof(GpuQuad));
            ctx->Unmap(quadBuf, 0);
            ctx->VSSetShaderResources(5, 1, &quadSRV);
            ctx->PSSetShaderResources(5, 1, &quadSRV);
            ctx->VSSetShader(vsQuad, nullptr, 0);
            ctx->PSSetShader(psQuad, nullptr, 0);
            ctx->OMSetBlendState(blendQuad, nullptr, 0xffffffff);   // distant objects fade in with their alpha
            // 4096 instances per call keeps each draw modest; order is preserved across calls.
            for (size_t first = 0; first < count; first += 4096) {
                UINT n = UINT(std::min<size_t>(4096, count - first));
                ctx->DrawInstanced(6, n, 0, UINT(first));
            }
            ID3D11ShaderResourceView* none = nullptr;
            ctx->VSSetShaderResources(5, 1, &none); ctx->PSSetShaderResources(5, 1, &none);
            ctx->VSSetShader(vsFull, nullptr, 0);
        }
        ID3D11ShaderResourceView* nones[5] = {}; ctx->PSSetShaderResources(0, 5, nones);
    }

    // Smoothed UI layer: rgb = colour, a = coverage mask (0 / 255) at 640x480.
    void drawUi(const GpuFrameInfo& info, const uint32_t* layer640x480, bool flat) {
        GpuFrameInfo copy = info; copy.uiFlat = flat ? 1.0f : 0.0f;
        updateFrameInfo(copy);
        ctx->UpdateSubresource(uiTex, 0, nullptr, layer640x480, 640 * 4, 0);
        bindScene();
        ctx->OMSetBlendState(blendUi, nullptr, 0xffffffff);
        ctx->PSSetShader(psUi, nullptr, 0);
        ctx->PSSetShaderResources(6, 1, &uiSRV);
        ctx->Draw(3, 0);
        ID3D11ShaderResourceView* none = nullptr; ctx->PSSetShaderResources(6, 1, &none);
    }

    // Scale the scene to the window and present.
    bool present(int syncInterval) {
        RECT rc{}; GetClientRect(window, &rc);
        int cw = std::max<LONG>(1, rc.right), ch = std::max<LONG>(1, rc.bottom);
        if (cw != backW || ch != backH) {
            if (backRTV) { ctx->OMSetRenderTargets(0, nullptr, nullptr); rel(backRTV); }
            if (child) SetWindowPos(child, HWND_TOP, 0, 0, cw, ch, SWP_NOACTIVATE);
            HRESULT hr = swap->ResizeBuffers(0, cw, ch, DXGI_FORMAT_UNKNOWN, 0);
            if (FAILED(hr)) return fail("ResizeBuffers failed", hr);
            backW = cw; backH = ch;
            if (!createBackBufferView()) return false;
        }
        if ((backW != sceneW || backH != sceneH) && log) {
            static int lastBW = 0, lastBH = 0, lastSW = 0, lastSH = 0;
            if (lastBW != backW || lastBH != backH || lastSW != sceneW || lastSH != sceneH) {
                lastBW = backW; lastBH = backH; lastSW = sceneW; lastSH = sceneH;
                char m[160]; std::snprintf(m, sizeof m, "present: back buffer %dx%d, scene %dx%d (scene is stretched over the window)", backW, backH, sceneW, sceneH);
                log(m);
            }
        }
        { GpuFrameInfo copy = lastInfo; copy.presentW = float(backW); copy.presentH = float(backH); updateFrameInfo(copy); }
        D3D11_VIEWPORT vp{ 0, 0, float(backW), float(backH), 0, 1 };
        ctx->OMSetRenderTargets(1, &backRTV, nullptr);
        ctx->RSSetViewports(1, &vp);
        ctx->VSSetShader(vsFull, nullptr, 0);
        ctx->PSSetShader(psPresent, nullptr, 0);
        ctx->PSSetShaderResources(7, 1, &sceneSRV);
        ctx->PSSetSamplers(0, 1, &pointSampler);   // never filter the picture: menus and text stay crisp
        ctx->OMSetBlendState(blendOpaque, nullptr, 0xffffffff);
        ctx->Draw(3, 0);
        ID3D11ShaderResourceView* none = nullptr; ctx->PSSetShaderResources(7, 1, &none);
        HRESULT hr = swap->Present(syncInterval, 0);
        if (hr == DXGI_ERROR_DEVICE_REMOVED || hr == DXGI_ERROR_DEVICE_RESET) return fail("device lost", hr);
        return true;
    }

    // Copy the scene target (what was drawn before scaling) back to the CPU, 0x00RRGGBB.
    bool readScene(std::vector<uint32_t>& out) {
        if (!sceneRT) return false;
        if (!readStage) {
            D3D11_TEXTURE2D_DESC td{}; td.Width = sceneW; td.Height = sceneH; td.MipLevels = 1; td.ArraySize = 1;
            td.Format = DXGI_FORMAT_B8G8R8A8_UNORM; td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_STAGING; td.CPUAccessFlags = D3D11_CPU_ACCESS_READ;
            if (FAILED(device->CreateTexture2D(&td, nullptr, &readStage))) return false;
        }
        ctx->CopyResource(readStage, sceneRT);
        D3D11_MAPPED_SUBRESOURCE m{};
        if (FAILED(ctx->Map(readStage, 0, D3D11_MAP_READ, 0, &m))) return false;
        out.resize(size_t(sceneW) * sceneH);
        for (int y = 0; y < sceneH; ++y) {
            const uint32_t* row = reinterpret_cast<const uint32_t*>(static_cast<const uint8_t*>(m.pData) + size_t(y) * m.RowPitch);
            for (int x = 0; x < sceneW; ++x) out[size_t(y) * sceneW + x] = row[x] & 0x00ffffffu;
        }
        ctx->Unmap(readStage, 0);
        return true;
    }

    int horizonWidth() const { return horizonW; }
    int horizonHeight() const { return horizonRows; }

private:
    static constexpr int STATIC_ATLAS = 4096, FRAME_ATLAS = 2048;
    struct Shelf { int y, h, x; };
    struct Entry { uint32_t xy, sum; };

    HWND window = nullptr, child = nullptr;
    static LRESULT CALLBACK childProc(HWND h, UINT m, WPARAM w, LPARAM l) {
        if (m == WM_NCHITTEST) return HTTRANSPARENT;   // mouse input belongs to the game window underneath
        if (m == WM_ERASEBKGND) return 1;
        if (m == WM_MOUSEACTIVATE) return MA_NOACTIVATE;
        return DefWindowProcW(h, m, w, l);
    }
    ID3D11Device* device = nullptr; ID3D11DeviceContext* ctx = nullptr; IDXGISwapChain1* swap = nullptr;
    ID3D11RenderTargetView* backRTV = nullptr;
    ID3D11Texture2D *sceneRT = nullptr, *cpuTex = nullptr, *paletteTex = nullptr, *legacyTex = nullptr, *horizonTex = nullptr,
        *staticAtlas = nullptr, *frameAtlas = nullptr, *uiTex = nullptr, *readStage = nullptr;
    ID3D11RenderTargetView* sceneRTV = nullptr;
    ID3D11ShaderResourceView *sceneSRV = nullptr, *cpuSRV = nullptr, *paletteSRV = nullptr, *legacySRV = nullptr, *horizonSRV = nullptr,
        *staticSRV = nullptr, *frameSRV = nullptr, *uiSRV = nullptr, *quadSRV = nullptr;
    ID3D11Buffer *quadBuf = nullptr, *frameCB = nullptr;
    ID3D11VertexShader *vsFull = nullptr, *vsQuad = nullptr;
    ID3D11PixelShader *psBackdrop = nullptr, *psQuad = nullptr, *psCopyCpu = nullptr, *psUi = nullptr, *psPresent = nullptr;
    ID3D11BlendState *blendOpaque = nullptr, *blendUi = nullptr, *blendQuad = nullptr;
    ID3D11RasterizerState* rast = nullptr;
    ID3D11SamplerState *pointSampler = nullptr, *linearSampler = nullptr;
    pD3DCompile compileFn = nullptr;
    void* factoryProc = nullptr;
    int sceneW = 0, sceneH = 0, backW = 0, backH = 0, horizonW = 0, horizonRows = 0;
    size_t quadCapacity = 0;
    std::unordered_map<uint64_t, Entry> staticMap;
    std::vector<Shelf> staticShelves, frameShelves;
    int staticTop = 0, frameTop = 0;
    bool staticFull = false;

    template<class T> static void rel(T*& p) { if (p) { p->Release(); p = nullptr; } }
    void say(const char* m) { if (log) log(m); }
    bool fail(const char* m, HRESULT hr = S_OK) {
        char msg[256]; std::snprintf(msg, sizeof msg, "Direct3D renderer disabled: %s (hr=0x%08lx)", m, static_cast<unsigned long>(hr));
        say(msg); failed = true; return false;
    }
    static uint32_t checksum(const uint8_t* p, int w, int h) {
        uint32_t s = 2166136261u; size_t n = size_t(w) * h, step = std::max<size_t>(1, n / 96);
        for (size_t i = 0; i < n; i += step) s = (s ^ p[i]) * 16777619u;
        return s ^ uint32_t(n);
    }
    bool allocate(std::vector<Shelf>& shelves, int& top, int aw, int ah, int w, int h, int& x, int& y) {
        for (auto& s : shelves)
            if (h <= s.h && s.x + w <= aw && h * 4 >= s.h) { x = s.x; y = s.y; s.x += w; return true; }
        if (top + h > ah) { if (&shelves == &staticShelves) staticFull = true; return false; }
        shelves.push_back({ top, h, w }); x = 0; y = top; top += h; return true;
    }
    bool createBackBufferView() {
        ID3D11Texture2D* back = nullptr;
        if (FAILED(swap->GetBuffer(0, __uuidof(ID3D11Texture2D), (void**)&back))) return fail("GetBuffer failed");
        HRESULT hr = device->CreateRenderTargetView(back, nullptr, &backRTV);
        back->Release();
        return SUCCEEDED(hr) || fail("back buffer view failed", hr);
    }
    ID3DBlob* compile(const char* src, const char* entry, const char* target) {
        ID3DBlob *code = nullptr, *err = nullptr;
        HRESULT hr = compileFn(src, std::strlen(src), "hd_gpu.hlsl", nullptr, nullptr, entry, target, D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &code, &err);
        if (FAILED(hr)) {
            char msg[600]; std::snprintf(msg, sizeof msg, "shader %s failed: %.400s", entry, err ? static_cast<const char*>(err->GetBufferPointer()) : "?");
            say(msg); if (err) err->Release(); return nullptr;
        }
        if (err) err->Release();
        return code;
    }
    bool createPipeline() {
        struct { const char* e; const char* t; void** out; bool vs; } list[] = {
            { "VSFull", "vs_5_0", (void**)&vsFull, true }, { "VSQuad", "vs_5_0", (void**)&vsQuad, true },
            { "PSBackdrop", "ps_5_0", (void**)&psBackdrop, false }, { "PSQuad", "ps_5_0", (void**)&psQuad, false },
            { "PSCopyCpu", "ps_5_0", (void**)&psCopyCpu, false }, { "PSUi", "ps_5_0", (void**)&psUi, false },
            { "PSPresent", "ps_5_0", (void**)&psPresent, false } };
        for (auto& s : list) {
            ID3DBlob* code = compile(hlsl(), s.e, s.t);
            if (!code) return fail("shader compilation failed");
            HRESULT hr = s.vs ? device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, (ID3D11VertexShader**)s.out)
                              : device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, (ID3D11PixelShader**)s.out);
            code->Release();
            if (FAILED(hr)) return fail("shader creation failed", hr);
        }
        D3D11_BUFFER_DESC bd{}; bd.ByteWidth = sizeof(GpuFrameInfo); bd.Usage = D3D11_USAGE_DEFAULT; bd.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        if (FAILED(device->CreateBuffer(&bd, nullptr, &frameCB))) return fail("constant buffer failed");
        D3D11_BLEND_DESC bl{}; bl.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
        if (FAILED(device->CreateBlendState(&bl, &blendOpaque))) return fail("blend state failed");
        bl.RenderTarget[0].BlendEnable = TRUE; bl.RenderTarget[0].SrcBlend = D3D11_BLEND_SRC_ALPHA; bl.RenderTarget[0].DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
        bl.RenderTarget[0].BlendOp = D3D11_BLEND_OP_ADD; bl.RenderTarget[0].SrcBlendAlpha = D3D11_BLEND_ONE; bl.RenderTarget[0].DestBlendAlpha = D3D11_BLEND_ZERO;
        bl.RenderTarget[0].BlendOpAlpha = D3D11_BLEND_OP_ADD;
        if (FAILED(device->CreateBlendState(&bl, &blendUi))) return fail("UI blend state failed");
        if (FAILED(device->CreateBlendState(&bl, &blendQuad))) return fail("quad blend state failed");
        D3D11_RASTERIZER_DESC rd{}; rd.FillMode = D3D11_FILL_SOLID; rd.CullMode = D3D11_CULL_NONE; rd.DepthClipEnable = TRUE;
        if (FAILED(device->CreateRasterizerState(&rd, &rast))) return fail("rasterizer state failed");
        D3D11_SAMPLER_DESC sd{}; sd.Filter = D3D11_FILTER_MIN_MAG_MIP_POINT; sd.AddressU = sd.AddressV = sd.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
        if (FAILED(device->CreateSamplerState(&sd, &pointSampler))) return fail("sampler failed");
        sd.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
        if (FAILED(device->CreateSamplerState(&sd, &linearSampler))) return fail("sampler failed");
        return true;
    }
    bool tex2d(ID3D11Texture2D*& t, ID3D11ShaderResourceView*& v, int w, int h, DXGI_FORMAT f) {
        D3D11_TEXTURE2D_DESC td{}; td.Width = w; td.Height = h; td.MipLevels = 1; td.ArraySize = 1; td.Format = f;
        td.SampleDesc.Count = 1; td.Usage = D3D11_USAGE_DEFAULT; td.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        return SUCCEEDED(device->CreateTexture2D(&td, nullptr, &t)) && SUCCEEDED(device->CreateShaderResourceView(t, nullptr, &v));
    }
    bool createAtlases() {
        if (!tex2d(staticAtlas, staticSRV, STATIC_ATLAS, STATIC_ATLAS, DXGI_FORMAT_R8_UINT) ||
            !tex2d(frameAtlas, frameSRV, FRAME_ATLAS, FRAME_ATLAS, DXGI_FORMAT_R8_UINT) ||
            !tex2d(paletteTex, paletteSRV, 256, 1, DXGI_FORMAT_B8G8R8A8_UNORM) ||
            !tex2d(legacyTex, legacySRV, 640, 480, DXGI_FORMAT_R8_UINT) ||
            !tex2d(uiTex, uiSRV, 640, 480, DXGI_FORMAT_B8G8R8A8_UNORM)) return fail("texture creation failed");
        return growQuads(2048);
    }
    bool growQuads(size_t count) {
        rel(quadBuf); rel(quadSRV);
        quadCapacity = std::max<size_t>(count, quadCapacity * 2);
        D3D11_BUFFER_DESC bd{}; bd.ByteWidth = UINT(quadCapacity * sizeof(GpuQuad)); bd.Usage = D3D11_USAGE_DYNAMIC;
        bd.BindFlags = D3D11_BIND_SHADER_RESOURCE; bd.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
        bd.MiscFlags = D3D11_RESOURCE_MISC_BUFFER_STRUCTURED; bd.StructureByteStride = sizeof(GpuQuad);
        if (FAILED(device->CreateBuffer(&bd, nullptr, &quadBuf))) return fail("quad buffer failed");
        D3D11_SHADER_RESOURCE_VIEW_DESC sv{}; sv.Format = DXGI_FORMAT_UNKNOWN; sv.ViewDimension = D3D11_SRV_DIMENSION_BUFFER;
        sv.Buffer.FirstElement = 0; sv.Buffer.NumElements = UINT(quadCapacity);
        if (FAILED(device->CreateShaderResourceView(quadBuf, &sv, &quadSRV))) return fail("quad view failed");
        return true;
    }
    void updateFrameInfo(const GpuFrameInfo& info) {
        lastInfo = info;
        ctx->UpdateSubresource(frameCB, 0, nullptr, &info, 0, 0);
        ctx->VSSetConstantBuffers(0, 1, &frameCB); ctx->PSSetConstantBuffers(0, 1, &frameCB);
    }
    void bindScene() {
        ctx->OMSetRenderTargets(1, &sceneRTV, nullptr);
        D3D11_VIEWPORT vp{ 0, 0, float(sceneW), float(sceneH), 0, 1 };
        ctx->RSSetViewports(1, &vp);
        ctx->RSSetState(rast);
        ctx->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        ctx->IASetInputLayout(nullptr);
        ctx->VSSetShader(vsFull, nullptr, 0);
        ctx->PSSetSamplers(0, 1, &pointSampler);
    }

    static const char* hlsl() {
        return R"HLSL(
cbuffer Frame : register(b0) {
    float viewW, viewH, sceneScale, halfW;
    int horizonW, horizonRows, horizonScroll, horizonY;
    float uiScale, uiOx, uiOy, uiFlat;
    float uiX0, uiX1, uiY0, uiY1;
    float backdropFilter, presentW, presentH, padC;
};
struct Quad { float4 px; float4 py; float4 pu; float4 pv; uint4 info; };
Texture2D<uint>   legacyTex  : register(t0);
Texture2D<uint>   horizonTex : register(t1);
Texture2D<float4> paletteTex : register(t2);
Texture2D<uint>   staticTex  : register(t3);
Texture2D<uint>   frameTex   : register(t4);
StructuredBuffer<Quad> quads : register(t5);
Texture2D<float4> uiTex      : register(t6);
Texture2D<float4> srcTex     : register(t7);
SamplerState pointSamp : register(s0);

float4 pal(uint i) { return float4(paletteTex.Load(int3(i, 0, 0)).rgb, 1); }
int wrapi(int x, int n) { int r = x % n; return r < 0 ? r + n : r; }

float4 VSFull(uint id : SV_VertexID) : SV_Position {
    float2 p = float2((id << 1) & 2, id & 2);
    return float4(p * float2(2, -2) + float2(-1, 1), 0, 1);
}

float4 PSPresent(float4 pos : SV_Position) : SV_Target {
    // The back buffer can differ from the scene target for a moment (window just resized): always stretch the whole scene over it.
    return float4(srcTex.SampleLevel(pointSamp, pos.xy / float2(presentW, presentH), 0).rgb, 1);
}
float4 PSCopyCpu(float4 pos : SV_Position) : SV_Target {
    return float4(srcTex.Load(int3(int2(pos.xy), 0)).rgb, 1);
}

uint backdropIndex(int bx, int oldY) {
    oldY = clamp(oldY, 0, 479);
    int hy = oldY - horizonY;
    if (horizonRows > 0 && hy >= 0 && hy < horizonRows)
        return horizonTex.Load(int3(wrapi(bx - horizonScroll, horizonW), hy, 0)).r;
    return legacyTex.Load(int3(clamp(bx, 0, 639), oldY, 0)).r;
}
float4 PSBackdrop(float4 pos : SV_Position) : SV_Target {
    int x = int(pos.x), y = int(pos.y);
    float fy = (y + 0.5) / sceneScale;
    float fx = (x + 0.5 - halfW) / sceneScale + 320.0;
    if (backdropFilter < 0.5)
        return pal(backdropIndex(int(floor(fx)), int(floor(fy))));
    // Bilinear sky / panorama (texture filtering also covers the backdrop)
    float sx = fx - 0.5, sy = fy - 0.5;
    int ix = int(floor(sx)), iy = int(floor(sy));
    float wx = sx - ix, wy = sy - iy;
    float3 c00 = pal(backdropIndex(ix, iy)).rgb, c10 = pal(backdropIndex(ix + 1, iy)).rgb;
    float3 c01 = pal(backdropIndex(ix, iy + 1)).rgb, c11 = pal(backdropIndex(ix + 1, iy + 1)).rgb;
    return float4(lerp(lerp(c00, c10, wx), lerp(c01, c11, wx), wy), 1);
}

struct QOut { float4 pos : SV_Position; nointerpolation uint q : QUAD; };
QOut VSQuad(uint vid : SV_VertexID, uint iid : SV_InstanceID) {
    Quad q = quads[iid];
    float x0 = floor(min(min(q.px.x, q.px.y), min(q.px.z, q.px.w))), x1 = ceil(max(max(q.px.x, q.px.y), max(q.px.z, q.px.w))) + 1;
    float y0 = floor(min(min(q.py.x, q.py.y), min(q.py.z, q.py.w))), y1 = ceil(max(max(q.py.x, q.py.y), max(q.py.z, q.py.w))) + 1;
    float2 c[6] = { float2(x0, y0), float2(x1, y0), float2(x0, y1), float2(x0, y1), float2(x1, y0), float2(x1, y1) };
    float2 p = c[vid];
    QOut o;
    o.pos = float4(p.x / viewW * 2 - 1, 1 - p.y / viewH * 2, 0, 1);
    o.q = iid;
    return o;
}

struct Cover { bool ok; float u; float v; float2 dx; float2 dy; };   // dx, dy: texels per output pixel
Cover evalQuad(Quad q, float sx, float sy) {
    float ex[4] = { q.px.x, q.px.y, q.px.z, q.px.w };
    float ey[4] = { q.py.x, q.py.y, q.py.z, q.py.w };
    float eu[4] = { q.pu.x, q.pu.y, q.pu.z, q.pu.w };
    float ev[4] = { q.pv.x, q.pv.y, q.pv.z, q.pv.w };
    float hx[4], hu[4], hv[4], su[4], sv[4]; int n = 0;
    [unroll] for (int e = 0; e < 4; ++e) {
        int b = (e + 1) & 3;
        float ay = ey[e], by = ey[b];
        if ((ay <= sy && by > sy) || (by <= sy && ay > sy)) {
            float t = (sy - ay) / (by - ay);
            hx[n] = ex[e] + (ex[b] - ex[e]) * t; hu[n] = eu[e] + (eu[b] - eu[e]) * t; hv[n] = ev[e] + (ev[b] - ev[e]) * t;
            su[n] = (eu[b] - eu[e]) / (by - ay); sv[n] = (ev[b] - ev[e]) / (by - ay);   // texels per output row along this edge
            ++n;
        }
    }
    // insertion sort by x
    [unroll] for (int a = 1; a < 4; ++a) {
        [unroll] for (int k = a; k > 0; --k) {
            if (k < n && hx[k] < hx[k - 1]) {
                float tx = hx[k]; hx[k] = hx[k - 1]; hx[k - 1] = tx;
                float tu = hu[k]; hu[k] = hu[k - 1]; hu[k - 1] = tu;
                float tv = hv[k]; hv[k] = hv[k - 1]; hv[k - 1] = tv;
                float tsu = su[k]; su[k] = su[k - 1]; su[k - 1] = tsu;
                float tsv = sv[k]; sv[k] = sv[k - 1]; sv[k - 1] = tsv;
            }
        }
    }
    Cover c; c.ok = false; c.u = 0; c.v = 0; c.dx = float2(0, 0); c.dy = float2(0, 0);
    [unroll] for (int s = 0; s < 4; s += 2) {
        if (s + 1 < n && hx[s + 1] > hx[s] && sx >= hx[s] && sx < hx[s + 1]) {
            float f = (sx - hx[s]) / (hx[s + 1] - hx[s]);
            c.u = hu[s] + (hu[s + 1] - hu[s]) * f; c.v = hv[s] + (hv[s + 1] - hv[s]) * f;
            c.dx = float2(hu[s + 1] - hu[s], hv[s + 1] - hv[s]) / (hx[s + 1] - hx[s]);
            c.dy = float2(su[s] + (su[s + 1] - su[s]) * f, sv[s] + (sv[s + 1] - sv[s]) * f);
            c.ok = true;
        }
    }
    return c;
}

// Vertical extent of the quad on the column x = sx: (top, bottom), or (1, 0) when the column misses it.
float2 vspan(Quad q, float sx) {
    float ex[4] = { q.px.x, q.px.y, q.px.z, q.px.w };
    float ey[4] = { q.py.x, q.py.y, q.py.z, q.py.w };
    float lo = 1e9, hi = -1e9;
    [unroll] for (int e = 0; e < 4; ++e) {
        int b = (e + 1) & 3;
        if ((ex[e] <= sx && ex[b] > sx) || (ex[b] <= sx && ex[e] > sx)) {
            float y = ey[e] + (ey[b] - ey[e]) * ((sx - ex[e]) / (ex[b] - ex[e]));
            lo = min(lo, y); hi = max(hi, y);
        }
    }
    return hi >= lo ? float2(lo, hi) : float2(1, 0);
}

uint texel(bool useFrame, int x, int y) {
    return useFrame ? frameTex.Load(int3(x, y, 0)).r : staticTex.Load(int3(x, y, 0)).r;
}
// Bilinear sample (transparent texels carry no weight). Returns rgb and the total weight.
float4 bilinearAt(float u, float v, int ax, int ay0, int tw, int th, bool useFrame, bool transparent) {
    float fu = u - 0.5, fv = v - 0.5;
    int ix = int(floor(fu)), iy = int(floor(fv));
    float wx = fu - ix, wy = fv - iy;
    int xa = clamp(ix, 0, tw - 1), xb = clamp(ix + 1, 0, tw - 1), ya = clamp(iy, 0, th - 1), yb = clamp(iy + 1, 0, th - 1);
    uint i00 = texel(useFrame, ax + xa, ay0 + ya), i10 = texel(useFrame, ax + xb, ay0 + ya);
    uint i01 = texel(useFrame, ax + xa, ay0 + yb), i11 = texel(useFrame, ax + xb, ay0 + yb);
    float w00 = (1 - wx) * (1 - wy), w10 = wx * (1 - wy), w01 = (1 - wx) * wy, w11 = wx * wy;
    if (transparent) { w00 *= (i00 != 0); w10 *= (i10 != 0); w01 *= (i01 != 0); w11 *= (i11 != 0); }
    float tot = w00 + w10 + w01 + w11;
    if (tot <= 0) return float4(0, 0, 0, 0);
    float3 c = (pal(i00).rgb * w00 + pal(i10).rgb * w10 + pal(i01).rgb * w01 + pal(i11).rgb * w11) / tot;
    return float4(c, tot);
}

float4 PSQuad(QOut i) : SV_Target {
    Quad q = quads[i.q];
    float sx = i.pos.x, sy = i.pos.y;
    uint flags = q.info.z;
    float qa = float((flags >> 8) & 255u) / 255.0;
    Cover c0 = evalQuad(q, sx, sy);
    // Strips thinner than one output row (water and road far away are drawn as many of them) cover only part of
    // a row: blend them in by that coverage instead of an all-or-nothing row, which produced streaks.
    if ((flags & 16u) != 0) {
        float2 sp = vspan(q, sx);
        if (sp.y > sp.x && sp.y - sp.x < 1.0) {
            float top = max(sp.x, sy - 0.5), bot = min(sp.y, sy + 0.5);
            if (bot <= top) discard;
            qa *= saturate(bot - top);
            c0 = evalQuad(q, sx, 0.5 * (top + bot));
        }
    }
    if (!c0.ok) discard;
    float u = c0.u, v = c0.v;
    if (flags & 4u) return float4(pal(q.info.w).rgb, qa);
    int ax = int(q.info.x & 0xffff), ay0 = int(q.info.x >> 16), tw = int(q.info.y & 0xffff), th = int(q.info.y >> 16);
    bool transparent = (flags & 1u) != 0, useFrame = (flags & 8u) != 0;
    int tx = clamp(int(u), 0, tw - 1), ty = clamp(int(v), 0, th - 1);
    uint idx = texel(useFrame, ax + tx, ay0 + ty);
    if (transparent && idx == 0) discard;            // nearest decides coverage (as on the CPU)
    if ((flags & 18u) == 0) return float4(pal(idx).rgb, qa);   // texture filtering off: plain nearest
    // Footprint of one output pixel in texels. Where a texture is squeezed (road and water towards the
    // horizon, far objects) one nearest/bilinear sample per pixel skips texels and shimmers or streaks, so
    // the footprint is averaged along its longer axis instead (anisotropic filtering).
    float2 dx = c0.dx, dy = c0.dy;
    float lx = length(dx), ly = length(dy);
    float2 d = lx > ly ? dx : dy;
    // A pixel can cover more than the whole texture (thin far-away strips such as the water): never reach past
    // the texture, average all of it instead.
    float2 ext = float2(max(tw - 2, 1), max(th - 2, 1));
    float shrink = min(1.0, min(ext.x / max(abs(d.x), 1e-4), ext.y / max(abs(d.y), 1e-4)));
    if (shrink < 1.0) { d *= shrink; u = tw * 0.5; v = th * 0.5; }
    float L = max(lx, ly) * shrink;
    if ((flags & 16u) != 0 && L > 1.25) {
        int n = int(min(ceil(L), 32.0));
        float3 sum = float3(0, 0, 0); float wsum = 0;
        for (int k = 0; k < n; ++k) {
            float t = (k + 0.5) / n - 0.5;
            float4 s = bilinearAt(u + d.x * t, v + d.y * t, ax, ay0, tw, th, useFrame, transparent);
            sum += s.rgb * s.a; wsum += s.a;
        }
        if (wsum <= 0) return float4(pal(idx).rgb, qa);
        return float4(sum / wsum, qa);
    }
    if ((flags & 2u) == 0) return float4(pal(idx).rgb, qa);
    float4 b = bilinearAt(u, v, ax, ay0, tw, th, useFrame, transparent);
    if (b.a <= 0) return float4(pal(0).rgb, qa);
    return float4(b.rgb, qa);
}

float4 PSUi(float4 pos : SV_Position) : SV_Target {
    if (pos.x < uiX0 || pos.x >= uiX1 || pos.y < uiY0 || pos.y >= uiY1) discard;
    float fx = (pos.x - uiOx) / uiScale - 0.5, fy = (pos.y - uiOy) / uiScale - 0.5;
    if (uiFlat > 0.5) {                            // large overlays stay flat
        float4 t = uiTex.Load(int3(clamp(int(floor(fx + 0.5)), 0, 639), clamp(int(floor(fy + 0.5)), 0, 479), 0));
        if (t.a < 0.5) discard;
        return float4(t.rgb, 1);
    }
    int ix = int(floor(fx)), iy = int(floor(fy));
    float wx = fx - ix, wy = fy - iy;
    int xa = clamp(ix, 0, 639), xb = clamp(ix + 1, 0, 639), ya = clamp(iy, 0, 479), yb = clamp(iy + 1, 0, 479);
    float4 t00 = uiTex.Load(int3(xa, ya, 0)), t10 = uiTex.Load(int3(xb, ya, 0)), t01 = uiTex.Load(int3(xa, yb, 0)), t11 = uiTex.Load(int3(xb, yb, 0));
    float m00 = t00.a > 0.5, m10 = t10.a > 0.5, m01 = t01.a > 0.5, m11 = t11.a > 0.5;
    if (m00 + m10 + m01 + m11 == 0) discard;
    float w00 = (1 - wx) * (1 - wy) * m00, w10 = wx * (1 - wy) * m10, w01 = (1 - wx) * wy * m01, w11 = wx * wy * m11;
    float total = w00 + w10 + w01 + w11;
    float3 col = (t00.rgb * w00 + t10.rgb * w10 + t01.rgb * w01 + t11.rgb * w11) / total;
    if (m00 * m10 * m01 * m11 > 0.5) return float4(col, 1);
    float alpha = saturate((total * 256 - 90) / 76);
    if (alpha <= 0) discard;
    return float4(col, alpha);
}
)HLSL";
    }
};

}  // namespace rrhd
