#include "TinyEXR.Native.h"

#include <cstdlib>

int __LetGeneraterHappy(const EXRImage& image,
                        const EXRHeader& header,
                        const EXRVersion& v,
                        const DeepImage& d) {
  return image.height + header.chunk_count + v.multipart + d.height;
}

int LoadEXRInternal(float** out_rgba, int* width, int* height,
                    const char* filename, const char** err) {
  return LoadEXR(out_rgba, width, height, filename, err);
}

int LoadEXRWithLayerInternal(float** out_rgba, int* width,
                             int* height, const char* filename,
                             const char* layername,
                             const char** err) {
  return LoadEXRWithLayer(out_rgba, width, height, filename, layername, err);
}

int EXRLayersInternal(const char* filename,
                      const char** layer_names[], int* num_layers,
                      const char** err) { return EXRLayers(filename, layer_names, num_layers, err); }

int IsEXRInternal(const char* filename) { return IsEXR(filename); }

int IsEXRFromMemoryInternal(const unsigned char* memory, size_t size) { return IsEXRFromMemory(memory, size); }

int SaveEXRToMemoryInternal(const float* data, const int width,
                            const int height, const int components,
                            const int save_as_fp16,
                            const unsigned char** outbuf,
                            const char** err) {
  return SaveEXRToMemory(data, width, height, components, save_as_fp16, outbuf, err);
}

int SaveEXRInternal(const float* data, const int width, const int height,
                    const int components, const int save_as_fp16,
                    const char* outfilename, const char** err) {
  return SaveEXR(data, width, height, components, save_as_fp16, outfilename, err);
}

int EXRNumLevelsInternal(const EXRImage* exr_image) {
  return EXRNumLevels(exr_image);
}

void InitEXRHeaderInternal(EXRHeader* exr_header) {
  InitEXRHeader(exr_header);
}

void EXRSetNameAttrInternal(EXRHeader* exr_header, const char* name) {
  EXRSetNameAttr(exr_header, name);
}

void InitEXRImageInternal(EXRImage* exr_image) {
  InitEXRImage(exr_image);
}

int FreeEXRHeaderInternal(EXRHeader* exr_header) {
  return FreeEXRHeader(exr_header);
}

int FreeEXRImageInternal(EXRImage* exr_image) {
  return FreeEXRImage(exr_image);
}

void FreeEXRErrorMessageInternal(const char* msg) {
  FreeEXRErrorMessage(msg);
}

int ParseEXRVersionFromFileInternal(EXRVersion* version, const char* filename) {
  return ParseEXRVersionFromFile(version, filename);
}

int ParseEXRVersionFromMemoryInternal(EXRVersion* version, const unsigned char* memory, size_t size) {
  return ParseEXRVersionFromMemory(version, memory, size);
}

int ParseEXRHeaderFromFileInternal(EXRHeader* header, const EXRVersion* version,
                                   const char* filename, const char** err) {
  return ParseEXRHeaderFromFile(header, version, filename, err);
}

int ParseEXRHeaderFromMemoryInternal(EXRHeader* header,
                                     const EXRVersion* version,
                                     const unsigned char* memory, size_t size,
                                     const char** err) {
  return ParseEXRHeaderFromMemory(header, version, memory, size, err);
}

int ParseEXRMultipartHeaderFromFileInternal(EXRHeader*** headers,
                                            int* num_headers,
                                            const EXRVersion* version,
                                            const char* filename,
                                            const char** err) {
  return ParseEXRMultipartHeaderFromFile(headers, num_headers, version, filename, err);
}

int ParseEXRMultipartHeaderFromMemoryInternal(EXRHeader*** headers,
                                              int* num_headers,
                                              const EXRVersion* version,
                                              const unsigned char* memory,
                                              size_t size, const char** err) {
  return ParseEXRMultipartHeaderFromMemory(headers, num_headers, version, memory, size, err);
}

int LoadEXRImageFromFileInternal(EXRImage* image, const EXRHeader* header,
                                 const char* filename, const char** err) {
  return LoadEXRImageFromFile(image, header, filename, err);
}

int LoadEXRImageFromMemoryInternal(EXRImage* image, const EXRHeader* header,
                                   const unsigned char* memory,
                                   const size_t size, const char** err) {
  return LoadEXRImageFromMemory(image, header, memory, size, err);
}

int LoadEXRMultipartImageFromFileInternal(EXRImage* images,
                                          const EXRHeader** headers,
                                          unsigned int num_parts,
                                          const char* filename,
                                          const char** err) {
  return LoadEXRMultipartImageFromFile(images, headers, num_parts, filename, err);
}

int LoadEXRMultipartImageFromMemoryInternal(EXRImage* images,
                                            const EXRHeader** headers,
                                            unsigned int num_parts,
                                            const unsigned char* memory,
                                            const size_t size, const char** err) {
  return LoadEXRMultipartImageFromMemory(images, headers, num_parts, memory, size, err);
}

int SaveEXRImageToFileInternal(const EXRImage* image,
                               const EXRHeader* exr_header, const char* filename, const char** err) {
  return SaveEXRImageToFile(image, exr_header, filename, err);
}

size_t SaveEXRImageToMemoryInternal(const EXRImage* image,
                                    const EXRHeader* exr_header,
                                    unsigned char** memory, const char** err) {
  return SaveEXRImageToMemory(image, exr_header, memory, err);
}

int SaveEXRMultipartImageToFileInternal(const EXRImage* images,
                                        const EXRHeader** exr_headers,
                                        unsigned int num_parts,
                                        const char* filename, const char** err) {
  return SaveEXRMultipartImageToFile(images, exr_headers, num_parts, filename, err);
}

size_t SaveEXRMultipartImageToMemoryInternal(const EXRImage* images,
                                             const EXRHeader** exr_headers,
                                             unsigned int num_parts,
                                             unsigned char** memory, const char** err) {
  return SaveEXRMultipartImageToMemory(images, exr_headers, num_parts, memory, err);
}

int LoadDeepEXRInternal(DeepImage* out_image, const char* filename,
                        const char** err) {
  return LoadDeepEXR(out_image, filename, err);
}

int LoadEXRFromMemoryInternal(float** out_rgba, int* width, int* height,
                              const unsigned char* memory, size_t size,
                              const char** err) {
  return LoadEXRFromMemory(out_rgba, width, height, memory, size, err);
}

void GlobalFree(void* ptr){
  std::free(ptr);
}