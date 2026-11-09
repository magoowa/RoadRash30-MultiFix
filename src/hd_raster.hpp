// SPDX-License-Identifier: PolyForm-Noncommercial-1.0.0
#pragma once
#include <algorithm>
#include <cmath>
#include <cstdint>
#include <cstring>

namespace rrhd {
struct Vertex { double x, y, u = 0, v = 0; };
struct Texture {
    const uint8_t* pixels = nullptr;
    int width = 0, height = 0;
    bool transparent = false;
};

// Scan-convert the original ordered quads at the destination resolution.
// No 480-row scratch arrays, 16-bit screen coordinates or enlarged old frame.
inline void quad(uint8_t* out, int width, int height, const Vertex (&p)[4],
                 const Texture& tex, uint8_t color, int clipTop=0, int clipBottom=0x7fffffff,
                 uint16_t* owner=nullptr, uint16_t id=0) {
    double lo = p[0].y, hi = lo;
    for (const auto& v : p) { lo = std::min(lo, v.y); hi = std::max(hi, v.y); }
    clipBottom=std::min(height,clipBottom);
    if (!std::isfinite(lo) || !std::isfinite(hi) || hi < clipTop || lo >= clipBottom) return;
    int begin = int(std::max(double(clipTop), std::ceil(lo - 0.5)));
    int end = int(std::min(double(clipBottom), std::ceil(hi - 0.5)));
    for (int y = begin; y < end; ++y) {
        Vertex hits[4]; int n = 0;
        const double sampleY = y + 0.5;
        for (int i = 0; i < 4; ++i) {
            const auto& a = p[i]; const auto& b = p[(i + 1) & 3];
            if ((a.y <= sampleY && b.y > sampleY) || (b.y <= sampleY && a.y > sampleY)) {
                const double t = (sampleY - a.y) / (b.y - a.y);
                hits[n++] = {a.x + (b.x-a.x)*t, sampleY,
                             a.u + (b.u-a.u)*t, a.v + (b.v-a.v)*t};
            }
        }
        std::sort(hits, hits + n, [](const Vertex& a, const Vertex& b) { return a.x < b.x; });
        for (int i = 0; i + 1 < n; i += 2) {
            const auto& a = hits[i]; const auto& b = hits[i+1];
            if (!(b.x > a.x) || b.x < 0 || a.x >= width) continue;
            int x0 = int(std::max(0.0, std::ceil(a.x - 0.5)));
            int x1 = int(std::min(double(width), std::ceil(b.x - 0.5)));
            uint8_t* row = out + size_t(y) * width;
            if (!tex.pixels) {
                if (x1 > x0) {
                    std::memset(row + x0, color, x1-x0);
                    if (owner) std::fill(owner + size_t(y)*width + x0, owner + size_t(y)*width + x1, uint16_t(0));
                }
                continue;
            }
            const double du = (b.u-a.u)/(b.x-a.x), dv = (b.v-a.v)/(b.x-a.x);
            double u = a.u + (x0 + 0.5 - a.x)*du, v = a.v + (x0 + 0.5 - a.x)*dv;
            for (int x = x0; x < x1; ++x, u += du, v += dv) {
                // Truncation equals floor for in-range texture coordinates;
                // negatives clamp to zero either way. Avoid two library floor
                // calls for every covered pixel in this 32-bit executable.
                int tx = std::clamp(int(u), 0, tex.width-1);
                int ty = std::clamp(int(v), 0, tex.height-1);
                uint8_t index = tex.pixels[size_t(ty)*tex.width + tx];
                if (index || !tex.transparent) {
                    row[x] = index;
                    if (owner) owner[size_t(y)*width + x] = id;
                }
            }
        }
    }
}

// Packed 8-bit-per-channel linear interpolation, weight 0..256.
inline uint32_t lerp8(uint32_t a, uint32_t b, unsigned w) {
    uint32_t rb = (((a & 0xff00ffu) * (256 - w) + (b & 0xff00ffu) * w) >> 8) & 0xff00ffu;
    uint32_t g  = (((a & 0x00ff00u) * (256 - w) + (b & 0x00ff00u) * w) >> 8) & 0x00ff00u;
    return rb | g;
}

// Second pass for textured quads: the same scan conversion as quad(), but only
// pixels that quad() attributed to this quad (owner == id) are rewritten, with
// a bilinear texture sample converted through the palette. Coverage and draw
// order therefore stay exactly those of the indexed pass; only the colour of a
// texel lookup is smoothed. Transparent texels (index 0) do not contribute.
inline void quadFiltered(uint32_t* out, const uint16_t* owner, uint16_t id, int width, int height,
                         const Vertex (&p)[4], const Texture& tex, const uint32_t* palette,
                         int clipTop=0, int clipBottom=0x7fffffff) {
    if (!tex.pixels || tex.width <= 0 || tex.height <= 0) return;
    double lo = p[0].y, hi = lo;
    for (const auto& v : p) { lo = std::min(lo, v.y); hi = std::max(hi, v.y); }
    clipBottom = std::min(height, clipBottom);
    if (!std::isfinite(lo) || !std::isfinite(hi) || hi < clipTop || lo >= clipBottom) return;
    int begin = int(std::max(double(clipTop), std::ceil(lo - 0.5)));
    int end = int(std::min(double(clipBottom), std::ceil(hi - 0.5)));
    const int tw = tex.width, th = tex.height;
    for (int y = begin; y < end; ++y) {
        Vertex hits[4]; int n = 0;
        const double sampleY = y + 0.5;
        for (int i = 0; i < 4; ++i) {
            const auto& a = p[i]; const auto& b = p[(i + 1) & 3];
            if ((a.y <= sampleY && b.y > sampleY) || (b.y <= sampleY && a.y > sampleY)) {
                const double t = (sampleY - a.y) / (b.y - a.y);
                hits[n++] = {a.x + (b.x-a.x)*t, sampleY, a.u + (b.u-a.u)*t, a.v + (b.v-a.v)*t};
            }
        }
        std::sort(hits, hits + n, [](const Vertex& a, const Vertex& b) { return a.x < b.x; });
        for (int i = 0; i + 1 < n; i += 2) {
            const auto& a = hits[i]; const auto& b = hits[i+1];
            if (!(b.x > a.x) || b.x < 0 || a.x >= width) continue;
            int x0 = int(std::max(0.0, std::ceil(a.x - 0.5)));
            int x1 = int(std::min(double(width), std::ceil(b.x - 0.5)));
            const uint16_t* own = owner + size_t(y) * width;
            uint32_t* row = out + size_t(y) * width;
            // 16.16 fixed point with a +1024 texel bias so that truncation is floor.
            const double du = (b.u-a.u)/(b.x-a.x), dv = (b.v-a.v)/(b.x-a.x);
            const double u0 = a.u + (x0 + 0.5 - a.x)*du - 0.5 + 1024.0, v0 = a.v + (x0 + 0.5 - a.x)*dv - 0.5 + 1024.0;
            int64_t U = int64_t(u0 * 65536.0), V = int64_t(v0 * 65536.0);
            const int64_t dU = int64_t(du * 65536.0), dV = int64_t(dv * 65536.0);
            for (int x = x0; x < x1; ++x, U += dU, V += dV) {
                if (own[x] != id) continue;
                const int ix = int(U >> 16) - 1024, iy = int(V >> 16) - 1024;
                const unsigned wx = unsigned(U >> 8) & 255u, wy = unsigned(V >> 8) & 255u;
                int xa = ix, xb = ix + 1, ya = iy, yb = iy + 1;
                if (unsigned(ix) >= unsigned(tw - 1) || unsigned(iy) >= unsigned(th - 1)) {
                    xa = std::clamp(xa, 0, tw-1); xb = std::clamp(xb, 0, tw-1);
                    ya = std::clamp(ya, 0, th-1); yb = std::clamp(yb, 0, th-1);
                }
                const uint8_t* r0 = tex.pixels + size_t(ya)*tw; const uint8_t* r1 = tex.pixels + size_t(yb)*tw;
                const uint8_t i00 = r0[xa], i10 = r0[xb], i01 = r1[xa], i11 = r1[xb];
                if (!tex.transparent || (i00 && i10 && i01 && i11)) {
                    row[x] = lerp8(lerp8(palette[i00], palette[i10], wx), lerp8(palette[i01], palette[i11], wx), wy);
                } else {
                    const unsigned w00 = (256-wx)*(256-wy)*(i00 != 0), w10 = wx*(256-wy)*(i10 != 0);
                    const unsigned w01 = (256-wx)*wy*(i01 != 0), w11 = wx*wy*(i11 != 0);
                    const unsigned total = w00 + w10 + w01 + w11;
                    if (!total) continue;
                    unsigned r = 0, g = 0, bl = 0;
                    const unsigned ws[4] = {w00, w10, w01, w11};
                    const uint32_t cs[4] = {palette[i00], palette[i10], palette[i01], palette[i11]};
                    for (int k = 0; k < 4; ++k) {
                        r += ((cs[k] >> 16) & 255) * ws[k]; g += ((cs[k] >> 8) & 255) * ws[k]; bl += (cs[k] & 255) * ws[k];
                    }
                    row[x] = ((r / total) << 16) | ((g / total) << 8) | (bl / total);
                }
            }
        }
    }
}
}
