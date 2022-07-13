cmake_minimum_required(VERSION 3.0.0)
project(TinyEXR.NET.Native VERSION 0.0.1)

add_subdirectory("tinyexr")


add_library(TinyEXR.NET.Native SHARED
    TinyEXR.NET.Native.cpp)
target_link_libraries(TinyEXR.NET.Native tinyexr)
