cmake_minimum_required(VERSION 3.5.0)

cmake_policy(SET CMP0091 NEW)

project(TinyEXRNative VERSION 0.0.1.0)

# close build tinyexr example
option(TINYEXR_BUILD_SAMPLE "Build a sample" OFF)

add_subdirectory("zlib")

add_library(TinyEXRNative SHARED
    TinyEXR.Native.cpp)

# `zconf.h` is generated into the build tree, but it is only needed while
# building this wrapper. Do not leak the build directory via zlibstatic's
# public interface, since newer CMake versions reject that export.
target_include_directories(TinyEXRNative PRIVATE ${CMAKE_CURRENT_BINARY_DIR}/zlib)
target_link_libraries(TinyEXRNative zlibstatic)
# static link msvcrt
if(MSVC)
set_target_properties(zlibstatic PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
)
set_target_properties(TinyEXRNative PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
)
endif()
# add -fPIC for linux
if(CMAKE_SYSTEM_NAME MATCHES "Linux")
target_compile_options(TinyEXRNative PUBLIC -fPIC)
target_compile_options(zlibstatic PUBLIC -fPIC)
endif()
