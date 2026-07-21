cmake_minimum_required(VERSION 3.21)

project(TinyEXRBaselineBenchmark LANGUAGES C CXX)

set(CMAKE_C_STANDARD 11)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

set(TINYEXR_BENCHMARK_OPTIMIZATION "CMake Release defaults")
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
    string(APPEND CMAKE_C_FLAGS_RELEASE " /O2 /Ob3 -march=native")
    string(APPEND CMAKE_CXX_FLAGS_RELEASE " /O2 /Ob3 -march=native")
    set(TINYEXR_BENCHMARK_OPTIMIZATION
        "clang-cl /O2 /Ob3 -march=native, vectorizers")

    include(CheckIPOSupported)
    check_ipo_supported(
        RESULT TINYEXR_BENCHMARK_IPO_SUPPORTED
        OUTPUT TINYEXR_BENCHMARK_IPO_ERROR
    )
    if(TINYEXR_BENCHMARK_IPO_SUPPORTED)
        set(CMAKE_INTERPROCEDURAL_OPTIMIZATION_RELEASE ON)
        string(APPEND TINYEXR_BENCHMARK_OPTIMIZATION ", IPO")
    else()
        message(WARNING
            "clang-cl interprocedural optimization is unavailable: "
            "${TINYEXR_BENCHMARK_IPO_ERROR}")
    endif()
endif()

include(FetchContent)

set(BENCHMARK_ENABLE_TESTING OFF CACHE BOOL "" FORCE)
set(BENCHMARK_ENABLE_GTEST_TESTS OFF CACHE BOOL "" FORCE)
set(BENCHMARK_DOWNLOAD_DEPENDENCIES ON CACHE BOOL "" FORCE)

FetchContent_Declare(
    googlebenchmark
    GIT_REPOSITORY https://github.com/google/benchmark.git
    GIT_TAG v1.9.5
)

FetchContent_MakeAvailable(googlebenchmark)

set(BUILD_SHARED_LIBS OFF CACHE BOOL "" FORCE)
set(BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(OPENEXR_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
set(OPENEXR_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(OPENEXR_BUILD_PYTHON OFF CACHE BOOL "" FORCE)
set(OPENEXR_INSTALL OFF CACHE BOOL "" FORCE)
set(OPENEXR_INSTALL_TOOLS OFF CACHE BOOL "" FORCE)
set(OPENEXR_INSTALL_DEVELOPER_TOOLS OFF CACHE BOOL "" FORCE)
set(OPENEXR_INSTALL_DOCS OFF CACHE BOOL "" FORCE)
set(OPENEXR_INSTALL_PKG_CONFIG OFF CACHE BOOL "" FORCE)
set(OPENEXR_FORCE_INTERNAL_DEFLATE ON CACHE BOOL "" FORCE)
set(OPENEXR_FORCE_INTERNAL_IMATH ON CACHE BOOL "" FORCE)
set(OPENEXR_FORCE_INTERNAL_OPENJPH ON CACHE BOOL "" FORCE)
if(MSVC AND CMAKE_CXX_COMPILER_ID STREQUAL "Clang")
    # OpenJPH's Visual Studio AVX-512 item metadata is not honored by clang-cl.
    set(OJPH_DISABLE_AVX512 ON CACHE BOOL "" FORCE)
endif()

FetchContent_Declare(
    openexr
    GIT_REPOSITORY https://github.com/AcademySoftwareFoundation/openexr.git
    GIT_TAG v3.4.13
    GIT_SHALLOW TRUE
)

FetchContent_MakeAvailable(openexr)

file(TO_CMAKE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../.." TINYEXR_BENCHMARK_REPO_ROOT_PATH)
set(TINYEXR_NATIVE_SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/../../TinyEXR.Native/tinyexr")

find_package(Git REQUIRED)
execute_process(
    COMMAND "${GIT_EXECUTABLE}"
        -c "safe.directory=${TINYEXR_NATIVE_SOURCE_DIR}"
        -C "${TINYEXR_NATIVE_SOURCE_DIR}"
        describe --tags --always
    RESULT_VARIABLE TINYEXR_VERSION_RESULT
    OUTPUT_VARIABLE TINYEXR_BENCHMARK_VERSION
    OUTPUT_STRIP_TRAILING_WHITESPACE
)
if(NOT TINYEXR_VERSION_RESULT EQUAL 0)
    set(TINYEXR_BENCHMARK_VERSION "vendored")
endif()

option(
    TINYEXR_BENCHMARK_USE_LIBDEFLATE
    "Use TinyEXR v3's vendored libdeflate backend"
    ON
)

# Upstream CMake exports only the legacy v1 library. Its v3 Makefile builds
# every src/*.c file, the amalgamated ZSTD source, and (by default) vendored
# libdeflate. Keep those groups separate so third-party warning policy does not
# leak into TinyEXR itself, then archive them as one consumable library target.
file(GLOB TINYEXR_V3_SOURCE_FILES CONFIGURE_DEPENDS
    "${TINYEXR_NATIVE_SOURCE_DIR}/src/*.c"
)

add_library(tinyexr_v3_sources OBJECT ${TINYEXR_V3_SOURCE_FILES})
target_include_directories(tinyexr_v3_sources PRIVATE
    ${TINYEXR_NATIVE_SOURCE_DIR}/include
    ${TINYEXR_NATIVE_SOURCE_DIR}/src
    ${TINYEXR_NATIVE_SOURCE_DIR}/deps/zstd
)
if(MSVC AND CMAKE_C_COMPILER_ID STREQUAL "Clang")
    target_compile_definitions(tinyexr_v3_sources PRIVATE
        _CRT_SECURE_NO_WARNINGS
    )
endif()

add_library(tinyexr_v3_zstd OBJECT
    ${TINYEXR_NATIVE_SOURCE_DIR}/deps/zstd/tinyexr_zstd.c
)
target_include_directories(tinyexr_v3_zstd PRIVATE
    ${TINYEXR_NATIVE_SOURCE_DIR}/deps/zstd
)

set(TINYEXR_V3_OBJECTS
    $<TARGET_OBJECTS:tinyexr_v3_sources>
    $<TARGET_OBJECTS:tinyexr_v3_zstd>
)

if(MSVC)
    target_compile_options(tinyexr_v3_zstd PRIVATE /w)
else()
    target_compile_options(tinyexr_v3_zstd PRIVATE -w)
endif()

if(TINYEXR_BENCHMARK_USE_LIBDEFLATE)
    target_compile_definitions(tinyexr_v3_sources PRIVATE
        EXR_USE_LIBDEFLATE
        EXR_ZLIB_DEFAULT_LIBDEFLATE=1
    )
    target_include_directories(tinyexr_v3_sources PRIVATE
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate
    )

    add_library(tinyexr_v3_libdeflate OBJECT
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/adler32.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/deflate_compress.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/deflate_decompress.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/zlib_compress.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/zlib_decompress.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/utils.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/x86/cpu_features.c
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate/lib/arm/cpu_features.c
    )
    target_include_directories(tinyexr_v3_libdeflate PRIVATE
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/libdeflate
    )
    if(MSVC)
        target_compile_options(tinyexr_v3_libdeflate PRIVATE /w)
    else()
        target_compile_options(tinyexr_v3_libdeflate PRIVATE -w)
    endif()
    list(APPEND TINYEXR_V3_OBJECTS
        $<TARGET_OBJECTS:tinyexr_v3_libdeflate>
    )
    set(TINYEXR_BENCHMARK_DEFLATE "vendored libdeflate, level 4")
else()
    set(TINYEXR_BENCHMARK_DEFLATE "in-tree pure C")
endif()

add_library(tinyexr_v3 STATIC ${TINYEXR_V3_OBJECTS})
add_library(TinyEXR::V3 ALIAS tinyexr_v3)
set_target_properties(tinyexr_v3 PROPERTIES OUTPUT_NAME tinyexr3)
target_include_directories(tinyexr_v3
    PUBLIC
        ${TINYEXR_NATIVE_SOURCE_DIR}/include
    PRIVATE
        ${TINYEXR_NATIVE_SOURCE_DIR}/src
        ${TINYEXR_NATIVE_SOURCE_DIR}/deps/zstd
)

if(NOT WIN32)
    target_link_libraries(tinyexr_v3 PUBLIC m)
endif()

add_executable(tinyexr_compression_benchmark src/tinyexr_compression.cpp)

target_compile_definitions(tinyexr_compression_benchmark PRIVATE
    TINYEXR_BENCHMARK_REPO_ROOT="${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    TINYEXR_BENCHMARK_VERSION="${TINYEXR_BENCHMARK_VERSION}"
    TINYEXR_BENCHMARK_COMPILER="${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}"
    TINYEXR_BENCHMARK_OPTIMIZATION="${TINYEXR_BENCHMARK_OPTIMIZATION}"
    TINYEXR_BENCHMARK_DEFLATE="${TINYEXR_BENCHMARK_DEFLATE}"
)

target_link_libraries(tinyexr_compression_benchmark PRIVATE
    benchmark::benchmark
    TinyEXR::V3
)

add_executable(openexr_compression_benchmark
    src/openexr_compression.cpp
)

target_compile_definitions(openexr_compression_benchmark PRIVATE
    TINYEXR_BENCHMARK_REPO_ROOT="${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    TINYEXR_BENCHMARK_COMPILER="${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}"
    TINYEXR_BENCHMARK_OPTIMIZATION="${TINYEXR_BENCHMARK_OPTIMIZATION}"
)

target_link_libraries(openexr_compression_benchmark PRIVATE
    benchmark::benchmark
    OpenEXR::OpenEXR
)

add_custom_target(compression_benchmarks DEPENDS
    tinyexr_compression_benchmark
    openexr_compression_benchmark
)

if(MSVC)
    target_compile_options(tinyexr_compression_benchmark PRIVATE /W4 /permissive-)
    target_compile_options(openexr_compression_benchmark PRIVATE /W4 /permissive-)
    set_target_properties(openexr_compression_benchmark PROPERTIES
        VS_DEBUGGER_WORKING_DIRECTORY "${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    )
    set_target_properties(tinyexr_compression_benchmark PROPERTIES
        VS_DEBUGGER_WORKING_DIRECTORY "${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    )
else()
    target_compile_options(tinyexr_compression_benchmark PRIVATE -Wall -Wextra -Wpedantic)
    target_compile_options(openexr_compression_benchmark PRIVATE -Wall -Wextra -Wpedantic)
endif()
