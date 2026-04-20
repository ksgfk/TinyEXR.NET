# TinyEXR.NET

`TinyEXR.NET` is a pure managed C# port of the tinyexr v1 API.

The library targets `net10.0`. The public API no longer exposes `TinyEXR.Native`, native handles, or `unsafe`-based lifecycle methods.

## Package

NuGet: [TinyEXR.NET](https://www.nuget.org/packages/TinyEXR.NET)

## Public API

The entry point is [`TinyEXR.Exr`](TinyEXR.NET/Exr.cs).

The managed facade provides:

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

- `None`, `ZIP`, `ZIPS`, `RLE`, `PIZ`, `PXR24`, `B44`, `B44A`: supported on `net10.0`
- `DWAA`, `DWAB`: recognized but not implemented yet, reported as `UnsupportedFeature`

For deep scanline loading, the supported compression set is narrower and follows the OpenEXR deep file layout:

- `None`, `RLE`, `ZIP`, `ZIPS`: supported
- `PIZ`, `PXR24`, `B44`, `B44A`, `DWAA`, `DWAB`: unsupported for deep images

This is an intentional divergence from the current vendored `tinyexr` source:

- `TinyEXR.NET` treats deep image compression as spec-bound behavior and rejects non-standard combinations such as deep scanline + `PIZ` with `UnsupportedFeature`
- the current `tinyexr` source tree kept in `TinyEXR.Native/tinyexr` is more permissive here and may attempt to decode deep scanline + `PIZ` when built with `TINYEXR_USE_PIZ`

In other words, this repository does not treat deep + `PIZ` as a missing ported feature. It treats it as a non-standard extension and keeps the managed implementation aligned with the OpenEXR deep file layout instead of mirroring that permissive upstream behavior.

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
