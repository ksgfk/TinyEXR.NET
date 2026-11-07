# Migrating to TinyEXR.NET 1.3.0 (managed API 3.1)

NuGet version **1.3.0** uses assembly and file versions **1.3.0.0**.
Rebuild consumers: optional constructor parameters and format rules
changed. The API identifier is now `ExrFile.ApiVersionMajor = 3`, `Minor = 1`.
The earlier [V3-only breaking changes](migration-v3-only.md) still apply.

## Behavior changes

- Correctness comparisons use official OpenEXR 3.5.3 at
  `3950290530c4fd4776667d0dc4d65a9d003eb936`. Performance benchmarks compare
  TinyEXR.NET, TinyEXR V3 and official OpenEXR through whole-image memory APIs.
- Deep supports only NONE, RLE and ZIPS. Deep ZIP/HTJ2K/PIZ/B44/DWA requests are
  invalid even for raw data. Valid but unimplemented deep ZSTD is `Unsupported`.
- `Compression.LJ2K = 12`, `ZSTD = 13` follow official numbering. Do not reuse
  TinyEXR's private value 12 for ZSTD. Neither codec is implemented.
- Illegal header structures return `InvalidFile`; malformed blocks remain
  `Corrupt`. Invalid saves return `InvalidArgument`; unimplemented features
  return `Unsupported`. Explicit resource caps now return **`LimitExceeded = -7`**.
- Multipart display windows and shared attributes must match. Empty part names
  become unique `partN` names, avoiding explicit names without changing inputs.
  Explicit duplicate names are rejected.
- B44 perceptual tables/quantization, PXR24 payload handling, HTJ2K UINT/NLT and
  official tile-parts now follow OpenEXR. Two deep row-layout fixes are retained.

## New APIs

`ReaderLimits` and `WriterLimits` append `maximumFileByteCount` (zero = unlimited).
Writer limits also append `maximumMaterializedByteCount` (zero = unlimited).
Reader materialization still defaults to `int.MaxValue`; all existing other
resource defaults remain unchanged. Whole-image budgets include all parts and
levels, plus deep count and sample arrays. Streaming writes enforce block budgets.
File budgets measure maximum accessed extent relative to the source/sink origin,
including seeks, with no repeated charge for retries or backpatches.

`ReaderOptions` and `WriterOptions` append optional `context`:

```csharp
using ExrContext context = new();
ReaderOptions reads = new(new ReaderLimits(maximumFileByteCount: 64L << 20), context: context);
ReaderResult<Image> loaded = ExrFile.LoadFromFile(path, reads);
if (loaded.IsSuccess)
    ExrFile.SaveToFile(loaded.Value!, output, new WriterOptions(context: context));
context.Trim();
```

A reader/writer leases the context until disposal, even after completing an
operation. Binding a second object, trimming or disposing a busy context throws
`InvalidOperationException`. Idle disposal is idempotent. Disposed contexts cannot
be reused. Without a context the original independent ownership behavior remains.

`Header.WithIntAttribute` / `TryGetIntAttribute`, `WithFloatAttribute` /
`TryGetFloatAttribute`, and `WithPreviewImage` / `TryGetPreviewImage` return immutable
copies or typed lookup results. Setters cannot overwrite modeled reserved fields.
Missing/wrong types return false. Float payloads preserve exact IEEE bits.
`PreviewImage(width, height, rgba8)` copies the input and validates positive sizes,
payload length, overflow and the 2^24-pixel cap. Metadata budgets still apply.

`SpectralImage.LoadFromMemory(data, partIndex, options)` and
`LoadFromFile(path, partIndex, options)` select a part. Existing overloads automatically select the
first flat spectral part. Explicit negative or out-of-range indices return `InvalidArgument`; deep/non-spectral selections
return `Unsupported`. Only selected pixels are decoded. Wavelengths must be finite
within 0..10^7 nm. Expansion budgets count wavelengths plus every Stokes plane;
a 512-byte HALF source cannot allocate a 1024-byte float cube under a 512-byte cap.
More than 4,096 distinct wavelengths now raises the wavelength quota error instead
of silently truncating the returned list; spectral load results use `LimitExceeded`.
