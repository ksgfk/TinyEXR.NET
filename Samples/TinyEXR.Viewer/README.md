# TinyEXR.Viewer

`TinyEXR.Viewer` is a desktop-first Avalonia sample for manual EXR inspection on top of `TinyEXR.NET`.

## What It Does

- Opens EXR files from the file picker, drag and drop, or a command-line path.
- Displays single-part images and pure image multipart files.
- Supports part, layer, and level switching when the decoded image data is available.
- Applies exposure and converts linear HDR values to SDR `sRGB` for preview.
- Shows EXR version flags, windows, tile metadata, parts, layers, channels, deep statistics, and custom attributes.

## Current Boundaries

- No true HDR output path.
- No tone mapping.
- Mixed multipart files with deep or non-image parts are metadata-only.
- Deep single-part files show structure and statistics, but do not produce a 2D preview.
- No automated tests are included; validate by launching the app and opening representative EXR samples.

## Run

```powershell
dotnet run --project .\Samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj
```

Or pass an EXR path directly:

```powershell
dotnet run --project .\Samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj -- .\.cache\openexr-images\ScanLines\Desk.exr
```
