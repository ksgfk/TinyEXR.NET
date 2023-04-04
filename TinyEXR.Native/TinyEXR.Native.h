#ifndef __TINYEXR_NATIVE__
#define __TINYEXR_NATIVE__

#ifdef _WIN32
#define EXPORT_API __declspec(dllexport)
#else
#define EXPORT_API __attribute__((visibility("default")))
#endif

#if defined(_WIN32)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#endif

#include "zlib/zlib.h"

#define TINYEXR_USE_MINIZ 0
#define TINYEXR_USE_OPENMP 0
#include "tinyexr/tinyexr.h"

#ifdef __cplusplus
extern "C" {
#endif

EXPORT_API int LoadEXRInternal(float** out_rgba, int* width, int* height,
                               const char* filename, const char** err);
EXPORT_API int LoadEXRWithLayerInternal(float** out_rgba, int* width, int* height,
                                        const char* filename, const char* layer_name,
                                        const char** err);
EXPORT_API int EXRLayersInternal(const char* filename, const char** layer_names[],
                                 int* num_layers, const char** err);
EXPORT_API int IsEXRInternal(const char* filename);
EXPORT_API int IsEXRFromMemoryInternal(const unsigned char* memory, size_t size);
EXPORT_API int SaveEXRToMemoryInternal(const float* data, const int width, const int height,
                                       const int components, const int save_as_fp16,
                                       const unsigned char** buffer, const char** err);
EXPORT_API int SaveEXRInternal(const float* data, const int width, const int height,
                               const int components, const int save_as_fp16,
                               const char* filename, const char** err);
EXPORT_API int EXRNumLevelsInternal(const EXRImage* exr_image);
EXPORT_API void InitEXRHeaderInternal(EXRHeader* exr_header);
EXPORT_API void EXRSetNameAttrInternal(EXRHeader* exr_header, const char* name);
EXPORT_API void InitEXRImageInternal(EXRImage* exr_image);
EXPORT_API int FreeEXRHeaderInternal(EXRHeader* exr_header);
EXPORT_API int FreeEXRImageInternal(EXRImage* exr_image);
EXPORT_API void FreeEXRErrorMessageInternal(const char* msg);
EXPORT_API int ParseEXRVersionFromFileInternal(EXRVersion* version, const char* filename);
EXPORT_API int ParseEXRVersionFromMemoryInternal(EXRVersion* version,
                                                 const unsigned char* memory, size_t size);
EXPORT_API int ParseEXRHeaderFromFileInternal(EXRHeader* header, const EXRVersion* version,
                                              const char* filename, const char** err);
EXPORT_API int ParseEXRHeaderFromMemoryInternal(EXRHeader* header,
                                                const EXRVersion* version,
                                                const unsigned char* memory, size_t size,
                                                const char** err);
EXPORT_API int ParseEXRMultipartHeaderFromFileInternal(EXRHeader*** headers,
                                                       int* num_headers,
                                                       const EXRVersion* version,
                                                       const char* filename,
                                                       const char** err);
EXPORT_API int ParseEXRMultipartHeaderFromMemoryInternal(EXRHeader*** headers,
                                                         int* num_headers,
                                                         const EXRVersion* version,
                                                         const unsigned char* memory,
                                                         size_t size, const char** err);
EXPORT_API int LoadEXRImageFromFileInternal(EXRImage* image, const EXRHeader* header,
                                            const char* filename, const char** err);
EXPORT_API int LoadEXRImageFromMemoryInternal(EXRImage* image, const EXRHeader* header,
                                              const unsigned char* memory,
                                              const size_t size, const char** err);
EXPORT_API int LoadEXRMultipartImageFromFileInternal(EXRImage* images,
                                                     const EXRHeader** headers,
                                                     unsigned int num_parts,
                                                     const char* filename,
                                                     const char** err);
EXPORT_API int LoadEXRMultipartImageFromMemoryInternal(EXRImage* images,
                                                       const EXRHeader** headers,
                                                       unsigned int num_parts,
                                                       const unsigned char* memory,
                                                       const size_t size, const char** err);
EXPORT_API int SaveEXRImageToFileInternal(const EXRImage* image,
                                          const EXRHeader* exr_header, const char* filename,
                                          const char** err);
EXPORT_API size_t SaveEXRImageToMemoryInternal(const EXRImage* image,
                                               const EXRHeader* exr_header,
                                               unsigned char** memory, const char** err);
EXPORT_API int SaveEXRMultipartImageToFileInternal(const EXRImage* images,
                                                   const EXRHeader** exr_headers,
                                                   unsigned int num_parts,
                                                   const char* filename, const char** err);
EXPORT_API size_t SaveEXRMultipartImageToMemoryInternal(const EXRImage* images,
                                                        const EXRHeader** exr_headers,
                                                        unsigned int num_parts,
                                                        unsigned char** memory, const char** err);
EXPORT_API int LoadDeepEXRInternal(DeepImage* out_image, const char* filename,
                                   const char** err);
EXPORT_API int LoadEXRFromMemoryInternal(float** out_rgba, int* width, int* height,
                                         const unsigned char* memory, size_t size,
                                         const char** err);

EXPORT_API void FreeInternal(void* ptr);
EXPORT_API size_t StrLenInternal(const char* str);

#ifdef __cplusplus
}
#endif

#endif
