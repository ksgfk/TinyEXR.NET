using System;
using System.Runtime.InteropServices;

namespace TinyEXR.NET
{
    public enum ResultType
    {
        Success = 0,
        InvalidMagicNumver = -1,
        InvalidExrVersion = -2,
        InvalidArgument = -3,
        InvalidData = -4,
        InvalidFile = -5,
        InvalidParameter = -6,
        CannotOpenFile = -7,
        UnsupportedFormat = -8,
        InvalidHeader = -9,
        UnsupportedFeature = -10,
        CannotWriteFile = -11,
        SerialzationFailed = -12,
        LayerNotFound = -13,
        DataTooLarge = -14
    }

    public unsafe static class Native
    {
        public const string LibraryName = "TinyEXR.NET.Native";

        [DllImport(LibraryName)]
        public static extern int LoadEXR_Export(
            float** out_rgba,
            int* width,
            int* height,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadEXRWithLayer_Export(
            float** out_rgba,
            int* width,
            int* height,
            byte* filename,
            byte* layername,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int IsEXR_Export(char* filename);

        [DllImport(LibraryName)]
        public static extern int SaveEXRToMemory_Export(
            float* data,
            int width,
            int height,
            int components,
            int save_as_fp16,
            byte** outbuf,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int SaveEXR_Export(
            float* data,
            int width,
            int height,
            int components,
            int save_as_fp16,
            byte* outfilename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int EXRNumLevels_Export(ExrImage* exr_image);

        [DllImport(LibraryName)]
        public static extern void InitEXRHeader_Export(ExrHeader* exr_header);

        [DllImport(LibraryName)]
        public static extern void EXRSetNameAttr_Export(ExrHeader* exr_header, byte* name);

        [DllImport(LibraryName)]
        public static extern void InitEXRImage_Export(ExrImage* exr_image);

        [DllImport(LibraryName)]
        public static extern int FreeEXRHeader_Export(ExrHeader* exr_header);

        [DllImport(LibraryName)]
        public static extern int FreeEXRImage_Export(ExrImage* exr_image);

        [DllImport(LibraryName)]
        public static extern void FreeEXRErrorMessage_Export(byte* msg);

        [DllImport(LibraryName)]
        public static extern int ParseEXRVersionFromFile_Export(ExrVersion* version, byte* filename);

        //I don't know if using UIntPtr is the best way
        [DllImport(LibraryName)]
        public static extern int ParseEXRVersionFromMemory_Export(
            ExrVersion* version,
            byte* memory,
            UIntPtr size);

        [DllImport(LibraryName)]
        public static extern int ParseEXRHeaderFromFile_Export(
            ExrHeader* header,
            ExrVersion* version,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int ParseEXRHeaderFromMemory_Export(
            ExrHeader* header,
            ExrVersion* version,
            byte* memory,
            UIntPtr size,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int ParseEXRMultipartHeaderFromFile_Export(
            ExrHeader*** headers,
            int* num_headers,
            ExrVersion* version,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int ParseEXRMultipartHeaderFromMemory_Export(
            ExrHeader*** headers,
            int* num_headers,
            ExrVersion* version,
            byte* memory,
            UIntPtr size,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadEXRImageFromFile_Export(
            ExrImage* image,
            ExrHeader* header,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadEXRImageFromMemory_Export(
            ExrImage* image,
            ExrHeader* header,
            byte* memory,
            UIntPtr size,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadEXRMultipartImageFromFile_Export(
            ExrImage* images,
            ExrHeader** headers,
            uint num_parts,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadEXRMultipartImageFromMemory_Export(
            ExrImage* images,
            ExrHeader** headers,
            uint num_parts,
            byte* memory,
            UIntPtr size,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int SaveEXRImageToFile_Export(
            ExrImage* image,
            ExrHeader* exr_header,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern UIntPtr SaveEXRImageToMemory_Export(
            ExrImage* image,
            ExrHeader* exr_header,
            byte** memory,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int SaveEXRMultipartImageToFile_Export(
            ExrImage* images,
            ExrHeader** exr_headers,
            uint num_parts,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern UIntPtr SaveEXRMultipartImageToMemory_Export(
            ExrImage* images,
            ExrHeader** exr_headers,
            uint num_parts,
            byte** memory,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadDeepEXR_Export(
            DeepImage* out_image,
            byte* filename,
            byte** err);

        [DllImport(LibraryName)]
        public static extern int LoadEXRFromMemory_Export(
            float** out_rgba,
            int* width,
            int* height,
            byte* memory,
            UIntPtr size,
            byte** err);

        [DllImport(LibraryName)]
        public static extern void FreeImageData(float* rgba);

        [DllImport(LibraryName)]
        public static extern void FreeMemory(byte* memory);
    }
}
