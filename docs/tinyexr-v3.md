# TinyEXR v3 API notes

## Upstream snapshot

`TinyEXR.Native/tinyexr` tracks upstream `origin/release` at commit
`1b106618644dbf8a0935c2348ba51a2d863dd7c2`, described by Git as
`v3.2.0-38-g1b10661` on 2026-07-20.

The v3 API is not a source-compatible revision of the legacy `tinyexr.h` API.
It is a new pure-C11 API in `include/exr.h`, with the old header retained for
legacy users and tests.

## V1 and v3 differences

| Area | Legacy v1 API | Upstream v3 API |
| --- | --- | --- |
| Implementation | Single-header legacy implementation with C-style entry points | Modular pure-C11 core in `src/*.c` |
| API shape | Independent `ParseEXR*`, `LoadEXR*`, and `SaveEXR*` function families | High-level whole-image calls plus stateful reader/writer and block APIs |
| Ownership | Caller-initialized structs, raw pointers, explicit free calls, optional error strings | Opaque reader/writer state, allocator hooks, and explicit image/part ownership |
| Results | Legacy integer result codes | `exr_result`, including `EXR_WOULD_BLOCK`, I/O, corrupt, and unsupported distinctions |
| I/O | Primarily file and complete-memory entry points | Memory, file, callback source, supplied ranges, seekable sink, suspend/resume |
| Partial reads | Separate legacy paths, without a common block contract | Whole part, scanline range, tile, or canonical block decode |
| Partial writes | Whole-image save paths | One scanline block or tile at a time with bounded working memory and offset backpatching |
| Multipart and levels | Separate structures and function families | Parts and mip/rip levels are part of the central image model |
| Deep data | Separate deep loader representation | Unified deep parts plus two-stage count/sample block decoding |
| Metadata | Parsed legacy header fields and custom attributes | First-class immutable header/channel/attribute model with round-trip attributes |
| Compression additions | Values through DWAB in the historical API | Adds HTJ2K256 (10), HTJ2K32 (11), and ZSTD (12) |
| Utility surface | EXR loading and saving | Also includes spectral, conversion, resize, tone-map, color, transfer, LUT, and RGBA helpers |

Upstream v3 intentionally leaves DWAA and DWAB unsupported. Its HTJ2K support
is a dedicated JPEG 2000 Part 15 implementation, not an alias for ordinary
JPEG 2000 Part 1.

## Managed mapping

The managed v3 surface lives in `TinyEXR.V3`; the existing `TinyEXR` namespace
continues to expose the v1-compatible facade.

| Upstream v3 concept | Managed API |
| --- | --- |
| `exr_result` | `ExrResult`, `ReaderResult`, `WriterResult` |
| `exr_box2i` | `Box2i` |
| `exr_channel` | `Channel` |
| `exr_header` / attributes | `Header`, `HeaderAttribute`, `TileDescription` |
| `exr_part` / `exr_image` | `Part`, `Image`, `FlatLevel`, `DeepLevel`, `ChannelBuffer` |
| `exr_reader` | `ExrReader` |
| `exr_writer` | `ExrWriter` |
| `exr_block_info` | `BlockInfo` |
| source callback / pending range | `IExactDataSource`, `IAsyncExactDataSource`, `SuppliedDataSource`, `DataRange` |
| seekable output sink | `ISeekableDataSink`, `IAsyncSeekableDataSink`, `StreamDataSink` |
| allocator limits | `ReaderLimits`, `WriterLimits` resource ceilings |
| spectral header/cube API | `Spectral`, `SpectralImage` |
| pixel conversion | `PixelConversion`, `PixelConversionMode` |
| one-shot / streaming resize | `ImageProcessing.Resize`, `StreamingImageResizer` |
| tone map, color, transfer | `ImageProcessing`, `ToneMapParameters`, `ColorMatrix3x3` |
| baked 3D LUT | `Lut3D`, `LutInterpolation` |
| planar/interleaved bridges | `PartConversion`, `InterleavedFloatImage` |

The managed API adds .NET-specific asynchronous operations, cancellation,
atomic retry rules, `IDisposable`/`IAsyncDisposable`, and immutable owned model
objects. It does not expose pointers or require unsafe code in this repository.

## Current managed v3 coverage

| Capability | Status |
| --- | --- |
| Header-only parsing and chunk indexing | Implemented |
| Single/multipart scanline and tiled parts | Implemented |
| One-level, mipmap, and ripmap levels | Implemented |
| Flat whole-part, scanline-range, tile, and block reads | Implemented |
| Deep scanline/tiled reads and two-stage block decode | Implemented |
| Whole deep-part materialization | Implemented |
| Sync/async exact sources and supplied-range resume | Implemented |
| Sync/async seekable streaming writer | Implemented |
| Missing/zero chunk-offset reconstruction | Implemented |
| Half/float plus u8/u16 and raw u32-to-float SIMD conversion | Implemented |
| ZIP/RLE predictor/reorder SIMD on x64 and ARM64 | Implemented |
| RGB color-matrix SIMD | Implemented |
| UInt/Half/Float plus u8/u16 conversion | Implemented |
| Spectral header helpers and wavelength-cube memory/file load | Implemented |
| Emissive/reflective spectral `Part` setup | Implemented |
| Whole-image and O(filter-support) streaming resize | Implemented |
| Tone-map, color matrix, luminance, and transfer functions | Implemented |
| `.cube` 3D LUT parse plus trilinear/tetrahedral apply | Implemented |
| Flat planar/interleaved and luminance-chroma bridges | Implemented |

The CPU utilities operate on caller-owned spans or immutable owned results.
Resize preserves HDR values and supports alpha-aware premultiplication; the
streaming variant accepts UInt, Half, or Float rows and returns `WouldBlock`
until its next contributor row is available. Half conversions reuse the v3 SIMD
path, while UInt narrowing follows upstream clamp and round-to-nearest,
ties-to-even behavior.

On `net8.0`, the conversion and EXR predictor paths use safe `Vector128`
operations when the current x86-64 or ARM64 runtime supports them. Color-matrix
application uses `System.Numerics.Vector3`. Scalar paths remain available for
unsupported hardware and `netstandard2.1`, and parity tests compare exact output
across every vector tail. Normalized `uint` conversion deliberately remains
scalar so it preserves the existing double-precision scaling semantics.

`SpectralImage` materializes part 0 into wavelength-major float planes, sorts
and merges wavelengths using the upstream 0.01 nm rule, preserves units and
polarisation handedness, and point-expands sampled spectral channels. Files are
still written by the central `ExrWriter`; `Spectral.CreateEmissivePart` and
`CreateReflectivePart` produce complete parts ready for that writer.

The optional CUDA/Vulkan APIs in `exr_gpu.h` / `exr_vk.h` are not mapped. The
native process-global worker-thread setting is also not exposed: managed I/O is
instance-scoped, supports async coordination, and does not rely on a mutable
global thread count.

### Compression matrix

"Raw fallback" means the file retains the requested compression tag but stores
the canonical block bytes when no encoded representation is available or
smaller. OpenEXR readers recognize this by the packed and canonical sizes being
equal for codecs that permit raw fallback. B44/B44A are decoded as codec data
even when their packed size equals the canonical size. Raw fallback is valid
storage, but it is not an implementation of the selected codec.

| Compression | Flat decode | Flat encode | Deep decode | Deep encode |
| --- | --- | --- | --- | --- |
| NONE | Raw | Raw | Raw | Raw |
| RLE | Implemented | Implemented | Implemented | Implemented |
| ZIPS / ZIP | Implemented | Implemented | Implemented | Implemented |
| PIZ / PXR24 | Implemented | Implemented | Raw-only input | Unsupported |
| B44 / B44A | Implemented | Implemented | Raw-only input | Unsupported |
| DWAA / DWAB | Raw-only input | Unsupported | Raw-only input | Unsupported |
| HTJ2K256 / HTJ2K32 | Implemented | Implemented | Raw only, matching upstream deep policy | Raw fallback only |
| ZSTD | Implemented | Implemented | Implemented | Implemented |

ZSTD decoding is an independent safe managed implementation. Encoding uses
`ZstdSharp.Port` 0.8.8 at level 3, matching tinyexr's policy. Flat data uses one
frame per block; deep data compresses the cumulative count table and sample
payload independently. Both paths store raw bytes when compression is not
smaller.

Deep RLE, ZIPS, and ZIP encoding applies the OpenEXR byte reorder/predictor and
compresses the cumulative count table and sample payload independently. The
writer uses canonical raw fallback separately for either payload when the
encoded form is not smaller.

## V1 facade migration

The public v1 facade remains available. Migration to the v3 core is currently
incremental:

- Single-part and multipart memory headers use the v3 format parser; seekable
  stream and file headers use the incremental v3 reader. Files with legacy
  unknown part-type strings continue through `PortV1`.
- Flat scanline and tiled memory, stream, and file image loads use `ExrReader`
  for both single-part and multipart files, including one-level, mipmap, and
  ripmap parts. The bridge reconstructs the legacy per-level and per-tile
  channel views from v3 planar levels. Caller-owned streams retain their
  original position and may use a non-zero EXR origin.
- `LoadEXR`, `LoadEXRWithLayer`, and `EXRLayers` use the same v3 flat image
  path, while retaining the v1 layer selection and RGBA expansion rules.
- HALF-to-FLOAT conversion on migrated read paths uses the v3 SIMD
  implementation.
- Single-part deep scanline and one-level deep-tiled memory, seekable-stream,
  and file loads use `ExrReader`. The bridge rebuilds the legacy per-row
  cumulative offset table, splits each planar deep channel into row sample
  arrays, and converts UINT, HALF, and FLOAT samples to the v1 `float`
  representation. Raw deep attributes such as `version`,
  `maxSamplesPerPixel`, `chromaticities`, and custom values are retained
  exactly. This path includes genuine ZSTD deep payloads and preserves non-zero
  stream origins and caller stream positions. Deep tiled mipmap/ripmap files
  return `UnsupportedFeature` because `ExrDeepImage` has no level dimension.
- Eligible flat memory/file saves use `ExrWriter` for both single-part and
  multipart scanline or tiled images, including single-entry multipart
  containers and mipmap/ripmap levels. The bridge slices legacy planar levels
  into streaming blocks and tiles, preserves part names and custom metadata,
  and maps the v1 `chromaticities` attribute to the first-class v3 field for
  exact round trips.
- Save-time UINT/HALF/FLOAT conversion is performed before each v3 writer block;
  HALF conversion uses the v3 SIMD implementation. Flat scanline channels may
  be subsampled when their sampling grid aligns with the data window. Tiled
  channels retain the OpenEXR unit-sampling requirement.
- The v1 multipart image model contains only `ExrImage`, so mixed or all-deep
  multipart image loads return `UnsupportedFeature`; multipart header parsing
  still exposes every part. Subsampled deep data and deep saves also remain
  unsupported. Non-aligned legacy scanline sampling, legacy unknown part types,
  and metadata that cannot be migrated without changing the v1 output contract
  continue through `PortV1` where that implementation can represent them.
- The legacy DWAA/DWAB `UnsupportedFeature` behavior is preserved.

This keeps existing callers stable while allowing individual facade paths to be
moved only after their compatibility behavior is covered by shared tests.

## HTJ2K implementation

No suitable managed HTJ2K backend was available during the v3 port:

- NuGet has no OpenJPH or HTJ2K package.
- Active pure-C# JPEG 2000 packages such as CoreJ2K implement Part 1 and parts
  of Part 2. They do not implement the Part 15 HT block coder.
- Native OpenJPEG wrappers also do not provide the OpenJPH Part 15 path and
  would conflict with the package's pure-managed and NativeAOT goals.

The managed reader and writer therefore include a dedicated safe-C# Part 15
implementation aligned with the upstream OpenEXR profile. The decoder validates
the codestream, parses JPEG 2000 markers and packets, decodes cleanup,
significance-propagation, and magnitude-refinement passes, and applies
reversible 5/3, RCT, and NLT reconstruction. The encoder writes genuine
HTJ2K32/HTJ2K256 codestreams for flat HALF, UINT, and FLOAT scanline or tiled
blocks, including sampled scanline components and upstream RCT/full-width UINT
profiles.

The codec is covered by genuine upstream fixtures, malformed payload tests,
all-zero codeblocks, mixed 32-bit channels, and encode/decode round trips.
Compressed deep HTJ2K data remains unsupported, matching the upstream deep
codec policy; deep writers can only use canonical raw fallback under those
compression tags.
