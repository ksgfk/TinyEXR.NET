using System;
using System.Runtime.InteropServices;
using System.Security;

namespace TinyEXR
{
    public unsafe static class Native
    {
        public const string DllName = "TinyEXR.Native.dll";

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRInternal(float** out_rgba, int* width, int* height, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRWithLayerInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRWithLayerInternal(float** out_rgba, int* width, int* height, byte* filename, byte* layer_name, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "EXRLayersInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int EXRLayersInternal(byte* filename, sbyte*** layer_names, int* num_layers, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "IsEXRInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int IsEXRInternal(byte* filename);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "IsEXRFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int IsEXRFromMemoryInternal(byte* memory, ulong size);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "SaveEXRToMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SaveEXRToMemoryInternal(float* data, int width, int height, int components, int save_as_fp16, byte** buffer, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "SaveEXRInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SaveEXRInternal(float* data, int width, int height, int components, int save_as_fp16, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "EXRNumLevelsInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int EXRNumLevelsInternal(IntPtr exr_image);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "InitEXRHeaderInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void InitEXRHeaderInternal(IntPtr exr_header);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "EXRSetNameAttrInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void EXRSetNameAttrInternal(IntPtr exr_header, byte* name);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "InitEXRImageInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void InitEXRImageInternal(IntPtr exr_image);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "FreeEXRHeaderInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FreeEXRHeaderInternal(IntPtr exr_header);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "FreeEXRImageInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int FreeEXRImageInternal(IntPtr exr_image);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "FreeEXRErrorMessageInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeEXRErrorMessageInternal(byte* msg);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "ParseEXRVersionFromFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ParseEXRVersionFromFileInternal(IntPtr version, byte* filename);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "ParseEXRVersionFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ParseEXRVersionFromMemoryInternal(IntPtr version, byte* memory, ulong size);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "ParseEXRHeaderFromFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ParseEXRHeaderFromFileInternal(IntPtr header, IntPtr version, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "ParseEXRHeaderFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ParseEXRHeaderFromMemoryInternal(IntPtr header, IntPtr version, byte* memory, ulong size, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "ParseEXRMultipartHeaderFromFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ParseEXRMultipartHeaderFromFileInternal(IntPtr headers, int* num_headers, IntPtr version, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "ParseEXRMultipartHeaderFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int ParseEXRMultipartHeaderFromMemoryInternal(IntPtr headers, int* num_headers, IntPtr version, byte* memory, ulong size, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRImageFromFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRImageFromFileInternal(IntPtr image, IntPtr header, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRImageFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRImageFromMemoryInternal(IntPtr image, IntPtr header, byte* memory, ulong size, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRMultipartImageFromFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRMultipartImageFromFileInternal(IntPtr images, IntPtr headers, uint num_parts, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRMultipartImageFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRMultipartImageFromMemoryInternal(IntPtr images, IntPtr headers, uint num_parts, byte* memory, ulong size, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "SaveEXRImageToFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SaveEXRImageToFileInternal(IntPtr image, IntPtr exr_header, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "SaveEXRImageToMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong SaveEXRImageToMemoryInternal(IntPtr image, IntPtr exr_header, byte** memory, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "SaveEXRMultipartImageToFileInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int SaveEXRMultipartImageToFileInternal(IntPtr images, IntPtr exr_headers, uint num_parts, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "SaveEXRMultipartImageToMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern ulong SaveEXRMultipartImageToMemoryInternal(IntPtr images, IntPtr exr_headers, uint num_parts, byte** memory, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadDeepEXRInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadDeepEXRInternal(IntPtr out_image, byte* filename, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "LoadEXRFromMemoryInternal", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int LoadEXRFromMemoryInternal(float** out_rgba, int* width, int* height, byte* memory, ulong size, sbyte** err);

        [SuppressUnmanagedCodeSecurity, DllImport(DllName, EntryPoint = "GlobalFree", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void GlobalFree(void* ptr);

        [StructLayout(LayoutKind.Sequential, Size = 48)]
        public struct EXRImage
        {
            internal IntPtr tiles;
            internal IntPtr next_level;
            internal int level_x;
            internal int level_y;
            internal IntPtr images;
            internal int width;
            internal int height;
            internal int num_channels;
            internal int num_tiles;
        }

        [StructLayout(LayoutKind.Sequential, Size = 392)]
        public struct EXRHeader
        {
            internal float pixel_aspect_ratio;
            internal int line_order;
            internal EXRBox2i data_window;
            internal EXRBox2i display_window;
            internal fixed float screen_window_center[2];
            internal float screen_window_width;
            internal int chunk_count;
            internal int tiled;
            internal int tile_size_x;
            internal int tile_size_y;
            internal int tile_level_mode;
            internal int tile_rounding_mode;
            internal int long_name;
            internal int non_image;
            internal int multipart;
            internal uint header_len;
            internal int num_custom_attributes;
            internal IntPtr custom_attributes;
            internal IntPtr channels;
            internal IntPtr pixel_types;
            internal int num_channels;
            internal int compression_type;
            internal IntPtr requested_pixel_types;
            internal fixed sbyte name[256];
        }

        [StructLayout(LayoutKind.Sequential, Size = 20)]
        public struct EXRVersion
        {
            internal int version;
            internal int tiled;
            internal int long_name;
            internal int non_image;
            internal int multipart;
        }

        [StructLayout(LayoutKind.Sequential, Size = 40)]
        public partial struct DeepImage
        {
            internal IntPtr channel_names;
            internal IntPtr image;
            internal IntPtr offset_table;
            internal int num_channels;
            internal int width;
            internal int height;
            internal int pad0;
        }

        [StructLayout(LayoutKind.Sequential, Size = 16)]
        public partial struct EXRBox2i
        {
            internal int min_x;
            internal int min_y;
            internal int max_x;
            internal int max_y;
        }
    }
}
