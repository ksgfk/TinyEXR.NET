# Managed V3.1 API and format support

The public namespace is `TinyEXR`, with data sources and sinks in `TinyEXR.IO`.
`ExrFile.ApiVersionMajor/Minor/Patch` is **3.1.0**. This API identifier is separate
from package/assembly version 1.3.0/1.3.0.0 and EXR file version 2.
See [V3-only migration](migration-v3-only.md) and [V3.1 migration](migration-v3.1.md).

Official OpenEXR 3.5.3 is the only correctness reference. Performance benchmarks
compare TinyEXR.NET, TinyEXR V3 and official OpenEXR through whole-image memory APIs.
TinyEXR's modular V3 sources provide port ancestry; its private extensions and
accidental permissiveness are not part of the managed format contract.

## Support matrix

| Compression | Official ID | Flat read/write | Deep read/write |
| --- | --- | --- | --- |
| NONE | 0 | Yes | Yes |
| RLE | 1 | Yes | Yes |
| ZIPS | 2 | Yes | Yes |
| ZIP | 3 | Yes | Invalid combination |
| PIZ | 4 | Yes | Invalid combination |
| PXR24 | 5 | Yes | Invalid combination |
| B44 / B44A | 6 / 7 | Yes | Invalid combination |
| DWAA / DWAB | 8 / 9 | Official raw fallback subset / no encoding | Invalid combination |
| HTJ2K256 / HTJ2K32 | 10 / 11 | Yes | Invalid combination |
| LJ2K | 12 | Unsupported | Invalid combination |
| ZSTD | 13 | Unsupported | Unsupported |

Deep validity follows OpenEXR Core `exr_compression_is_valid_for_deep` at the
pinned commit. Canonical raw fallback does not make an invalid combination legal.
Compressed DWA input returns `Unsupported`; no alternative decoder is attempted.

All flat/deep scanline/tiled parts, edge tiles, mipmaps, ripmaps and mixed multipart
containers use the same Reader/Writer model. Tiled and deep channels require unit
sampling. Flat scanline origins, widths and heights must be divisible by channel
sampling factors, including negative origins. Channels use UTF-8 byte name order.
Multipart headers must agree on shared display geometry and attributes.

## Streaming and ownership

`ExrReader` supports whole parts, levels, scanline ranges, tiles and canonical
blocks. Deep block decode reads counts before samples. `ExrWriter` writes blocks
and backpatches offsets. Synchronous/asynchronous sources and sinks support
cancellation and `WouldBlock` retries; supplied sources may have unknown length.

Optional `ExrContext` resources are exclusively leased until Reader/Writer disposal.
Without a context each object owns independent resources. Pools are private,
never `ArrayPool<T>.Shared`. `Trim()` releases idle retained buffers. Limits are
per-options, not stored in the context. Headers, previews and image models own
immutable input copies; codec scratch never escapes into returned image data.

Explicit quotas produce `LimitExceeded`, invalid file structures `InvalidFile`,
invalid save models `InvalidArgument`, malformed blocks `Corrupt`, and valid
unimplemented features `Unsupported`. Defaults retain the existing protection
ceilings. Whole-image read budgets include all parts/levels and deep counts/samples.
File limits charge maximum accessed extent from the logical origin, not total
traffic; retries and output backpatches do not consume the budget twice.

## Metadata, spectral and utilities

`Header.WithIntAttribute`, `WithFloatAttribute` and `WithPreviewImage` return new
headers. Matching `TryGet...` methods report missing/type mismatches as false.
Float custom attributes preserve their original bits, including NaNs. Preview
images contain copied RGBA8 pixels and are limited to 2^24 pixels and configured
metadata budgets.

Spectral loads default to the first flat spectral part, with explicit part-index
overloads for memory/file input. Only the selected part is decoded. Wavelengths
are finite and within 0..10^7 nm. Expanded budgets include wavelengths and all
Stokes planes. Sampling expansion uses absolute EXR coordinates.

Pixel conversion, resize/streaming resize, tone mapping, color/transfer transforms,
3D LUTs and planar/interleaved bridges remain available. Safe x64/ARM64 SIMD paths
are checked against scalar implementations; netstandard2.1 uses scalar fallbacks.
HTJ2K is a fully managed Part 15 codec, including RCT, NLT and multiple tile-parts.
No native code or unsafe context is required by consumers.

## Exr convenience contract

`Exr` retains familiar method names and out arguments over V3 types. All parsing
and coding use Reader/Writer; magic/version probes read four/eight bytes only.
Layer, multiview, RGBA expansion, single-channel replication, default compression
and flat-scanline `IncreasingY` save normalization remain library contracts.
Direct Reader/Writer/ExrFile calls respect supplied line order. Caller streams
remain open and Exr convenience calls restore their original position on failure
or success. Single-part calls reject multipart flags, even for one part;
multipart saving always sets the flag. There is no V1 model or fallback path.
