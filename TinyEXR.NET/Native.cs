using System;
using System.Runtime.InteropServices;

namespace TinyEXR
{
    public static unsafe partial class Native
    {
        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRInternal(float** out_rgba, int* width, int* height, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRWithLayerInternal(float** out_rgba, int* width, int* height, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char *")] sbyte* layer_name, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int EXRLayersInternal([NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **[]")] sbyte*** layer_names, int* num_layers, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int IsEXRInternal([NativeTypeName("const char *")] sbyte* filename);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int IsEXRFromMemoryInternal([NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRToMemoryInternal([NativeTypeName("const float *")] float* data, [NativeTypeName("const int")] int width, [NativeTypeName("const int")] int height, [NativeTypeName("const int")] int components, [NativeTypeName("const int")] int save_as_fp16, [NativeTypeName("const unsigned char **")] byte** buffer, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRInternal([NativeTypeName("const float *")] float* data, [NativeTypeName("const int")] int width, [NativeTypeName("const int")] int height, [NativeTypeName("const int")] int components, [NativeTypeName("const int")] int save_as_fp16, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int EXRNumLevelsInternal([NativeTypeName("const EXRImage *")] EXRImage* exr_image);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void InitEXRHeaderInternal(EXRHeader* exr_header);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void EXRSetNameAttrInternal(EXRHeader* exr_header, [NativeTypeName("const char *")] sbyte* name);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void InitEXRImageInternal(EXRImage* exr_image);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int FreeEXRHeaderInternal(EXRHeader* exr_header);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int FreeEXRImageInternal(EXRImage* exr_image);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void FreeEXRErrorMessageInternal([NativeTypeName("const char *")] sbyte* msg);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRVersionFromFileInternal(EXRVersion* version, [NativeTypeName("const char *")] sbyte* filename);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRVersionFromMemoryInternal(EXRVersion* version, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRHeaderFromFileInternal(EXRHeader* header, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRHeaderFromMemoryInternal(EXRHeader* header, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRMultipartHeaderFromFileInternal(EXRHeader*** headers, int* num_headers, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int ParseEXRMultipartHeaderFromMemoryInternal(EXRHeader*** headers, int* num_headers, [NativeTypeName("const EXRVersion *")] EXRVersion* version, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRImageFromFileInternal(EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* header, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRImageFromMemoryInternal(EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* header, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("const size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRMultipartImageFromFileInternal(EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRMultipartImageFromMemoryInternal(EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("const size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRImageToFileInternal([NativeTypeName("const EXRImage *")] EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* exr_header, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("size_t")]
        public static extern UIntPtr SaveEXRImageToMemoryInternal([NativeTypeName("const EXRImage *")] EXRImage* image, [NativeTypeName("const EXRHeader *")] EXRHeader* exr_header, [NativeTypeName("unsigned char **")] byte** memory, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int SaveEXRMultipartImageToFileInternal([NativeTypeName("const EXRImage *")] EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** exr_headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        [return: NativeTypeName("size_t")]
        public static extern UIntPtr SaveEXRMultipartImageToMemoryInternal([NativeTypeName("const EXRImage *")] EXRImage* images, [NativeTypeName("const EXRHeader **")] EXRHeader** exr_headers, [NativeTypeName("unsigned int")] uint num_parts, [NativeTypeName("unsigned char **")] byte** memory, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadDeepEXRInternal(DeepImage* out_image, [NativeTypeName("const char *")] sbyte* filename, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern int LoadEXRFromMemoryInternal(float** out_rgba, int* width, int* height, [NativeTypeName("const unsigned char *")] byte* memory, [NativeTypeName("size_t")] UIntPtr size, [NativeTypeName("const char **")] sbyte** err);

        [DllImport("TinyEXR.Native", CallingConvention = CallingConvention.Cdecl, ExactSpelling = true)]
        public static extern void FreeInternal(void* ptr);
    }
}
