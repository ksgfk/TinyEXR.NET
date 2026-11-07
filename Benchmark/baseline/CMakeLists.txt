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

include("${CMAKE_CURRENT_SOURCE_DIR}/../../Scripts/cmake/OpenEXR.cmake")
file(TO_CMAKE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../.." TINYEXR_BENCHMARK_REPO_ROOT_PATH)

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
    openexr_compression_benchmark
)

if(MSVC)
    target_compile_options(openexr_compression_benchmark PRIVATE /W4 /permissive-)
    set_target_properties(openexr_compression_benchmark PROPERTIES
        VS_DEBUGGER_WORKING_DIRECTORY "${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    )
else()
    target_compile_options(openexr_compression_benchmark PRIVATE -Wall -Wextra -Wpedantic)
endif()
