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
| Linux x64   | 🚧     |

Unlisted platforms are also unplanned.

But you can contribute to support any other platform! :)

## Usage

### Simple read/write

```c#
TinyEXR.Exr.LoadFromFile("114514.exr", out var rgba, out int width, out int height);

float[] data = new float[1919 * 810 * 3];
TinyEXR.Exr.SaveToFile(data, 1919, 810, 3, false, "a.exr");
```

### Read layer file

```c#
TinyEXR.Exr.LoadFromFileWithLayers("4396.exr", "777", out var rgba, out int width, out int height);
```

### Read single part file

```c#
using var image = new TinyEXR.ExrImageReader("0721.exr");
var b = image.GetPixels(0);
var g = image.GetPixels(1);
var r = image.GetPixels(2);
```

### Write scanline file

```c#
var save = new ExrImageWriter(3, image.Width, image.Height);
save.SetChannel(0, "B", b);
save.SetChannel(1, "G", g);
save.SetChannel(2, "R", r);
var result = save.WriteToFile("1551.exr");
```

## Development build

### Windows

my environment is:

* MSVC v143
* CMake
* .NET 6

```bash
cd TinyEXR.Native
mkdir build
cd build
cmake ..
xcopy /Y zlib\zconf.h ..
cmake --build . --config Release
xcopy /Y Release\TinyEXR.Native.dll ..\..\TinyEXR.NET\Assets\runtimes\win-x64\native
cd ..\..\TinyEXR.NET
dotnet build --configuration Release
```

### Linux

my environment is:

* clang 14
* CMake
* .NET 6

```bash
cd TinyEXR.Native/zlib
mkdir build
cd build
cmake .. -DCMAKE_C_COMPILER=clang-14 -DCMAKE_C_FLAGS="-fPIC"
cmake --build . --config Release
sudo make install
cd ../../
mkdir build
cd build
cmake .. -DCMAKE_C_COMPILER=clang-14 -DCMAKE_CXX_COMPILER=clang++-14
cmake --build . --config Release
cp libTinyEXR.Native.so ../../TinyEXR.NET/Assets/runtimes/linux-x64/native/
dotnet build --configuration Release
```

if you have installed zlib, you can skip some step

## Details

I use [CppSharp](https://github.com/mono/CppSharp) to generate binding code. Then write glue code by hand, because generated code are too heavy...

CppSharp does not generate struct defined by tinyexr. So, to make CppSharp happy, I separate the definition and implementation of tinyexr. It's too troublesome. Is there a simpler way?

## License

`TinyEXR.NET` is under MIT license

and wrapped C++ lib `tinyexr` is under 3-clause BSD

