# TinyEXR.NET

`TinyEXR.NET` is a C# wrapper of single header-only C++ library [tinyexr](https://github.com/syoyo/tinyexr)

`tinyexr` is a portable single header-only C++ library to load and save OpenEXR (.exr) images

The target framework of `TinyEXR.NET`  is `.NET Standard 2.1`

## Download

`TinyEXR.NET` can be found on NuGet [![NuGet](https://img.shields.io/nuget/v/TinyEXR.NET)](https://www.nuget.org/packages/TinyEXR.NET) (← click it !)

## Supported Platforms

Key:

* ✅: completed
* 🚧: work in progress
* ⌛: planned, not yet started
* ❌: no plan

| Platform    | State |
| ----------- | ----- |
| Windows x64 | 🚧     |
| Linux x64   | ⌛     |

Unlisted platforms are also unplanned.

But you can contribute to support any other platform! :)

## Usage

TODO...

## Development build

TODO...

## Details

I use [CppSharp](https://github.com/mono/CppSharp) to generate binding code. Then write glue code by hand, because generated code are too heavy...

CppSharp does not generate struct defined by tinyexr. So, to make CppSharp happy, I separate the definition and implementation of tinyexr. It's too troublesome. Is there a simpler way?

## License

`TinyEXR.NET` is under MIT license

and wrapped C++ lib `tinyexr` is under 3-clause BSD

