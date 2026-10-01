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
- Inspects pixels with a right-click on the preview. The Pixel Inspector shows zero-based coordinates in the selected level, absolute EXR coordinates, and original channel values for the selected layer before exposure or sRGB conversion. A cyan marker stays on the selected pixel when the window is resized.
- Preserves pixel readings while adjusting exposure; opening a file or switching part, layer, or level clears the selection. Use **Clear** to remove it manually. Right-clicking the margins around the image does not select a pixel.
- Displays exact `UInt` values and identifies the stored sample coordinates used for subsampled channels, matching the preview's held-sample behavior.
- Shows EXR version flags, windows, tile metadata, parts, layers, channels, deep statistics, and custom attributes.

## Current Boundaries

- No true HDR output path.
- No tone mapping.
- Deep parts show structure and statistics, but do not produce a 2D preview.
- Parts using an unsupported codec or exceeding reader limits remain available as metadata with their decode status.
- Pixel mapping and original channel sampling are covered by shared tests in both compatibility hosts. Validate the desktop UI by launching the app and opening representative EXR samples.

## Run

```powershell
dotnet run --project .\Samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj
```

Or pass an EXR path directly:

```powershell
dotnet run --project .\Samples\TinyEXR.Viewer\TinyEXR.Viewer.csproj -- .\.cache\openexr-images\ScanLines\Desk.exr
```
