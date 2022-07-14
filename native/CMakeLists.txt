cmake_minimum_required(VERSION 3.0.0)
project(TinyEXR.NET.Native VERSION 0.0.1)

# close build tinyexr example
option(TINYEXR_BUILD_SAMPLE "Build a sample" OFF)

add_subdirectory("tinyexr")

add_library(TinyEXR.NET.Native SHARED
    TinyEXR.NET.Native.cpp)
target_link_libraries(TinyEXR.NET.Native tinyexr)
set_target_properties(TinyEXR.NET.Native
    PROPERTIES
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/lib")
