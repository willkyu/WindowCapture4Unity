cmake_minimum_required(VERSION 3.20)
project(WGC LANGUAGES CXX)

if (NOT CMAKE_BUILD_TYPE)
    set(CMAKE_BUILD_TYPE Release CACHE STRING "Build type" FORCE)
endif()

add_library(WGC SHARED
    WGC.cpp
    WGC.PixelFormat.h
)

target_compile_features(WGC PRIVATE cxx_std_17)

target_compile_definitions(WGC PRIVATE
    UNICODE
    _UNICODE
    NOMINMAX
    WIN32_LEAN_AND_MEAN
    _WIN32_WINNT=0x0A00
)

if (MSVC)
    target_compile_options(WGC PRIVATE /EHsc)
endif()

target_link_libraries(WGC PRIVATE
    d3d11
    windowsapp
)

add_executable(WGCTest
    WGC.test.cpp
    WGC.PixelFormat.h
)

target_compile_features(WGCTest PRIVATE cxx_std_17)

target_compile_definitions(WGCTest PRIVATE
    UNICODE
    _UNICODE
    NOMINMAX
    WIN32_LEAN_AND_MEAN
    _WIN32_WINNT=0x0A00
)

if (MSVC)
    target_compile_options(WGCTest PRIVATE /EHsc)
endif()

target_link_libraries(WGCTest PRIVATE
    WGC
    user32
)
