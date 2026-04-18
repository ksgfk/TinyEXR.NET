# TinyEXR.NativeAot.SourceStatic

This sample consumes `TinyEXR.NET` from a local NuGet feed and enables source-based static linking for NativeAOT.

It restores packages into `artifacts/nuget-packages` so the sample can be verified without touching the machine-wide NuGet cache.

## Build

From the repository root:

```powershell
dotnet pack TinyEXR.NET\TinyEXR.NET.csproj -c Release -o artifacts\packages
dotnet publish samples\TinyEXR.NativeAot.SourceStatic\TinyEXR.NativeAot.SourceStatic.csproj -c Release -r win-x64 -p:PublishAot=true
```

Expected result:

- the publish step runs `cmake` automatically
- the published executable starts without `TinyEXRNative.dll`
- running the executable prints `False`

The sample uses:

```xml
<TinyEXRStaticLinkMode>source</TinyEXRStaticLinkMode>
```

When that property is omitted or set to `dynamic`, `TinyEXR.NET` falls back to the packaged native shared library assets instead.
