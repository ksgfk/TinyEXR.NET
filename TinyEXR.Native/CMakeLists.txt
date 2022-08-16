cmake_minimum_required(VERSION 3.0.0)
project(TinyEXR.Native VERSION 0.0.1.0)

# close build tinyexr example
option(TINYEXR_BUILD_SAMPLE "Build a sample" OFF)

if(MSVC)
add_subdirectory("zlib")
endif()

add_library(TinyEXR.Native SHARED
    TinyEXR.Native.cpp
    tinyexr_modify.cpp)

if(MSVC)
target_link_libraries(TinyEXR.Native zlibstatic)
else()
target_link_libraries(TinyEXR.Native -l:libz.a)
endif()
