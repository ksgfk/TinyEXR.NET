# Codec provenance and official verification

Official **OpenEXR v3.5.3**, commit
`3950290530c4fd4776667d0dc4d65a9d003eb936`, is the sole format and sample
reference. `Scripts/cmake/OpenEXR.cmake` is shared by tests and benchmarks. It pins
Imath 3.2.3 (`5f27ba266d3ea1565e912570c30b5eafc89959f1`) and uses OpenEXR's bundled
OpenJPH 0.32, libdeflate and ZSTD sources. No system dependency silently replaces
these sources. Native dependencies are test/benchmark-only and do not ship in NuGet.

TinyEXR `eb0ba07f44a33e1ce5bc0fb74142a3a6558f0661` is a port source and performance comparator, never a correctness oracle.
The original V3-only port used `1b106618644dbf8a0935c2348ba51a2d863dd7c2`; unchanged
source-file annotations retain that history. The unmodified source is cached by Scripts/prepare-tinyexr.ps1; its pin lives in Scripts/tinyexr-source.json. Git submodules are no longer required.

| Managed source | Port source / authoritative correction |
| --- | --- |
| `BlockCodec`, `CodecContext` | TinyEXR `exr_codec.c`, `exr_core.c`; official compression IDs, deep capabilities and sampling |
| `RleCodec` | TinyEXR `exr_rle.c`, including `15dc739` run boundaries; bounded Span packing |
| `ZipCodec`, `Pxr24Codec` | TinyEXR EXR transforms; official `internal_pxr24.c` payload interpretation |
| `PizCodec`, `PizHuffman`, `PizWavelet` | TinyEXR `exr_piz.c`; sparse canonical tables (`be487f7`), reusable workspace, bounded lookup and bitmap handling |
| `B44Codec`, `B44Tables` | TinyEXR pack/unpack ancestry; official `internal_b44.c` and `internal_b44_table_init.c` quantization, exp/log direction and HALF rounding |
| `Htj2k*` | OpenJPH/TinyEXR Part 15 ancestry; official unsigned level shift, NLT and multiple tile-parts; reused decode scratch and packet storage |
| `ZlibBackend` | Bounded zlib contract, implemented by `ZLibStream` / SharpZipLib; no generic DEFLATE re-port |

TinyEXR/OpenEXR BSD-3-Clause and OpenJPH BSD-2-Clause attribution is retained in
[THIRD-PARTY-NOTICES](../THIRD-PARTY-NOTICES). Original managed code remains MIT.

## Upstream follow-up decisions

The V3.1 follow-up includes reusable contexts, explicit quota status/file budgets,
aggregate materialization limits, typed metadata, preview validation, selective
spectral loading and wavelength bounds. Managed Span lengths, checked offsets,
request-size caps and destination validation provide the applicable sized-C-API
and input-hardening protections. RLE literal boundaries, PIZ sparse Huffman work,
range-table handling and HTJ2K scratch reuse were adopted and measured.

Existing safe SIMD predictor/conversion paths remain paired with scalar tests.
Architecture-specific C AVX/NEON cleanup kernels are not copied into managed code
without matching managed performance evidence. Generic DEFLATE changes do not
apply to the retained .NET/SharpZipLib backends. GPU, texture compression and
unrelated tocio features are outside the EXR library scope.

The two managed deep fixes remain: cumulative counts restart on each row, and
samples are serialized by row then channel. Tests exercise multirow, multichannel
deep tiles in both directions with OpenEXR; there are no TinyEXR bug exemptions.
Illegal deep ZIP/HTJ2K combinations are rejected instead of retained for testing.
Official edge tiles can include a full nominal raw count table; only its compact
valid prefix is materialized.

## Oracle tools

`Test/NativeReference/codec_oracle.cpp` uses OpenEXR Core compression/decompression
and the C++ compressor context. `file_oracle.cpp` uses official multipart, tiled,
scanline and deep C++ APIs. It can independently generate mixed/sampled/negative
window/ripmap files, reencode files, dump every part/level/channel/count/sample and header attributes,
and export both complete B44 tables. No RGBA-only comparator is used.

```sh
cmake -S Test/NativeReference -B .cache/openexr-reference -G Ninja -DCMAKE_BUILD_TYPE=Release
cmake --build .cache/openexr-reference --parallel 4
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false --report-trx
dotnet test --project Test/TinyEXR.Interop/TinyEXR.Interop.csproj -c Release -p:SignAssemblyKey=false -p:LibraryFramework=netstandard2.1 --report-trx
```

On Windows use a Visual Studio developer shell and add
`-DCMAKE_C_COMPILER=clang-cl -DCMAKE_CXX_COMPILER=clang-cl` to CMake. On x64 Windows,
the shared configuration enables SSE4.1 for official code requiring those
intrinsics and disables OpenJPH AVX512 in clang-cl. Upstream sources stay unchanged.

The matrix is an independent MSTest project with individually discovered cases.
Set `TINYEXR_OPENEXR_REFERENCE_DIR` to override the default native build directory.
Missing tools fail the test; failed inputs and process logs are attached to TRX.
The matrix compares lossless bytes exactly. Lossy codecs compare native
quantization and both decoders on the same file, not compressed byte identity.
It covers all supported codecs, sampling, negative origins, edge tiles, mip/rip,
multipart, zero/variable deep counts, all HALF inputs for B44, NaNs, infinities,
denormals and signed zero. Both B44 tables have all 65,536 entries checked.
Retained TinyEXR fixtures are inputs whose expected samples are checked with
OpenEXR. CI repeats the oracle on x64/ARM64, net8/netstandard and scalar paths.
