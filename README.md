# TinyEXR.NET

`TinyEXR.NET` is a pure managed C# port of the tinyexr v1 single-part API surface.

The library targets `netstandard2.1` and `net10.0`. The public API no longer exposes `TinyEXR.Native`, native handles, or `unsafe`-based lifecycle methods.

## Package

NuGet: [TinyEXR.NET](https://www.nuget.org/packages/TinyEXR.NET)

## Public API

The entry point is [`TinyEXR.Exr`](TinyEXR.NET/Exr.cs).

The managed facade provides:

- `TryReadVersion`, `TryReadHeader`, `TryReadImage`, `TryReadDeepImage`
- `TryReadRgba`, `TryReadLayers`, `TryWriteImage`, `TryWriteRgba`
- convenience wrappers such as `LoadEXR`, `LoadEXRWithLayer`, `SaveEXR`, `SaveEXRToMemory`
- spectral helpers such as `EXRFormatWavelength`, `EXRSpectralChannelName`, `EXRSetSpectralAttributes`
- helper classes [`SinglePartExrReader`](TinyEXR.NET/SinglePartExrReader.cs) and [`ScanlineExrWriter`](TinyEXR.NET/ScanlineExrWriter.cs)

The public data model is fully managed:

- [`ExrVersion`](TinyEXR.NET/ExrVersion.cs)
- [`ExrHeader`](TinyEXR.NET/ExrHeader.cs)
- [`ExrImage`](TinyEXR.NET/ExrImage.cs)
- [`ExrDeepImage`](TinyEXR.NET/ExrDeepImage.cs)
- [`ExrAttribute`](TinyEXR.NET/ExrAttribute.cs)
- [`ExrTileDescription`](TinyEXR.NET/ExrTileDescription.cs)
- [`ExrChannel`](TinyEXR.NET/ExrChannel.cs)

## Compression Support

Current v1 support is intentionally conservative:

- `None`: supported on `netstandard2.1` and `net10.0`
- `ZIP`, `ZIPS`: supported on `net10.0`
- `ZIP`, `ZIPS`: reported as `UnsupportedFeature` on `netstandard2.1`
- `RLE`, `PIZ`, `PXR24`, `B44`, `B44A`, `DWAA`, `DWAB`: recognized but not implemented yet, reported as `UnsupportedFeature`

## Quick Example

```csharp
using TinyEXR;

ResultCode result = Exr.LoadEXR("image.exr", out float[] rgba, out int width, out int height);
if (result != ResultCode.Success)
{
    throw new InvalidOperationException(result.ToString());
}

Console.WriteLine($"{width}x{height}, rgba={rgba.Length}");
```

```csharp
using TinyEXR;

float[] rgba =
{
    1.0f, 0.25f, 0.5f, 1.0f
};

ResultCode result = Exr.SaveEXRToMemory(rgba, 1, 1, 4, asFp16: false, out byte[] encoded);
if (result != ResultCode.Success)
{
    throw new InvalidOperationException(result.ToString());
}
```

## Tests

The test suite is split into:

- managed unit tests
- integration tests against `openexr-images`
- round-trip write/read tests
- a dedicated `netstandard2.1` fallback runner that validates ZIP/ZIPS unsupported paths

The integration samples are pinned to `openexr-images` commit `e38ffb0790f62f05a6f083a6fa4cac150b3b7452` and are expected under `.cache/openexr-images` by default.

Prepare them with:

```powershell
pwsh ./TinyEXR.Test/prepare-openexr-images.ps1
```

You can override the sample location with `TINYEXR_OPENEXR_IMAGES_ROOT`.

Run the full suite with:

```powershell
dotnet test --solution TinyEXR.Test/TinyEXR.Test.sln --configuration Release --report-trx --report-trx-filename portv1.trx -p:SignAssemblyKey=false
```

Note: the repository still keeps the upstream `tinyexr` submodule for reference and regression samples used by the tests, but the shipped package itself is pure managed.

## License

`TinyEXR.NET` is released under the MIT license.
