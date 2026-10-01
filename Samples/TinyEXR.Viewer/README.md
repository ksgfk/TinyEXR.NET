# TinyEXR.Viewer

`TinyEXR.Viewer` is a desktop-first Avalonia sample for manual EXR inspection. It reads files directly through `TinyEXR.ExrReader` and the modern part/level/channel model.

The sample uses the unversioned managed namespaces introduced in TinyEXR.NET
1.2.0. See the [migration guide](../../docs/migration-1.2.0.md) when updating
code based on earlier versions of this sample.

## What It Does

- Opens EXR files from the file picker, drag and drop, or a command-line path.
- Displays flat, deep, and mixed flat/deep multipart files.
- Supports part, layer, and level switching when the reader materializes the part data.
- Previews flat parts while exposing deep part levels, channels, and aggregate sample statistics.
- Applies exposure and converts linear HDR values to SDR `sRGB` for preview.
- Shows EXR version flags, windows, tile metadata, parts, layers, channels, deep statistics, and custom attributes.

## Current Boundaries

- No true HDR output path.
- No tone mapping.
- Deep parts show structure and statistics, but do not produce a 2D preview.
- Parts using an unsupported codec or exceeding reader limits remain available as metadata with their decode status.
- No automated tests are included; validate by launching the app and opening representative EXR samples.

## Run

```powershell
dotnet run --project .\Samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj
```

Or pass an EXR path directly:

```powershell
dotnet run --project .\Samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj -- .\.cache\openexr-images\ScanLines\Desk.exr
```
