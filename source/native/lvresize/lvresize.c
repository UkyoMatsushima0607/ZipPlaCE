/*
 * ZipPla CE: LongVectorImageResizer.core（ガンマ 1.0 の経路）のネイティブ版。
 *
 * C# の core と同じ計算（64bit の整数に 3 チャンネル（24bpp）・4 画素（8bpp）を詰めた積和、SWAR）を、添字の計算まで同じに写した。
 * 内側の積和（横の畳み込み Σ cx[i] * buff1d[i]、縦の畳み込み Σ cy[j] * buff2d[j]）だけを、実行時に CPU を見て
 *   - AVX-512（AVX512F + AVX512DQ の vpmullq で 64bit の積を 8 個ずつ）
 *   - AVX2（vpmuludq 3 回で 64bit の積を 4 個ずつ）
 *   - スカラー
 * から選ぶ。どれも 2^64 を法とする足し算・掛け算だけなので、足す順番が変わっても結果は C# の版とビット単位で同じになる。
 *
 * ビルド: Windows は tools/build-lvresize.ps1（MSVC）。Linux（テスト用）は gcc -O2 -shared -fPIC -o liblvresize.so lvresize.c
 * ライセンス: ZipPla と同じく AGPL v3
 */
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include <immintrin.h>

#ifdef _MSC_VER
#include <intrin.h>
#define LVR_EXPORT __declspec(dllexport)
#define LVR_TARGET_AVX2
#define LVR_TARGET_AVX512
#define LVR_INLINE static __forceinline
#else
#include <cpuid.h>
#define LVR_EXPORT __attribute__((visibility("default")))
#define LVR_TARGET_AVX2 __attribute__((target("avx2")))
#define LVR_TARGET_AVX512 __attribute__((target("avx512f,avx512dq")))
#define LVR_INLINE static inline __attribute__((always_inline))
#endif

typedef int (*lvr_cancel_fn)(void);

/* ---- CPU の判定 ---- */

static void lvr_cpuid(int leaf, int sub, int r[4])
{
#ifdef _MSC_VER
    __cpuidex(r, leaf, sub);
#else
    unsigned a, b, c, d;
    __cpuid_count(leaf, sub, a, b, c, d);
    r[0] = (int)a; r[1] = (int)b; r[2] = (int)c; r[3] = (int)d;
#endif
}

static uint64_t lvr_xgetbv0(void)
{
#ifdef _MSC_VER
    return _xgetbv(0);
#else
    unsigned lo, hi;
    __asm__ volatile("xgetbv" : "=a"(lo), "=d"(hi) : "c"(0));
    return ((uint64_t)hi << 32) | lo;
#endif
}

/* 0: スカラー、1: AVX2、2: AVX-512。OS が YMM・ZMM の状態を保存するかも見る */
static int lvr_detect(void)
{
    int r[4];
    lvr_cpuid(0, 0, r);
    if (r[0] < 7) return 0;
    lvr_cpuid(1, 0, r);
    const int osxsave = (r[2] >> 27) & 1, avx = (r[2] >> 28) & 1;
    if (!osxsave || !avx) return 0;
    const uint64_t xcr0 = lvr_xgetbv0();
    if ((xcr0 & 0x6) != 0x6) return 0; /* XMM・YMM */
    lvr_cpuid(7, 0, r);
    const int avx2 = (r[1] >> 5) & 1, avx512f = (r[1] >> 16) & 1, avx512dq = (r[1] >> 17) & 1;
    if (!avx2) return 0;
    if (avx512f && avx512dq && (xcr0 & 0xE6) == 0xE6) return 2; /* opmask・ZMM の上位・ZMM16〜31 */
    return 1;
}

LVR_EXPORT int lvr_get_isa(void) { return lvr_detect(); }

/* ---- 積和 ---- */

LVR_INLINE uint64_t dot_scalar(const uint64_t *c, const uint64_t *b, int64_t n, uint64_t v)
{
    for (int64_t i = 0; i < n; i++) v += c[i] * b[i];
    return v;
}

LVR_TARGET_AVX2 LVR_INLINE __m256i mul64_avx2(__m256i a, __m256i b)
{
    const __m256i lo = _mm256_mul_epu32(a, b);
    const __m256i t = _mm256_add_epi64(_mm256_mul_epu32(_mm256_srli_epi64(a, 32), b), _mm256_mul_epu32(a, _mm256_srli_epi64(b, 32)));
    return _mm256_add_epi64(lo, _mm256_slli_epi64(t, 32));
}

LVR_TARGET_AVX2 LVR_INLINE uint64_t dot_avx2(const uint64_t *c, const uint64_t *b, int64_t n, uint64_t v)
{
    int64_t i = 0;
    if (n >= 4)
    {
        __m256i acc = _mm256_setzero_si256();
        for (; i + 4 <= n; i += 4)
            acc = _mm256_add_epi64(acc, mul64_avx2(_mm256_loadu_si256((const __m256i *)(c + i)), _mm256_loadu_si256((const __m256i *)(b + i))));
        const __m128i s = _mm_add_epi64(_mm256_castsi256_si128(acc), _mm256_extracti128_si256(acc, 1));
        v += (uint64_t)_mm_cvtsi128_si64(s) + (uint64_t)_mm_extract_epi64(s, 1);
    }
    for (; i < n; i++) v += c[i] * b[i];
    return v;
}

LVR_TARGET_AVX512 LVR_INLINE uint64_t dot_avx512(const uint64_t *c, const uint64_t *b, int64_t n, uint64_t v)
{
    __m512i acc = _mm512_setzero_si512();
    int64_t i = 0;
    for (; i + 8 <= n; i += 8)
        acc = _mm512_add_epi64(acc, _mm512_mullo_epi64(_mm512_loadu_si512((const void *)(c + i)), _mm512_loadu_si512((const void *)(b + i))));
    if (i < n)
    {
        const __mmask8 m = (__mmask8)((1u << (n - i)) - 1);
        acc = _mm512_add_epi64(acc, _mm512_mullo_epi64(_mm512_maskz_loadu_epi64(m, c + i), _mm512_maskz_loadu_epi64(m, b + i)));
    }
    return v + (uint64_t)_mm512_reduce_add_epi64(acc);
}

/* ---- 縦の積和: 出力の n 画素ぶん、v[k] = init + Σ_j cy[j] * rows[j][k]（j の順は C# と同じ） ---- */

LVR_INLINE void vdot_scalar(uint64_t *out, const uint64_t *const *rows, const uint64_t *cy, int64_t taps, int64_t n, uint64_t init)
{
    for (int64_t k = 0; k < n; k++)
    {
        uint64_t v = init;
        for (int64_t j = 0; j < taps; j++) v += cy[j] * rows[j][k];
        out[k] = v;
    }
}

LVR_TARGET_AVX2 LVR_INLINE void vdot_avx2(uint64_t *out, const uint64_t *const *rows, const uint64_t *cy, int64_t taps, int64_t n, uint64_t init)
{
    int64_t k = 0;
    const __m256i vinit = _mm256_set1_epi64x((long long)init);
    for (; k + 8 <= n; k += 8)
    {
        __m256i a0 = vinit, a1 = vinit;
        for (int64_t j = 0; j < taps; j++)
        {
            const __m256i w = _mm256_set1_epi64x((long long)cy[j]);
            a0 = _mm256_add_epi64(a0, mul64_avx2(w, _mm256_loadu_si256((const __m256i *)(rows[j] + k))));
            a1 = _mm256_add_epi64(a1, mul64_avx2(w, _mm256_loadu_si256((const __m256i *)(rows[j] + k + 4))));
        }
        _mm256_storeu_si256((__m256i *)(out + k), a0);
        _mm256_storeu_si256((__m256i *)(out + k + 4), a1);
    }
    for (; k < n; k++)
    {
        uint64_t v = init;
        for (int64_t j = 0; j < taps; j++) v += cy[j] * rows[j][k];
        out[k] = v;
    }
}

LVR_TARGET_AVX512 LVR_INLINE void vdot_avx512(uint64_t *out, const uint64_t *const *rows, const uint64_t *cy, int64_t taps, int64_t n, uint64_t init)
{
    int64_t k = 0;
    const __m512i vinit = _mm512_set1_epi64((long long)init);
    for (; k + 16 <= n; k += 16)
    {
        __m512i a0 = vinit, a1 = vinit;
        for (int64_t j = 0; j < taps; j++)
        {
            const __m512i w = _mm512_set1_epi64((long long)cy[j]);
            a0 = _mm512_add_epi64(a0, _mm512_mullo_epi64(w, _mm512_loadu_si512((const void *)(rows[j] + k))));
            a1 = _mm512_add_epi64(a1, _mm512_mullo_epi64(w, _mm512_loadu_si512((const void *)(rows[j] + k + 8))));
        }
        _mm512_storeu_si512((void *)(out + k), a0);
        _mm512_storeu_si512((void *)(out + k + 8), a1);
    }
    for (; k < n; k += 8)
    {
        const __mmask8 m = n - k >= 8 ? (__mmask8)0xFF : (__mmask8)((1u << (n - k)) - 1);
        __m512i a = vinit;
        for (int64_t j = 0; j < taps; j++)
            a = _mm512_add_epi64(a, _mm512_mullo_epi64(_mm512_set1_epi64((long long)cy[j]), _mm512_maskz_loadu_epi64(m, rows[j] + k)));
        _mm512_mask_storeu_epi64(out + k, m, a);
    }
}

/* ---- core（C# の LongVectorImageResizer.core の linearize == false の経路と同じ） ---- */

#define LVR_CORE_BODY(DOT, VDOT)                                                                                                    \
    uint8_t *dstScan0_ = dstScan0;                                                                                                  \
    const int64_t dstX0Bytes = x0 * dstBytesPerPixel;                                                                               \
    uint8_t *dstScan0X0 = dstScan0_ + dstX0Bytes;                                                                                   \
    const int64_t dstX1Bytes = (dstDataWidth - x0) * dstBytesPerPixel - 1;                                                          \
    uint8_t *dstScan0X0Mirror01 = dstScan0_ + dstX1Bytes;                                                                           \
    uint8_t *dstScan0Mirror = dstScan0_ + (dstDataHeight - 1) * dstStride;                                                          \
    uint8_t *dstScan0X0Mirror10 = dstScan0Mirror + dstX0Bytes;                                                                      \
    uint8_t *dstScan0X0Mirror11 = dstScan0Mirror + dstX1Bytes;                                                                      \
    const int dst24bpp = dstBytesPerPixel == 3;                                                                                     \
    const int64_t rx2 = (int64_t)rx * 2;                                                                                            \
    const int64_t ry2 = (int64_t)ry * 2;                                                                                            \
    const uint8_t *srcScan0_ = srcScan0;                                                                                            \
    const int64_t srcX0 = (x0 * txx + tx0) / txd;                                                                                   \
    const int64_t srcX0rx = srcX0 - rx;                                                                                             \
    const int64_t srcXStart = srcX0rx > 0 ? srcX0rx : 0;                                                                            \
    const int64_t srcBytesPerXSample = srcBytesPerPixel * srcXStep;                                                                 \
    const int64_t srcBytesPerXSample11 = srcBytesPerXSample - srcBytesPerPixel + 1;                                                 \
    const int srcXStepIsOne = srcXStep == 1;                                                                                        \
    const int64_t srcXStartBytes = srcXStart * srcBytesPerXSample;                                                                  \
    const int64_t srcX1 = ((x1 - 1) * txx + tx0) / txd + 1;                                                                         \
    const int64_t srcX1rx = srcX1 + rx;                                                                                             \
    const int64_t srcStrideStep = srcStride * srcYStep;                                                                             \
    const int64_t srcXU = (srcDataWidth - 1) / srcXStep + 1;                                                                        \
    const int64_t srcXStartBytesMirror = srcDataWidth * srcBytesPerPixel - srcXStart * srcBytesPerXSample - 1;                      \
    const int64_t srcXStop = srcX1rx < srcXU ? srcX1rx : srcXU;                                                                     \
    const int64_t srcY0 = (y0 * tyy + ty0) / tyd;                                                                                   \
    const int64_t srcY0ry = srcY0 - ry;                                                                                             \
    const int64_t srcY0ry2 = srcY0 - ry2;                                                                                           \
    const int src24bpp = srcBytesPerPixel == 3;                                                                                     \
    const int64_t srcYStart = srcY0ry > 0 ? srcY0ry : 0;                                                                            \
    const int64_t srcY1 = ((y1 - 1) * tyy + ty0) / tyd + 1;                                                                         \
    const int64_t srcY1ry = srcY1 + ry;                                                                                             \
    const int64_t srcYU = (srcDataHeight - 1) / srcYStep + 1;                                                                       \
    const uint8_t *srcScan0Mirror = srcScan0_ + srcDataHeight * srcStride - srcStrideStep;                                          \
    const int64_t srcYStop = srcY1ry < srcYU ? srcY1ry : srcYU;                                                                     \
    const int64_t srcYStop1 = srcYStop - 1;                                                                                         \
    int64_t dstY = y0;                                                                                                              \
    const int64_t iy0 = (2 - ry) * tyd - ty0 + tyy - 1;                                                                             \
    const uint64_t unt = 1ULL | 1ULL << 21 | 1ULL << 43;                                                                            \
    const uint64_t unt2 = 1ULL | 1ULL << 22 | 1ULL << 43;                                                                           \
    const int interBits = 20 - interShift;                                                                                          \
    const uint64_t interDesignRoundOffset = unt2 << 19 | unt << (interShift - 1);                                                   \
    const uint64_t interBitsMask = ~(~0ULL << (interBits + 1));                                                                     \
    const uint64_t interMask = unt * interBitsMask | unt2 * interBitsMask;                                                          \
    const uint64_t interSignOffset = unt2 << (interBits - 1);                                                                       \
    uint64_t *buff1d_srcX0rx = buff1d - srcX0rx;                                                                                    \
    uint64_t *buff1d_srcX0 = buff1d - srcX0;                                                                                        \
    const int64_t dstWidth = x1 - x0;                                                                                               \
    /* srcX は行によらないので表にする（以前は行ごと・画素ごとに 64bit の割り算をしていた）。縦の作業領域は rows・out */            \
    int64_t *srcXTable = (int64_t *)malloc(sizeof(int64_t) * (size_t)(dstWidth > 0 ? dstWidth : 1));                                 \
    uint64_t *vout = (uint64_t *)malloc(sizeof(uint64_t) * (size_t)(dstWidth > 0 ? dstWidth : 1));                                   \
    const uint64_t **rows = (const uint64_t **)malloc(sizeof(uint64_t *) * (size_t)ry2);                                             \
    if (!srcXTable || !vout || !rows) { free(srcXTable); free(vout); free((void *)rows); return -2; }                               \
    for (int64_t dstX = x0; dstX < x1; dstX++) srcXTable[dstX - x0] = (dstX * txx + tx0) / txd;                                     \
    int lvr_result = 0;                                                                                                             \
    for (int64_t srcY = srcYStart; srcY < srcYStop; srcY++)                                                                         \
    {                                                                                                                               \
        if (cancel && cancel()) { lvr_result = 1; break; }                                                                          \
        const int64_t srcYStrideStep = srcY * srcStrideStep;                                                                        \
        const uint8_t *srcScanL = srcScan0_ + srcYStrideStep;                                                                       \
        const uint8_t *srcAdr = srcScanL + srcXStartBytes;                                                                          \
        uint64_t *buff2dRow = buff2d + ((srcY - srcY0ry2) % ry2) * dstWidth; /* C# は列の順、ここでは行の順 */                    \
        if (src24bpp)                                                                                                               \
        {                                                                                                                           \
            for (int64_t srcX = srcXStart; srcX < srcXStop; srcX++)                                                                 \
            {                                                                                                                       \
                buff1d_srcX0rx[srcX] = (uint64_t)srcAdr[0] | (uint64_t)srcAdr[1] << 22 | (uint64_t)srcAdr[2] << 43;                 \
                srcAdr += srcXStepIsOne ? 3 : 2 + srcBytesPerXSample11;                                                             \
            }                                                                                                                       \
            for (int64_t dstX = x0; dstX < x1; dstX++)                                                                              \
            {                                                                                                                       \
                const uint64_t v = DOT(cx0 + dstX * rx2, buff1d_srcX0 + srcXTable[dstX - x0], rx2, interDesignRoundOffset);         \
                buff2dRow[dstX - x0] = ((v >> interShift) & interMask) - interSignOffset;                                           \
            }                                                                                                                       \
        }                                                                                                                           \
        else                                                                                                                        \
        {                                                                                                                           \
            const uint8_t *srcAdr01 = srcScanL + srcXStartBytesMirror;                                                              \
            const uint8_t *srcScanL1 = srcScan0Mirror - srcYStrideStep;                                                             \
            const uint8_t *srcAdr10 = srcScanL1 + srcXStartBytes;                                                                   \
            const uint8_t *srcAdr11 = srcScanL1 + srcXStartBytesMirror;                                                             \
            for (int64_t srcX = srcXStart; srcX < srcXStop; srcX++)                                                                 \
            {                                                                                                                       \
                buff1d_srcX0rx[srcX] = (uint64_t)*srcAdr | (uint64_t)*srcAdr01 << 16 | (uint64_t)*srcAdr10 << 32 | (uint64_t)*srcAdr11 << 48; \
                srcAdr += srcBytesPerXSample;                                                                                       \
                srcAdr01 -= srcBytesPerXSample;                                                                                     \
                srcAdr10 += srcBytesPerXSample;                                                                                     \
                srcAdr11 -= srcBytesPerXSample;                                                                                     \
            }                                                                                                                       \
            for (int64_t dstX = x0; dstX < x1; dstX++)                                                                              \
            {                                                                                                                       \
                const uint64_t v = DOT(cx0 + dstX * rx2, buff1d_srcX0 + srcXTable[dstX - x0], rx2, 0x4040404040404040ULL);          \
                buff2dRow[dstX - x0] = ((v >> 7) & 0x01ff01ff01ff01ffULL) - 0x0080008000800080ULL;                                  \
            }                                                                                                                       \
        }                                                                                                                           \
        const int64_t dstYBound = srcY < srcYStop1 ? ((srcY * tyd + iy0) / tyy < y1 ? (srcY * tyd + iy0) / tyy : y1) : y1;          \
        for (; dstY < dstYBound; dstY++)                                                                                            \
        {                                                                                                                           \
            const int64_t dstYStride = dstY * dstStride;                                                                            \
            uint8_t *dstAdr = dstScan0X0 + dstYStride;                                                                              \
            uint8_t *dstAdr01 = dstScan0X0Mirror01 + dstYStride;                                                                    \
            uint8_t *dstAdr10 = dstScan0X0Mirror10 - dstYStride;                                                                    \
            uint8_t *dstAdr11 = dstScan0X0Mirror11 - dstYStride;                                                                    \
            const uint64_t *cy = cy0 + dstY * ry2;                                                                                  \
            const int64_t srcYsMod = ((dstY * tyy + ty0) / tyd - srcY0ry) % ry2;                                                    \
            /* C# の buff2dVert[j]（j < jLoop）・(buff2dVert - ry2)[j]（j >= jLoop）は、リングの (srcYsMod + j) % ry2 行目 */             \
            for (int64_t j = 0; j < ry2; j++) rows[j] = buff2d + ((srcYsMod + j) % ry2) * dstWidth;                                 \
            VDOT(vout, rows, cy, ry2, dstWidth, src24bpp ? ((unt2 << 11) + (unt2 << 19)) : 0x4040404040404040ULL);                   \
            for (int64_t dstX = x0; dstX < x1; dstX++)                                                                              \
            {                                                                                                                       \
                const uint64_t v = vout[dstX - x0];                                                                                 \
                uint64_t s;                                                                                                         \
                if (src24bpp)                                                                                                       \
                {                                                                                                                   \
                    s = v & (3ULL << 19);                                                                                           \
                    *dstAdr++ = (uint8_t)(s == 0 ? 0 : s == (3ULL << 19) ? 255 : ((v >> 12) - 128));                                \
                    s = v & (3ULL << 41);                                                                                           \
                    *dstAdr++ = (uint8_t)(s == 0 ? 0 : s == (3ULL << 41) ? 255 : ((v >> 34) - 128));                                \
                    s = v & (3ULL << 62);                                                                                           \
                    *dstAdr++ = (uint8_t)(s == 0 ? 0 : s == (3ULL << 62) ? 255 : ((v >> 55) - 128));                                \
                }                                                                                                                   \
                else if (dst24bpp)                                                                                                  \
                {                                                                                                                   \
                    uint8_t vb;                                                                                                     \
                    s = v & 3ULL << 14;                                                                                             \
                    vb = (uint8_t)(s == 0 ? 0 : s == (3ULL << 14) ? 255 : ((v >> 7) - 128));                                        \
                    *dstAdr++ = vb; *dstAdr++ = vb; *dstAdr++ = vb;                                                                 \
                    s = v & 3ULL << 30;                                                                                             \
                    vb = (uint8_t)(s == 0 ? 0 : s == (3ULL << 30) ? 255 : ((v >> 23) - 128));                                       \
                    *dstAdr01-- = vb; *dstAdr01-- = vb; *dstAdr01-- = vb;                                                           \
                    s = v & 3ULL << 46;                                                                                             \
                    vb = (uint8_t)(s == 0 ? 0 : s == (3ULL << 46) ? 255 : ((v >> 39) - 128));                                       \
                    *dstAdr10++ = vb; *dstAdr10++ = vb; *dstAdr10++ = vb;                                                           \
                    s = v & 3ULL << 62;                                                                                             \
                    vb = (uint8_t)(s == 0 ? 0 : s == (3ULL << 62) ? 255 : ((v >> 55) - 128));                                       \
                    *dstAdr11-- = vb; *dstAdr11-- = vb; *dstAdr11-- = vb;                                                           \
                }                                                                                                                   \
                else                                                                                                                \
                {                                                                                                                   \
                    s = v & 3ULL << 14;                                                                                             \
                    *dstAdr++ = (uint8_t)(s == 0 ? 0 : s == (3ULL << 14) ? 255 : ((v >> 7) - 128));                                 \
                    s = v & 3ULL << 30;                                                                                             \
                    *dstAdr01-- = (uint8_t)(s == 0 ? 0 : s == (3ULL << 30) ? 255 : ((v >> 23) - 128));                              \
                    s = v & 3ULL << 46;                                                                                             \
                    *dstAdr10++ = (uint8_t)(s == 0 ? 0 : s == (3ULL << 46) ? 255 : ((v >> 39) - 128));                              \
                    s = v & 3ULL << 62;                                                                                             \
                    *dstAdr11-- = (uint8_t)(s == 0 ? 0 : s == (3ULL << 62) ? 255 : ((v >> 55) - 128));                              \
                }                                                                                                                   \
            }                                                                                                                       \
        }                                                                                                                           \
    }                                                                                                                               \
    free(srcXTable); free(vout); free((void *)rows);                                                                                \
    return lvr_result;

#define LVR_CORE_PARAMS                                                                                                              \
    uint8_t *dstScan0, int64_t x0, int64_t x1, int64_t y0, int64_t y1, int64_t dstStride, int64_t dstBytesPerPixel,                  \
    int64_t dstDataWidth, int64_t dstDataHeight,                                                                                     \
    const uint8_t *srcScan0, int64_t txx, int64_t tx0, int64_t txd, int64_t srcXStep, int64_t tyy, int64_t ty0, int64_t tyd,         \
    int64_t srcYStep, int64_t srcStride, int64_t srcBytesPerPixel, int64_t srcDataWidth, int64_t srcDataHeight,                     \
    int32_t rx, int32_t ry, const uint64_t *cx0, const uint64_t *cy0, uint64_t *buff1d, uint64_t *buff2d, int32_t interShift,       \
    lvr_cancel_fn cancel

static int core_scalar(LVR_CORE_PARAMS) { LVR_CORE_BODY(dot_scalar, vdot_scalar) }
LVR_TARGET_AVX2 static int core_avx2(LVR_CORE_PARAMS) { LVR_CORE_BODY(dot_avx2, vdot_avx2) }
LVR_TARGET_AVX512 static int core_avx512(LVR_CORE_PARAMS) { LVR_CORE_BODY(dot_avx512, vdot_avx512) }

static int lvr_isa = -1;

/*
 * 戻り値: 0 = 最後まで描いた、1 = cancel で止めた、-1 = 引数が正しくない、-2 = 作業領域を確保できなかった。
 * isa: -1 = CPU に合わせて選ぶ、0 = スカラー、1 = AVX2、2 = AVX-512（テスト用。CPU が対応していなければ使える中で一番上）
 */
LVR_EXPORT int lvr_core(LVR_CORE_PARAMS, int32_t isa)
{
    if (rx <= 0 || ry <= 0 || txd == 0 || tyd == 0 || tyy == 0 || srcXStep <= 0 || srcYStep <= 0) return -1;
    if ((srcBytesPerPixel != 3 && srcBytesPerPixel != 1) || (dstBytesPerPixel != 3 && dstBytesPerPixel != 1)) return -1;
    if (interShift < 1 || interShift > 19) return -1;
    if (lvr_isa < 0) lvr_isa = lvr_detect();
    if (isa < 0 || isa > lvr_isa) isa = lvr_isa;
#define LVR_ARGS dstScan0, x0, x1, y0, y1, dstStride, dstBytesPerPixel, dstDataWidth, dstDataHeight, srcScan0, txx, tx0, txd, srcXStep, \
    tyy, ty0, tyd, srcYStep, srcStride, srcBytesPerPixel, srcDataWidth, srcDataHeight, rx, ry, cx0, cy0, buff1d, buff2d, interShift, cancel
    switch (isa)
    {
    case 2: return core_avx512(LVR_ARGS);
    case 1: return core_avx2(LVR_ARGS);
    default: return core_scalar(LVR_ARGS);
    }
#undef LVR_ARGS
}
