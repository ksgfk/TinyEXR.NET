# TinyEXR.NET

`TinyEXR.NET` is a C# wrapper of [tinyexr](https://github.com/syoyo/tinyexr)

The target framework of `TinyEXR.NET`  is `.NET Standard 2.1`

## Download

`TinyEXR.NET` can be found on NuGet [![NuGet](https://img.shields.io/nuget/v/TinyEXR.NET)](https://www.nuget.org/packages/TinyEXR.NET) (← click it !)

## Usage

The API is completely consistent with tinyexr. You can find them in the class `TinyEXR.Exr`.

Besides, I add some simple helper class, like `TinyEXR.SinglePartExrReader` and `TinyEXR.ScanlineExrWriter`. You can use them to easily read and save exr images.

If you don't like them, you can also use native functions in the class `TinyEXR.Native.EXRNative`. Of course, you should be clear about what you are doing :).

## Details

This lib use [ClangSharp](https://github.com/dotnet/ClangSharp) to generate binding code.

API is unstable. May be modified at any time.

tinyexr did not export any symbols, so I have to make a wrapper for these C++ functions. Fortunately, they are not so much. The wrapper lib is in the folder `TinyEXR.Native`

Currently, only `win-x64`, `win-arm64`, `linux-x64`, `linux-arm64`, `osx-x64` and `osx-arm64` are available. If you want use this lib on other platforms, you have to build them by your self.

## NativeAOT

`TinyEXR.NET` now supports two NativeAOT consumption modes:

- default dynamic mode: consume the packaged `runtimes/<rid>/native` shared library assets
- source static mode: let the publishing project build `TinyEXRNative` locally with `cmake` and statically link it into the final NativeAOT executable

To enable source static mode in the final application project:

```xml
<PropertyGroup>
  <TinyEXRStaticLinkMode>source</TinyEXRStaticLinkMode>
</PropertyGroup>
```

Then publish with NativeAOT as usual, for example:

```powershell
dotnet publish -c Release -r win-x64 -p:PublishAot=true
```

Requirements for source static mode:

- `cmake` must be available on `PATH`
- a local C/C++ toolchain compatible with the target RID must be installed

An end-to-end sample is available in [samples/TinyEXR.NativeAot.SourceStatic](samples/TinyEXR.NativeAot.SourceStatic).

## TODO

### multi-part wrapper

### No P/Invoke?

I think port C++ to C# is not difficult. Thus, we obtain better compatibility.

Why not?

Of course, this takes a lot of time...

## License

`TinyEXR.NET` is under MIT license
