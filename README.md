# TinyEXR.NET

`TinyEXR.NET` is a C# wrapper of C++ library [tinyexr](https://github.com/syoyo/tinyexr)

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

Currently, only `win-x64`, `linux-x64` and `osx-x64` are available. If you want use this lib on other platforms, you have to build them by your self.

## TODO

multi-part wrapper

## License

`TinyEXR.NET` is under MIT license
