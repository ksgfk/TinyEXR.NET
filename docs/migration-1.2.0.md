# Migrating to TinyEXR.NET 1.2.0

TinyEXR.NET 1.2.0 is a deliberate breaking minor release. The modern managed
API uses unversioned namespaces alongside the existing v1-compatible facade.
The NuGet package version is `1.2.0`; `AssemblyVersion` and `FileVersion` are
`1.2.0.0`. `ExrFile.ApiVersionMajor` remains `3`: it identifies the upstream
API generation, not the package version or managed namespace.

## Required consumer changes

1. Replace `using TinyEXR.V3;` with `using TinyEXR;` and
   `using TinyEXR.V3.IO;` with `using TinyEXR.IO;`. Update global usings,
   aliases, fully qualified type names, and reflection or configuration strings
   that contain the old names.
2. Rename references to the modern `TinyEXR.V3.SpectrumType` to
   `TinyEXR.SpectralType`. Do not rename the legacy `TinyEXR.SpectrumType`.
3. Rebuild every dependent assembly against 1.2.0, including libraries and
   plugins, and deploy the rebuilt dependency set together. Update any
   persisted assembly-qualified type names used by your application.

There are no `TinyEXR.V3` compatibility wrappers or type forwarders. The old
fully qualified type identities no longer exist, so replacing the DLL or
adding an assembly binding redirect cannot make an old modern-API consumer
compatible. This release is not a binary drop-in replacement.

Consumers using only the legacy facade keep its source names, but should
still rebuild and validate their dependencies against the new assembly version.
If you cannot migrate yet, constrain the NuGet dependency to `[1.1.0,1.2.0)`:

```xml
<PackageReference Include="TinyEXR.NET" Version="[1.1.0,1.2.0)" />
```

## Spectral enum distinction

Only the modern enum type is renamed. Its values do not change:

| 1.1.x type | 1.2.0 type | Values |
| --- | --- | --- |
| `TinyEXR.V3.SpectrumType` | `TinyEXR.SpectralType` | `None = 0`, `Reflective = 1`, `Emissive = 2`, `Polarised = 3` |
| `TinyEXR.SpectrumType` (legacy) | `TinyEXR.SpectrumType` (unchanged) | `Reflective = 0`, `Emissive = 1`, `Polarised = 2` |

These enums are not numerically interchangeable. Avoid casting between them
or applying a blind `SpectrumType` replacement across legacy code.

The member names `Spectral.GetSpectrumType` and `SpectralImage.SpectrumType`
are unchanged; their return/property type is now `SpectralType`. For example:

```csharp
using TinyEXR;

SpectralType type = Spectral.GetSpectrumType(header);
if (type == SpectralType.None)
{
    // The header does not describe spectral data.
}
```

## Complete public type mapping

All 69 modern public types are listed below: 57 in the root namespace and
12 in the I/O namespace. Generic and non-generic result types are separate
entries; `<T>` denotes generic arity 1, while entries without it have arity 0.
No legacy facade types are moved.

### Root namespace (57 types)

| 1.1.x fully qualified type | 1.2.0 fully qualified type |
| --- | --- |
| `TinyEXR.V3.ExrResult` | `TinyEXR.ExrResult` |
| `TinyEXR.V3.PixelType` | `TinyEXR.PixelType` |
| `TinyEXR.V3.Compression` | `TinyEXR.Compression` |
| `TinyEXR.V3.LineOrder` | `TinyEXR.LineOrder` |
| `TinyEXR.V3.PartType` | `TinyEXR.PartType` |
| `TinyEXR.V3.TileLevelMode` | `TinyEXR.TileLevelMode` |
| `TinyEXR.V3.TileRoundingMode` | `TinyEXR.TileRoundingMode` |
| `TinyEXR.V3.ResizeFilter` | `TinyEXR.ResizeFilter` |
| `TinyEXR.V3.EdgeMode` | `TinyEXR.EdgeMode` |
| `TinyEXR.V3.ToneMapOperator` | `TinyEXR.ToneMapOperator` |
| `TinyEXR.V3.ColorSpace` | `TinyEXR.ColorSpace` |
| `TinyEXR.V3.TransferFunction` | `TinyEXR.TransferFunction` |
| `TinyEXR.V3.LutInterpolation` | `TinyEXR.LutInterpolation` |
| `TinyEXR.V3.SpectrumType` | `TinyEXR.SpectralType` |
| `TinyEXR.V3.PixelConversionMode` | `TinyEXR.PixelConversionMode` |
| `TinyEXR.V3.ExrFile` | `TinyEXR.ExrFile` |
| `TinyEXR.V3.Box2i` | `TinyEXR.Box2i` |
| `TinyEXR.V3.Chromaticities` | `TinyEXR.Chromaticities` |
| `TinyEXR.V3.ChannelBuffer` | `TinyEXR.ChannelBuffer` |
| `TinyEXR.V3.PartLevel` | `TinyEXR.PartLevel` |
| `TinyEXR.V3.FlatLevel` | `TinyEXR.FlatLevel` |
| `TinyEXR.V3.DeepSampleRange` | `TinyEXR.DeepSampleRange` |
| `TinyEXR.V3.DeepLevel` | `TinyEXR.DeepLevel` |
| `TinyEXR.V3.Part` | `TinyEXR.Part` |
| `TinyEXR.V3.Image` | `TinyEXR.Image` |
| `TinyEXR.V3.ColorMatrix3x3` | `TinyEXR.ColorMatrix3x3` |
| `TinyEXR.V3.ToneMapParameters` | `TinyEXR.ToneMapParameters` |
| `TinyEXR.V3.ImageProcessing` | `TinyEXR.ImageProcessing` |
| `TinyEXR.V3.Lut3D` | `TinyEXR.Lut3D` |
| `TinyEXR.V3.Channel` | `TinyEXR.Channel` |
| `TinyEXR.V3.HeaderAttribute` | `TinyEXR.HeaderAttribute` |
| `TinyEXR.V3.TileDescription` | `TinyEXR.TileDescription` |
| `TinyEXR.V3.Header` | `TinyEXR.Header` |
| `TinyEXR.V3.InterleavedFloatImage` | `TinyEXR.InterleavedFloatImage` |
| `TinyEXR.V3.PartConversion` | `TinyEXR.PartConversion` |
| `TinyEXR.V3.ExrReader` | `TinyEXR.ExrReader` |
| `TinyEXR.V3.ReaderState` | `TinyEXR.ReaderState` |
| `TinyEXR.V3.ReaderLimits` | `TinyEXR.ReaderLimits` |
| `TinyEXR.V3.ReaderOptions` | `TinyEXR.ReaderOptions` |
| `TinyEXR.V3.ReaderLimitExceededException` | `TinyEXR.ReaderLimitExceededException` |
| `TinyEXR.V3.ReaderResult` | `TinyEXR.ReaderResult` |
| `TinyEXR.V3.ReaderResult<T>` | `TinyEXR.ReaderResult<T>` |
| `TinyEXR.V3.BlockInfo` | `TinyEXR.BlockInfo` |
| `TinyEXR.V3.DeepChannelDestination` | `TinyEXR.DeepChannelDestination` |
| `TinyEXR.V3.PixelConversion` | `TinyEXR.PixelConversion` |
| `TinyEXR.V3.SimdCapabilities` | `TinyEXR.SimdCapabilities` |
| `TinyEXR.V3.SimdRuntime` | `TinyEXR.SimdRuntime` |
| `TinyEXR.V3.Spectral` | `TinyEXR.Spectral` |
| `TinyEXR.V3.SpectralImage` | `TinyEXR.SpectralImage` |
| `TinyEXR.V3.StreamingImageResizer` | `TinyEXR.StreamingImageResizer` |
| `TinyEXR.V3.ExrWriter` | `TinyEXR.ExrWriter` |
| `TinyEXR.V3.WriterState` | `TinyEXR.WriterState` |
| `TinyEXR.V3.WriterLimits` | `TinyEXR.WriterLimits` |
| `TinyEXR.V3.WriterOptions` | `TinyEXR.WriterOptions` |
| `TinyEXR.V3.WriterLimitExceededException` | `TinyEXR.WriterLimitExceededException` |
| `TinyEXR.V3.WriterResult` | `TinyEXR.WriterResult` |
| `TinyEXR.V3.WriterResult<T>` | `TinyEXR.WriterResult<T>` |

### I/O namespace (12 types)

| 1.1.x fully qualified type | 1.2.0 fully qualified type |
| --- | --- |
| `TinyEXR.V3.IO.DataTransferStatus` | `TinyEXR.IO.DataTransferStatus` |
| `TinyEXR.V3.IO.DataRange` | `TinyEXR.IO.DataRange` |
| `TinyEXR.V3.IO.DataTransferResult` | `TinyEXR.IO.DataTransferResult` |
| `TinyEXR.V3.IO.IDataSourceLength` | `TinyEXR.IO.IDataSourceLength` |
| `TinyEXR.V3.IO.IExactDataSource` | `TinyEXR.IO.IExactDataSource` |
| `TinyEXR.V3.IO.IAsyncExactDataSource` | `TinyEXR.IO.IAsyncExactDataSource` |
| `TinyEXR.V3.IO.ISeekableDataSink` | `TinyEXR.IO.ISeekableDataSink` |
| `TinyEXR.V3.IO.IAsyncSeekableDataSink` | `TinyEXR.IO.IAsyncSeekableDataSink` |
| `TinyEXR.V3.IO.MemoryDataSource` | `TinyEXR.IO.MemoryDataSource` |
| `TinyEXR.V3.IO.StreamDataSink` | `TinyEXR.IO.StreamDataSink` |
| `TinyEXR.V3.IO.StreamDataSource` | `TinyEXR.IO.StreamDataSource` |
| `TinyEXR.V3.IO.SuppliedDataSource` | `TinyEXR.IO.SuppliedDataSource` |

## Source layout and internal namespaces

The 41 managed source files previously under `TinyEXR.NET/V3/` move directly
under `TinyEXR.NET/`, preserving their subfolders such as `IO/`, `Reader/`,
`Writer/`, `Simd/`, `Format/`, and `Compression/`. Internal format types use
`TinyEXR.Format`; internal compression types use `TinyEXR.Codecs` (the source
folder remains `Compression/`). These are implementation details, not new
public APIs. The legacy `PortV1` implementation remains in place.

References to upstream tinyexr v3, its native C API, historical benchmark runs,
and existing v3-named test or benchmark identifiers still refer to that API
generation. They do not imply that `TinyEXR.V3` remains a supported namespace.

## Verification and release signing

Check consumers on both supported targets, `net8.0` and `netstandard2.1`.
Repository verification uses the .NET 10 SDK and both shared test hosts; see
[Test/README.md](../Test/README.md) for fixture setup and test commands.

Local builds and tests with `-p:SignAssemblyKey=false` validate unsigned
assemblies only. Passing them does not establish compatibility with an old
binary or validate a signed release package. Release signing still requires
the authorized private strong-name key and separate signed-package verification.
