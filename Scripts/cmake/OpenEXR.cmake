# Shared correctness/performance reference. Bundled OpenJPH, libdeflate and ZSTD
# sources are pinned by OpenEXR; Imath is pinned separately.
include(FetchContent)
set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
foreach(option BUILD_TOOLS BUILD_EXAMPLES BUILD_PYTHON INSTALL INSTALL_TOOLS INSTALL_DEVELOPER_TOOLS INSTALL_DOCS INSTALL_PKG_CONFIG)
  set(OPENEXR_${option} OFF CACHE BOOL "" FORCE)
endforeach()
foreach(dependency DEFLATE IMATH OPENJPH ZSTD)
  set(OPENEXR_FORCE_INTERNAL_${dependency} ON CACHE BOOL "" FORCE)
endforeach()
set(OPENEXR_IMATH_TAG "5f27ba266d3ea1565e912570c30b5eafc89959f1" CACHE STRING "" FORCE)
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
  set(OJPH_DISABLE_AVX512 ON CACHE BOOL "" FORCE)
endif()
FetchContent_Declare(openexr
  GIT_REPOSITORY https://github.com/AcademySoftwareFoundation/openexr.git
  GIT_TAG 3950290530c4fd4776667d0dc4d65a9d003eb936)
FetchContent_MakeAvailable(openexr)
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang" AND CMAKE_SYSTEM_PROCESSOR MATCHES "AMD64|amd64|x86_64")
  target_compile_options(OpenEXRCore PRIVATE -msse4.1)
  target_compile_options(OpenEXR PRIVATE -msse4.1)
endif()

# A cached/source-directory override must not silently change the oracle.
find_package(Git REQUIRED)
foreach(reference openexr imath)
  FetchContent_GetProperties(${reference})
  execute_process(COMMAND "${GIT_EXECUTABLE}" rev-parse HEAD
    WORKING_DIRECTORY "${${reference}_SOURCE_DIR}"
    OUTPUT_VARIABLE reference_commit OUTPUT_STRIP_TRAILING_WHITESPACE
    RESULT_VARIABLE reference_status)
  if(reference STREQUAL "openexr")
    set(expected_commit "3950290530c4fd4776667d0dc4d65a9d003eb936")
  else()
    set(expected_commit "5f27ba266d3ea1565e912570c30b5eafc89959f1")
  endif()
  if(NOT reference_status EQUAL 0 OR NOT reference_commit STREQUAL expected_commit)
    message(FATAL_ERROR "${reference} must be pinned to ${expected_commit}; found ${reference_commit}")
  endif()
endforeach()
