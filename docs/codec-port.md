# V3 codec port and verification

The reference is `TinyEXR.Native/tinyexr` commit
`1b106618644dbf8a0935c2348ba51a2d863dd7c2`. No upstream sources are modified.
The new managed codec files were ported from the modular V3 C sources, rather
than relocated from the removed V1 implementation.

| Managed files under `TinyEXR.NET/Compression` | Pinned V3 reference |
| --- | --- |
| `BlockCodec`, `CodecContext`, `CodecWorkspace` | `src/exr_codec.c`, canonical layouts and sampling in `src/exr_core.c` |
| `RleCodec` | `src/exr_rle.c` |
| `ZipCodec` | `src/exr_zip.c` |
| `Pxr24Codec` | `src/exr_pxr24.c` |
| `PizCodec`, `PizWavelet`, `PizHuffman` | `src/exr_piz.c` bitmap/LUT, lifting, canonical Huffman and run coding |
| `B44Codec`, `B44Tables` | `src/exr_b44.c` pack/unpack, edge padding, flat blocks and perceptual tables |
| `ZlibBackend` | Bounded zlib backend contract; .NET `ZLibStream` and `SharpZipLib` implement DEFLATE |

The existing V3 HTJ2K implementation remains integrated through the block
reader/writer and pooled HTJ2K resources. ZIP/RLE use the existing SIMD
predictor/reorder implementation, whose selected and scalar paths are compared
in shared tests. Each reader/writer owns reusable codec scratch, including deep
count/sample transforms. Pixel data stays in V3 types throughout.

Ports retain the BSD-3-Clause attribution to Syoyo Fujita and TinyEXR authors.
The complete upstream license is in `THIRD-PARTY-NOTICES`, included in NuGet
packages. Original managed code remains MIT licensed.

## Reference tools and tests

`Test/NativeReference` builds the pinned C sources, rejects any different Git
revision, and disables unsupported ZSTD. It builds the upstream `parse_harness`,
`encode_harness`, `compare_exr` plus two test-only tools:

- `codec_oracle`: bidirectional canonical-block encode/decode.
- `file_oracle`: dumps every block, level, deep count and sample; reencodes with
  the native streaming API; exports both B44 perceptual tables.

`Test/TinyEXR.Interop` checks 288 codec scenarios and 161 generated file
scenarios. Codec cases include mixed HALF/FLOAT/UINT, sampling, negative origins,
odd dimensions, zeros, random bits, NaN, infinities, denormals, signed zero and
perceptually linear channels. Both 65,536-entry B44 tables must match exactly.
B44 perceptually linear zero follows the native table mapping (log zero maps
to zero, whose inverse table value is one); no legacy zero special case remains.

File checks cover all supported compressions, edge tiles, mip/rip levels,
sampled scanlines, multipart, deep and mixed parts. Lossless samples are compared
byte for byte. Lossy codec checks compare native quantization and both decoders
on the same encoded file; compressed file bytes need not match. The full-block
dump comparison supplements the upstream tool, which compares base flat images
only. Shared MSTest coverage includes pinned OpenEXR fixtures and malformed data.

```sh
cmake -S Test/NativeReference -B .cache/v3-reference -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build .cache/v3-reference
dotnet run --project Test/TinyEXR.Interop -p:SignAssemblyKey=false -- .cache/v3-reference
dotnet run --project Test/TinyEXR.Interop -p:SignAssemblyKey=false -p:LibraryFramework=netstandard2.1 -- .cache/v3-reference
```

On Windows, use a Visual Studio developer shell and select its Clang compiler
with `-DCMAKE_C_COMPILER=<path-to-clang.exe>`; the upstream C11 sources require
a C11 compiler. CI runs the matrix on Ubuntu, including an explicitly scalar
.NET run. No native library ships in the managed package.

## Native reference limitations

The pinned commit contains two independently reproduced deep encoder defects:

1. `src/exr_deep.c:exr_deep_encode_tile` loops channels before rows, whereas
   `exr_deep_decode_samples` expects channel planes within each row. Multiple
   channels and multiple tile rows therefore change samples even on native
   self-roundtrip.
2. `exr_deep_encode_block` accumulates counts across rows. The block decoder
   requires the cumulative table to reset for each row. Multi-row deep ZIP
   output can therefore fail the native decoder's size validation.

`FileChecks.VerifyPinnedDeepWriterLimitations` reproduces these defects and
requires their observed behavior. The matrix does not hide them by patching the
reference or changing managed wire layout. Of 161 generated file cases, 122
exercise native encoding in both directions; 39 multi-row deep cases exercise
managed encoding → native decoding, including every mip/rip level and sample.
Native deep encode checks use one-row tiles/blocks. Thus **unrestricted
bidirectional multi-row deep encoding cannot be certified against this fixed
commit**. A reference upgrade/fix is required to remove that qualification.

The managed writer emits the row-wise count/sample layout accepted by the
native decoder; tests retain multi-row managed roundtrips and full native decode
comparison. Native defects do not introduce a legacy fallback or relax managed
validation.
