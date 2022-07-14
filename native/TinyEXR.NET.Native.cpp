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

#include <cstdlib> //for free()
#define TINYEXR_USE_OPENMP 0
#define TINYEXR_IMPLEMENTATION
#include "tinyexr/tinyexr.h"

#ifdef __cplusplus
extern "C" {
#endif

// *****************************
// * tinyexr functions wrapper *
// *****************************
EXPORT_API int LoadEXR_Export(float** out_rgba, int* width, int* height,
                              const char* filename, const char** err) {
  return LoadEXR(out_rgba, width, height, filename, err);
}
EXPORT_API int LoadEXRWithLayer_Export(float** out_rgba, int* width,
                                       int* height, const char* filename,
                                       const char* layername,
                                       const char** err) {
  return LoadEXRWithLayer(out_rgba, width, height, filename, layername, err);
}
EXPORT_API int EXRLayers_Export(const char* filename,
                                const char** layer_names[], int* num_layers,
                                const char** err) {
  return EXRLayers(filename, layer_names, num_layers, err);
}
EXPORT_API int IsEXR_Export(const char* filename) { return IsEXR(filename); }
EXPORT_API int SaveEXRToMemory_Export(const float* data, const int width,
                                      const int height, const int components,
                                      const int save_as_fp16,
                                      const unsigned char** outbuf,
                                      const char** err) {
  return SaveEXRToMemory(data, width, height, components, save_as_fp16, outbuf, err);
}
EXPORT_API int SaveEXR_Export(const float* data, const int width, const int height,
                              const int components, const int save_as_fp16,
                              const char* outfilename, const char** err) {
  return SaveEXR(data, width, height, components, save_as_fp16, outfilename, err);
}
EXPORT_API int EXRNumLevels_Export(const EXRImage* exr_image) {
  return EXRNumLevels(exr_image);
}
EXPORT_API void InitEXRHeader_Export(EXRHeader* exr_header) {
  InitEXRHeader(exr_header);
}
EXPORT_API void EXRSetNameAttr_Export(EXRHeader* exr_header, const char* name) {
  EXRSetNameAttr(exr_header, name);
}
EXPORT_API void InitEXRImage_Export(EXRImage* exr_image) {
  InitEXRImage(exr_image);
}
EXPORT_API int FreeEXRHeader_Export(EXRHeader* exr_header) {
  return FreeEXRHeader(exr_header);
}
EXPORT_API int FreeEXRImage_Export(EXRImage* exr_image) {
  return FreeEXRImage(exr_image);
}
EXPORT_API void FreeEXRErrorMessage_Export(const char* msg) {
  FreeEXRErrorMessage(msg);
}
EXPORT_API int ParseEXRVersionFromFile_Export(EXRVersion* version, const char* filename) {
  return ParseEXRVersionFromFile(version, filename);
}
EXPORT_API int ParseEXRVersionFromMemory_Export(EXRVersion* version, const unsigned char* memory, size_t size) {
  return ParseEXRVersionFromMemory(version, memory, size);
}
EXPORT_API int ParseEXRHeaderFromFile_Export(EXRHeader* header, const EXRVersion* version,
                                             const char* filename, const char** err) {
  return ParseEXRHeaderFromFile(header, version, filename, err);
}
EXPORT_API int ParseEXRHeaderFromMemory_Export(EXRHeader* header,
                                               const EXRVersion* version,
                                               const unsigned char* memory, size_t size,
                                               const char** err) {
  return ParseEXRHeaderFromMemory(header, version, memory, size, err);
}
EXPORT_API int ParseEXRMultipartHeaderFromFile_Export(EXRHeader*** headers,
                                                      int* num_headers,
                                                      const EXRVersion* version,
                                                      const char* filename,
                                                      const char** err) {
  return ParseEXRMultipartHeaderFromFile(headers, num_headers, version, filename, err);
}
EXPORT_API int ParseEXRMultipartHeaderFromMemory_Export(EXRHeader*** headers,
                                                        int* num_headers,
                                                        const EXRVersion* version,
                                                        const unsigned char* memory,
                                                        size_t size, const char** err) {
  return ParseEXRMultipartHeaderFromMemory(headers, num_headers, version, memory, size, err);
}
EXPORT_API int LoadEXRImageFromFile_Export(EXRImage* image, const EXRHeader* header,
                                           const char* filename, const char** err) {
  return LoadEXRImageFromFile(image, header, filename, err);
}
EXPORT_API int LoadEXRImageFromMemory_Export(EXRImage* image, const EXRHeader* header,
                                             const unsigned char* memory,
                                             const size_t size, const char** err) {
  return LoadEXRImageFromMemory(image, header, memory, size, err);
}
EXPORT_API int LoadEXRMultipartImageFromFile_Export(EXRImage* images,
                                                    const EXRHeader** headers,
                                                    unsigned int num_parts,
                                                    const char* filename,
                                                    const char** err) {
  return LoadEXRMultipartImageFromFile(images, headers, num_parts, filename, err);
}
EXPORT_API int LoadEXRMultipartImageFromMemory_Export(EXRImage* images,
                                                      const EXRHeader** headers,
                                                      unsigned int num_parts,
                                                      const unsigned char* memory,
                                                      const size_t size, const char** err) {
  return LoadEXRMultipartImageFromMemory(images, headers, num_parts, memory, size, err);
}
EXPORT_API int SaveEXRImageToFile_Export(const EXRImage* image,
                                         const EXRHeader* exr_header, const char* filename, const char** err) {
  return SaveEXRImageToFile(image, exr_header, filename, err);
}
EXPORT_API size_t SaveEXRImageToMemory_Export(const EXRImage* image,
                                              const EXRHeader* exr_header,
                                              unsigned char** memory, const char** err) {
  return SaveEXRImageToMemory(image, exr_header, memory, err);
}
EXPORT_API int SaveEXRMultipartImageToFile_Export(const EXRImage* images,
                                                  const EXRHeader** exr_headers,
                                                  unsigned int num_parts,
                                                  const char* filename, const char** err) {
  return SaveEXRMultipartImageToFile(images, exr_headers, num_parts, filename, err);
}
EXPORT_API size_t SaveEXRMultipartImageToMemory_Export(const EXRImage* images,
                                                       const EXRHeader** exr_headers,
                                                       unsigned int num_parts,
                                                       unsigned char** memory, const char** err) {
  return SaveEXRMultipartImageToMemory(images, exr_headers, num_parts, memory, err);
}
EXPORT_API int LoadDeepEXR_Export(DeepImage* out_image, const char* filename,
                                  const char** err) {
  return LoadDeepEXR(out_image, filename, err);
}
EXPORT_API int LoadEXRFromMemory_Export(float** out_rgba, int* width, int* height,
                                        const unsigned char* memory, size_t size,
                                        const char** err) {
  return LoadEXRFromMemory(out_rgba, width, height, memory, size, err);
}

// ****************************
// * useful utility functions *
// ****************************
EXPORT_API void FreeImageData(float* rgba) {
  std::free(rgba);
}

#ifdef __cplusplus
}
#endif
