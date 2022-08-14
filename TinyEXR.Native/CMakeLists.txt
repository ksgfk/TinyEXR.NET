cmake_minimum_required(VERSION 3.0.0)
project(TinyEXR.Native VERSION 0.0.1.0)

# close build tinyexr example
option(TINYEXR_BUILD_SAMPLE "Build a sample" OFF)

add_subdirectory("zlib")

add_library(TinyEXR.Native SHARED
    TinyEXR.Native.cpp
    tinyexr_modify.cpp)
target_link_libraries(TinyEXR.Native zlibstatic)