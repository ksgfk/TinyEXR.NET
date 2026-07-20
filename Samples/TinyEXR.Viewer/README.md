# TinyEXR.Viewer

`TinyEXR.Viewer` is a desktop-first Avalonia sample for manual EXR inspection. It reads files directly through `TinyEXR.V3.ExrReader` and the V3 part/level/channel model.

## What It Does

- Opens EXR files from the file picker, drag and drop, or a command-line path.
- Displays flat, deep, and mixed flat/deep multipart files.
- Supports part, layer, and level switching when V3 materializes the part data.
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
