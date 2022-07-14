# TinyEXR.NET

`TinyEXR.NET` is a C# wrapper of single header-only C++ library [tinyexr](https://github.com/syoyo/tinyexr)

`tinyexr` is a portable single header-only C++ library to load and save OpenEXR (.exr) images

The target framework of `TinyEXR.NET`  is `.NET Standard 2.1`

**WARNING:**  This library is WIP. API is unstable

## Download

`TinyEXR.NET` can be found on NuGet [![NuGet](https://img.shields.io/nuget/v/TinyEXR.NET)](https://www.nuget.org/packages/TinyEXR.NET) (← click it !)

## Supported Platforms

Key:

* ✅: completed
* 🚧: work in progress
* ⌛: planned, not yet started
* ❌: no plan

| Platform      | State |
| ------------- | ----- |
| Windows x64   | 🚧     |
| Windows x86   | ❌     |
| Windows arm64 | ❌     |
| Linux x64     | ⌛     |
| Linux x86     | ❌     |
| Linux arm64   | ❌     |
| macOS x64     | ⌛     |
| macOS arm64   | ❌     |

## Usage

WIP

## Development build

### Windows

#### 0. Requirements

* MSVC that supported C++11 (recommend VS2019 or higher)
* Powershell (>= 3.0)
* CMake (>= 3.0.0)
* .NET SDK x64 (>= .NET Core 3.0)

#### 1. Download the code

First, clone this project or download the latest release and unzip.

#### 2. Build native library

Run `build_win-x64.ps1`

#### 3. Build TinyEXR.NET

You can use dotnet command in `TinyEXR.NET` folder: `dotnet build --configuration Release`

Or, you can open `TinyEXR.NET/TinyEXR.NET.sln` in VS or any other IDE and compile

#### 4. Finish

Finally, we can find NuGet package in `TinyEXR.NET\bin\Release`. You can use it anywhere

## Details

Although [tinyexr](https://github.com/syoyo/tinyexr) provides C interface, the MSVC compiler does not export any symbols by default.

So we have to create a C++ project in the folder `native` to wrap tinyexr API.

I don't know if there is any better way. But it works. :)

The reason why we only support x64 platform is that I don't know whether the existing code will work properly if we support x86 or other platforms. Specifically, Is the struct layout at C++ side consistent with C# side? I don't have time to solve this problem at present. So I just give up these platform.

Contribution is welcome!

## License

`TinyEXR.NET` is under MIT license

and wrapped C++ lib `tinyexr` is under 3-clause BSD

