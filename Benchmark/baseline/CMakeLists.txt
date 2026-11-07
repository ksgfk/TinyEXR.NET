cmake_minimum_required(VERSION 3.21)

project(TinyEXRBaselineBenchmark LANGUAGES C CXX)

# Strict C11 is scoped to tinyexr_v3 in Scripts/cmake/TinyEXR.cmake.
# Do not impose it on OpenEXR: its Linux endian helpers need the default
# GNU C environment, which exposes le16toh/htole16 and their 32/64-bit variants.
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

# Fail before downloading/building other dependencies when the prepared source
# is absent, dirty, or at the wrong revision.
include("${CMAKE_CURRENT_SOURCE_DIR}/../../Scripts/cmake/TinyEXR.cmake")
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

include("${CMAKE_CURRENT_SOURCE_DIR}/../../Scripts/cmake/OpenEXR.cmake")
file(TO_CMAKE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../.." TINYEXR_BENCHMARK_REPO_ROOT_PATH)

add_library(file_adapters STATIC src/tiny_adapter.cpp src/official_file.cpp)
target_link_libraries(file_adapters PUBLIC tinyexr_v3 OpenEXR::OpenEXR)
target_compile_definitions(file_adapters PUBLIC
    TINYEXR_BENCHMARK_REPO_ROOT="${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    TINYEXR_BENCHMARK_COMPILER="${CMAKE_CXX_COMPILER_ID} ${CMAKE_CXX_COMPILER_VERSION}"
    TINYEXR_BENCHMARK_OPTIMIZATION="${TINYEXR_BENCHMARK_OPTIMIZATION}"
    TINYEXR_SOURCE_COMMIT="${TINYEXR_SOURCE_COMMIT}")

add_executable(openexr_compression_benchmark src/openexr_compression.cpp)
add_executable(tinyexr_compression_benchmark src/openexr_compression.cpp)
target_compile_definitions(tinyexr_compression_benchmark PRIVATE USE_TINYEXR)
foreach(target openexr_compression_benchmark tinyexr_compression_benchmark)
    target_link_libraries(${target} PRIVATE file_adapters benchmark::benchmark)
endforeach()
add_executable(file_fixture_tool src/file_fixture_tool.cpp)
target_link_libraries(file_fixture_tool PRIVATE file_adapters)
add_custom_target(compression_benchmarks DEPENDS
    openexr_compression_benchmark tinyexr_compression_benchmark file_fixture_tool)
