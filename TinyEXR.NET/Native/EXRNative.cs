using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace TinyEXR.Native
{
    public static unsafe partial class EXRNative
    {
        public const string LibraryName = "TinyEXRNative";

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRInternal(float** out_rgba, int* width, int* height, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRWithLayerInternal(float** out_rgba, int* width, int* height, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char *")] sbyte* layer_name, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int EXRLayersInternal([NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **[]")] sbyte*** layer_names, int* num_layers, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int IsEXRInternal([NativeTypeName("const char *")] sbyte* filename);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int IsEXRFromMemoryInternal([NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRToMemoryInternal([NativeTypeName("const float *")] float* data, [NativeTypeName("const int")] int width, [NativeTypeName("const int")] int height, [NativeTypeName("const int")] int components, [NativeTypeName("const int")] int save_as_fp16, [NativeTypeName("unsigned char **")] byte** buffer, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRInternal([NativeTypeName("const float *")] float* data, [NativeTypeName("const int")] int width, [NativeTypeName("const int")] int height, [NativeTypeName("const int")] int components, [NativeTypeName("const int")] int save_as_fp16, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int EXRNumLevelsInternal([NativeTypeName("const EXRImage *")] EXRImage* exr_image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void InitEXRHeaderInternal(EXRHeader* exr_header);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void EXRSetNameAttrInternal(EXRHeader* exr_header, [NativeTypeName("const char *")] sbyte* name);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void InitEXRImageInternal(EXRImage* exr_image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int FreeEXRHeaderInternal(EXRHeader* exr_header);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int FreeEXRImageInternal(EXRImage* exr_image);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void FreeEXRErrorMessageInternal([NativeTypeName("const char *")] sbyte* msg);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRVersionFromFileInternal(EXRVersion* version, [NativeTypeName("const char *")] sbyte* filename);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRVersionFromMemoryInternal(EXRVersion* version, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRHeaderFromFileInternal(EXRHeader* header, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRHeaderFromMemoryInternal(EXRHeader* header, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRMultipartHeaderFromFileInternal(EXRHeader*** headers, int* num_headers, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRMultipartHeaderFromMemoryInternal(EXRHeader*** headers, int* num_headers, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRImageFromFileInternal(EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* header, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRImageFromMemoryInternal(EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* header, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("const size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRMultipartImageFromFileInternal(EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRMultipartImageFromMemoryInternal(EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("const size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRImageToFileInternal([NativeTypeName("const EXRImage *")] EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* exr_header, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("size_t")]
        public static extern UIntPtr SaveEXRImageToMemoryInternal([NativeTypeName("const EXRImage *")] EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* exr_header, [NativeTypeName("unsigned char **")] byte** memory, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRMultipartImageToFileInternal([NativeTypeName("const EXRImage *")] EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** exr_headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("size_t")]
        public static extern UIntPtr SaveEXRMultipartImageToMemoryInternal([NativeTypeName("const EXRImage *")] EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** exr_headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("unsigned char **")] byte** memory, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadDeepEXRInternal(DeepImage* out_image, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRFromMemoryInternal(float** out_rgba, int* width, int* height, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void FreeInternal(void* ptr);

        [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("size_t")]
        public static extern UIntPtr StrLenInternal([NativeTypeName("const char *")] sbyte* str);

        [NativeTypeName("#define TINYEXR_SUCCESS (0)")]
        public const int TINYEXR_SUCCESS = 0;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_MAGIC_NUMBER (-1)")]
        public const int TINYEXR_ERROR_INVALID_MAGIC_NUMBER = -1;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_EXR_VERSION (-2)")]
        public const int TINYEXR_ERROR_INVALID_EXR_VERSION = -2;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_ARGUMENT (-3)")]
        public const int TINYEXR_ERROR_INVALID_ARGUMENT = -3;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_DATA (-4)")]
        public const int TINYEXR_ERROR_INVALID_DATA = -4;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_FILE (-5)")]
        public const int TINYEXR_ERROR_INVALID_FILE = -5;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_PARAMETER (-6)")]
        public const int TINYEXR_ERROR_INVALID_PARAMETER = -6;

        [NativeTypeName("#define TINYEXR_ERROR_CANT_OPEN_FILE (-7)")]
        public const int TINYEXR_ERROR_CANT_OPEN_FILE = -7;

        [NativeTypeName("#define TINYEXR_ERROR_UNSUPPORTED_FORMAT (-8)")]
        public const int TINYEXR_ERROR_UNSUPPORTED_FORMAT = -8;

        [NativeTypeName("#define TINYEXR_ERROR_INVALID_HEADER (-9)")]
        public const int TINYEXR_ERROR_INVALID_HEADER = -9;

        [NativeTypeName("#define TINYEXR_ERROR_UNSUPPORTED_FEATURE (-10)")]
        public const int TINYEXR_ERROR_UNSUPPORTED_FEATURE = -10;

        [NativeTypeName("#define TINYEXR_ERROR_CANT_WRITE_FILE (-11)")]
        public const int TINYEXR_ERROR_CANT_WRITE_FILE = -11;

        [NativeTypeName("#define TINYEXR_ERROR_SERIALIZATION_FAILED (-12)")]
        public const int TINYEXR_ERROR_SERIALIZATION_FAILED = -12;

        [NativeTypeName("#define TINYEXR_ERROR_LAYER_NOT_FOUND (-13)")]
        public const int TINYEXR_ERROR_LAYER_NOT_FOUND = -13;

        [NativeTypeName("#define TINYEXR_ERROR_DATA_TOO_LARGE (-14)")]
        public const int TINYEXR_ERROR_DATA_TOO_LARGE = -14;

        [NativeTypeName("#define TINYEXR_PIXELTYPE_UINT (0)")]
        public const int TINYEXR_PIXELTYPE_UINT = 0;

        [NativeTypeName("#define TINYEXR_PIXELTYPE_HALF (1)")]
        public const int TINYEXR_PIXELTYPE_HALF = 1;

        [NativeTypeName("#define TINYEXR_PIXELTYPE_FLOAT (2)")]
        public const int TINYEXR_PIXELTYPE_FLOAT = 2;

        [NativeTypeName("#define TINYEXR_MAX_HEADER_ATTRIBUTES (1024)")]
        public const int TINYEXR_MAX_HEADER_ATTRIBUTES = 1024;

        [NativeTypeName("#define TINYEXR_MAX_CUSTOM_ATTRIBUTES (128)")]
        public const int TINYEXR_MAX_CUSTOM_ATTRIBUTES = 128;

        [NativeTypeName("#define TINYEXR_COMPRESSIONTYPE_NONE (0)")]
        public const int TINYEXR_COMPRESSIONTYPE_NONE = 0;

        [NativeTypeName("#define TINYEXR_COMPRESSIONTYPE_RLE (1)")]
        public const int TINYEXR_COMPRESSIONTYPE_RLE = 1;

        [NativeTypeName("#define TINYEXR_COMPRESSIONTYPE_ZIPS (2)")]
        public const int TINYEXR_COMPRESSIONTYPE_ZIPS = 2;

        [NativeTypeName("#define TINYEXR_COMPRESSIONTYPE_ZIP (3)")]
        public const int TINYEXR_COMPRESSIONTYPE_ZIP = 3;

        [NativeTypeName("#define TINYEXR_COMPRESSIONTYPE_PIZ (4)")]
        public const int TINYEXR_COMPRESSIONTYPE_PIZ = 4;

        [NativeTypeName("#define TINYEXR_COMPRESSIONTYPE_ZFP (128)")]
        public const int TINYEXR_COMPRESSIONTYPE_ZFP = 128;

        [NativeTypeName("#define TINYEXR_ZFP_COMPRESSIONTYPE_RATE (0)")]
        public const int TINYEXR_ZFP_COMPRESSIONTYPE_RATE = 0;

        [NativeTypeName("#define TINYEXR_ZFP_COMPRESSIONTYPE_PRECISION (1)")]
        public const int TINYEXR_ZFP_COMPRESSIONTYPE_PRECISION = 1;

        [NativeTypeName("#define TINYEXR_ZFP_COMPRESSIONTYPE_ACCURACY (2)")]
        public const int TINYEXR_ZFP_COMPRESSIONTYPE_ACCURACY = 2;

        [NativeTypeName("#define TINYEXR_TILE_ONE_LEVEL (0)")]
        public const int TINYEXR_TILE_ONE_LEVEL = 0;

        [NativeTypeName("#define TINYEXR_TILE_MIPMAP_LEVELS (1)")]
        public const int TINYEXR_TILE_MIPMAP_LEVELS = 1;

        [NativeTypeName("#define TINYEXR_TILE_RIPMAP_LEVELS (2)")]
        public const int TINYEXR_TILE_RIPMAP_LEVELS = 2;

        [NativeTypeName("#define TINYEXR_TILE_ROUND_DOWN (0)")]
        public const int TINYEXR_TILE_ROUND_DOWN = 0;

        [NativeTypeName("#define TINYEXR_TILE_ROUND_UP (1)")]
        public const int TINYEXR_TILE_ROUND_UP = 1;
    }
}
