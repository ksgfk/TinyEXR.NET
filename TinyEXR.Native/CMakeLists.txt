cmake_minimum_required(VERSION 3.12)

cmake_policy(SET CMP0091 NEW)

project(TinyEXRNative VERSION 0.0.1.0)

# close build tinyexr example
option(TINYEXR_BUILD_SAMPLE "Build a sample" OFF)
set(ZLIB_BUILD_TESTING OFF CACHE BOOL "" FORCE)
set(ZLIB_BUILD_SHARED OFF CACHE BOOL "" FORCE)
set(ZLIB_BUILD_STATIC ON CACHE BOOL "" FORCE)
set(ZLIB_INSTALL OFF CACHE BOOL "" FORCE)

add_subdirectory("zlib")

add_library(TinyEXRNative SHARED
    TinyEXR.Native.cpp)

add_library(TinyEXRNativeStatic STATIC
    TinyEXR.Native.cpp)

set_target_properties(TinyEXRNativeStatic PROPERTIES
    OUTPUT_NAME "TinyEXRNative-static"
)

function(configure_tinyexr_target target_name)
    # `zconf.h` is generated into the build tree, but it is only needed while
    # building this wrapper. Do not leak the build directory via zlibstatic's
    # public interface, since newer CMake versions reject that export.
    target_include_directories(${target_name} PRIVATE ${CMAKE_CURRENT_BINARY_DIR}/zlib)
    target_link_libraries(${target_name} PRIVATE zlibstatic)

    set_target_properties(${target_name} PROPERTIES
        ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/artifacts"
        LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/artifacts"
        RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/artifacts"
        ARCHIVE_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/artifacts/Debug"
        LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/artifacts/Debug"
        RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/artifacts/Debug"
        ARCHIVE_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/artifacts/Release"
        LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/artifacts/Release"
        RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/artifacts/Release"
        ARCHIVE_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/artifacts/RelWithDebInfo"
        LIBRARY_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/artifacts/RelWithDebInfo"
        RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/artifacts/RelWithDebInfo"
        ARCHIVE_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/artifacts/MinSizeRel"
        LIBRARY_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/artifacts/MinSizeRel"
        RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/artifacts/MinSizeRel"
    )

    if(CMAKE_SYSTEM_NAME MATCHES "Linux")
        target_compile_options(${target_name} PUBLIC -fPIC)
    endif()
endfunction()

set_target_properties(zlibstatic PROPERTIES
    ARCHIVE_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/artifacts"
    LIBRARY_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/artifacts"
    RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/artifacts"
    ARCHIVE_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/artifacts/Debug"
    LIBRARY_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/artifacts/Debug"
    RUNTIME_OUTPUT_DIRECTORY_DEBUG "${CMAKE_BINARY_DIR}/artifacts/Debug"
    ARCHIVE_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/artifacts/Release"
    LIBRARY_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/artifacts/Release"
    RUNTIME_OUTPUT_DIRECTORY_RELEASE "${CMAKE_BINARY_DIR}/artifacts/Release"
    ARCHIVE_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/artifacts/RelWithDebInfo"
    LIBRARY_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/artifacts/RelWithDebInfo"
    RUNTIME_OUTPUT_DIRECTORY_RELWITHDEBINFO "${CMAKE_BINARY_DIR}/artifacts/RelWithDebInfo"
    ARCHIVE_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/artifacts/MinSizeRel"
    LIBRARY_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/artifacts/MinSizeRel"
    RUNTIME_OUTPUT_DIRECTORY_MINSIZEREL "${CMAKE_BINARY_DIR}/artifacts/MinSizeRel"
)

configure_tinyexr_target(TinyEXRNative)
configure_tinyexr_target(TinyEXRNativeStatic)
# static link msvcrt
if(MSVC)
set_target_properties(zlibstatic PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
)
set_target_properties(TinyEXRNative PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
)
set_target_properties(TinyEXRNativeStatic PROPERTIES
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>"
)
endif()

if(CMAKE_SYSTEM_NAME MATCHES "Linux")
target_compile_options(zlibstatic PUBLIC -fPIC)
endif()
