cmake_minimum_required(VERSION 3.21)

project(TinyEXRBaselineBenchmark LANGUAGES C CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

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

file(TO_CMAKE_PATH "${CMAKE_CURRENT_SOURCE_DIR}/../.." TINYEXR_BENCHMARK_REPO_ROOT_PATH)

add_executable(tinyexr_baseline_benchmark
    src/main.cpp
    ../../TinyEXR.Native/tinyexr/tinyexr.cc
    ../../TinyEXR.Native/tinyexr/deps/miniz/miniz.c
)

target_include_directories(tinyexr_baseline_benchmark PRIVATE
    ../../TinyEXR.Native/tinyexr
    ../../TinyEXR.Native/tinyexr/deps/miniz
)

target_compile_definitions(tinyexr_baseline_benchmark PRIVATE
    TINYEXR_BENCHMARK_REPO_ROOT="${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
)

target_link_libraries(tinyexr_baseline_benchmark PRIVATE benchmark::benchmark)

if(MSVC)
    target_compile_options(tinyexr_baseline_benchmark PRIVATE /W4 /permissive-)
    set_target_properties(tinyexr_baseline_benchmark PROPERTIES
        VS_DEBUGGER_WORKING_DIRECTORY "${TINYEXR_BENCHMARK_REPO_ROOT_PATH}"
    )
else()
    target_compile_options(tinyexr_baseline_benchmark PRIVATE -Wall -Wextra -Wpedantic)
endif()
