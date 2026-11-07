# Build the unmodified, pinned C11 V3 library with its hosted defaults.
get_filename_component(TINYEXR_REPO_ROOT "${CMAKE_CURRENT_LIST_DIR}/../.." ABSOLUTE)
file(READ "${TINYEXR_REPO_ROOT}/Scripts/tinyexr-source.json" tinyexr_pin)
string(JSON TINYEXR_SOURCE_COMMIT GET "${tinyexr_pin}" commit)
set(TINYEXR_SOURCE_DIR "${TINYEXR_REPO_ROOT}/.cache/tinyexr" CACHE PATH "Prepared TinyEXR source")
find_package(Git REQUIRED)
execute_process(COMMAND "${GIT_EXECUTABLE}" rev-parse HEAD WORKING_DIRECTORY "${TINYEXR_SOURCE_DIR}"
    RESULT_VARIABLE source_status OUTPUT_VARIABLE source_commit OUTPUT_STRIP_TRAILING_WHITESPACE ERROR_QUIET)
if(NOT source_status EQUAL 0 OR NOT source_commit STREQUAL TINYEXR_SOURCE_COMMIT)
    message(FATAL_ERROR "Prepare TinyEXR ${TINYEXR_SOURCE_COMMIT} with pwsh Scripts/prepare-tinyexr.ps1 first.")
endif()
execute_process(COMMAND "${GIT_EXECUTABLE}" status --porcelain --untracked-files=normal WORKING_DIRECTORY "${TINYEXR_SOURCE_DIR}"
    RESULT_VARIABLE source_status OUTPUT_VARIABLE source_changes OUTPUT_STRIP_TRAILING_WHITESPACE)
if(NOT source_status EQUAL 0 OR NOT source_changes STREQUAL "")
    message(FATAL_ERROR "TinyEXR source has local changes; benchmark requires an unmodified checkout.")
endif()

file(GLOB tinyexr_sources CONFIGURE_DEPENDS "${TINYEXR_SOURCE_DIR}/src/*.c")
set(tinyexr_deflate_sources)
foreach(source adler32 deflate_compress deflate_decompress zlib_compress zlib_decompress utils x86/cpu_features arm/cpu_features)
    list(APPEND tinyexr_deflate_sources "${TINYEXR_SOURCE_DIR}/deps/libdeflate/lib/${source}.c")
endforeach()
add_library(tinyexr_v3 STATIC ${tinyexr_sources} ${tinyexr_deflate_sources}
    "${TINYEXR_SOURCE_DIR}/deps/zstd/tinyexr_zstd.c")
# Consumers use only the C++-safe public API; internal headers contain C11-only
# declarations and must not leak into the C++ benchmark adapters.
target_include_directories(tinyexr_v3 PUBLIC "${TINYEXR_SOURCE_DIR}/include"
    PRIVATE "${TINYEXR_SOURCE_DIR}/src" "${TINYEXR_SOURCE_DIR}/deps/libdeflate" "${TINYEXR_SOURCE_DIR}/deps/zstd")
target_compile_definitions(tinyexr_v3 PRIVATE EXR_USE_LIBDEFLATE EXR_ZLIB_DEFAULT_LIBDEFLATE=1
    EXR_JPH_ENABLE_AVX2_FOUR_QUAD16=1)
# TinyEXR and OpenEXR are linked into separate executables, so the vendored
# backend keeps its upstream symbol names without colliding with OpenEXR.
set_target_properties(tinyexr_v3 PROPERTIES C_STANDARD 11 C_STANDARD_REQUIRED ON C_EXTENSIONS OFF)
if(NOT MSVC)
    target_link_libraries(tinyexr_v3 PRIVATE m)
endif()
