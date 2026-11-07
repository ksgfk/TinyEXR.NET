# Migrating Exr to the V3-only implementation

This is a breaking source and binary change. V1 DTOs, enums, readers, writers,
adapter and codec implementation are deleted. Rebuild all consumers against
package version `1.3.0`, with assembly and file versions `1.3.0.0`.

## Method signatures

All status-returning `Exr` methods now return `ExrResult`. File, Memory and
Stream variants use the same models and validation. The following examples
show the memory signatures; file and stream variants have the corresponding
`string filename` / `Stream stream` first argument.

| Method family | V3 form |
| --- | --- |
| `LoadEXR*`, `LoadEXRWithLayer*` | Existing float RGBA array and width/height `out` arguments; `ExrResult` status |
| `SaveEXR*` | Existing float input, dimensions, components and `asFp16`; `ExrResult` status |
| `ParseEXRVersionFromMemory` | `(ReadOnlySpan<byte> data, out uint rawVersionField)` |
| `ParseEXRHeaderFromMemory` | `(ReadOnlySpan<byte> data, out uint rawVersionField, out Header? header)` |
| `ParseEXRMultipartHeaderFromMemory` | `(ReadOnlySpan<byte> data, out uint rawVersionField, out Header[] headers)` |
| `LoadEXRImageFromMemory`, `LoadDeepEXRFromMemory` | `(ReadOnlySpan<byte> data, out Part? part)` |
| `LoadEXRMultipartImageFromMemory` | `(ReadOnlySpan<byte> data, out Image? image)` |
| `SaveEXRImageToMemory` | `(Part part, out byte[] encoded)` |
| `SaveEXRMultipartImageToMemory` | `(Image image, out byte[] encoded)` |
| `EXRNumLevels` | `(Part part)` returns the number of materialized levels |
| `EXRSetNameAttr` | `(Header header, string? name, out Header? updated)` |
| `EXRSetSpectralAttributes` | `(Header header, SpectralType spectrumType, string units, out Header? updated)` |

The deep file method is still named `LoadDeepEXR`. Save-to-file calls take
`(Part part, string filename)` or `(Image image, string filename)`. Parsing a
header before loading pixels is optional; there is no separate header input
to load or save methods. Additional symmetric helpers include
`LoadEXRWithLayerFromMemory`, `EXRLayersFromMemory` and `LoadDeepEXRFromMemory`.

```csharp
using TinyEXR;

ExrResult result = Exr.LoadEXRImageFromFile(inputPath, out Part? part);
if (result != ExrResult.Success || part is null)
    throw new InvalidOperationException($"Read failed: {result}");

ChannelBuffer pixels = part.GetLevel(0, 0).GetChannel("R");
// pixels.PixelType is the on-disk type. Use PixelConversion when needed.
result = Exr.SaveEXRImageToFile(part, outputPath);

if (Exr.EXRSetNameAttr(part.Header, "beauty", out Header? renamed) == ExrResult.Success)
    part = new Part(renamed!, part.Levels, part.IsComplete);
```

## Removed types

| Removed | Replacement |
| --- | --- |
| `ExrVersion` | `uint` raw field: low byte is version, bits 9/10/11/12 are tiled/long names/non-image/multipart |
| `ExrHeader`, `ExrChannel`, `ExrAttribute`, `ExrBox2i` | `Header`, `Channel`, `HeaderAttribute`, `Box2i` |
| `ExrImage`, `ExrDeepImage` | `Part`, containing `FlatLevel` or `DeepLevel` |
| `ExrImageLevel`, `ExrTile`, `ExrImageChannel`, `ExrDeepChannel` | `PartLevel`, reader tile/block APIs, `ChannelBuffer`, `DeepLevel.SampleCounts` and per-channel sample buffers |
| `ExrMultipartHeader`, `ExrMultipartImage` | `Header[]`, `Image` |
| `ExrPixelType`, `CompressionType`, `LineOrderType` | `PixelType`, `Compression`, `LineOrder` |
| `ExrTileDescription`, `ExrTileLevelMode`, `ExrTileRoundingMode` | `TileDescription`, `TileLevelMode`, `TileRoundingMode` |
| `SpectrumType` | `SpectralType`; absence is `None`, not null |
| `ResultCode` | `ExrResult`; use member names, not old numeric codes |
| `SinglePartExrReader`, `ScanlineExrWriter` | `ExrReader`, `ExrWriter`, or `ExrFile` |

`Header` and channel descriptions are immutable. `RequestedPixelType` is
removed: `Part` preserves stored sample types, while RGBA convenience loads
produce float. Use `PixelConversion` or `PartConversion` for explicit conversion.
Spectral methods delegate to `Spectral`; the old and new spectral enum values
are different, so migrate by member name rather than integer cast.

## Preserved convenience behavior

- Default and named layers retain the existing last-dot layer discovery and
  prefix selection rules, including the existing multiview fixture behavior.
  Missing layers return `InvalidArgument`. A single selected channel fills all
  four RGBA components; RGB without alpha gets alpha 1. Sampled channels expand
  using V3 absolute coordinates, including negative data-window origins.
- Simple saving accepts 1, 3 or 4 components. One component is named `A`.
  `asFp16` selects HALF/FLOAT. Compression defaults to NONE only when both
  dimensions are below 16; otherwise ZIP.
- Only `Exr` saving normalizes flat scanline line order to `IncreasingY`, by
  creating a new header. `ExrFile` and `ExrWriter` honor the supplied order.
- Channels are organized in V3 UTF-8 name order; buffers match by name. Long
  name flags are automatic. Nonempty single-part names are serialized.

## Validation and failure changes

All full parsing and I/O use `ExrReader` / `ExrWriter` and their default limits.
There is no fallback after failure. Unknown version bits, unknown part types,
misaligned sampling, oversized headers and malformed data follow V3 results.
Old detailed error codes and permissive recovery behavior are not retained.

Single-part calls reject a multipart flag even if there is only one part.
Multipart saving always sets the flag. `LoadEXRImage*` requires flat data;
`LoadDeepEXR*` requires deep data. Multilevel deep and mixed flat/deep multipart
are supported by the unified models.

`IsEXR*` and `ExrFile.IsExr` probe only the four-byte magic. Version parsing
requires only the eight-byte prefix and validates the complete version field.
Header parsing also reads and validates the offset tables, as `ExrReader` does.

Caller streams must be readable and seekable. Their current position is the
EXR origin; they remain open and their position is restored after success or
failure. Failed calls return null objects, empty arrays and zero dimensions /
raw version fields. DWAA/DWAB retain raw-only decoding and reject encoding;
ZSTD is unsupported.

The subsequent [V3.1 migration](migration-v3.1.md) tightens deep compression and
format validation, adds contexts/metadata/budgets, and adopts official OpenEXR
compression IDs and verification.
