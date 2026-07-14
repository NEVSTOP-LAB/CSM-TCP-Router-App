# CMake build for the csm-tcp-router-client C SDK.
#
# Usage:
#   mkdir build && cd build
#   cmake ..
#   cmake --build . --config Release
#   ctest --output-on-failure -C Release
#
# Options:
#   -DCSM_BUILD_TESTS=ON|OFF      (default ON)
#   -DCSM_BUILD_EXAMPLES=ON|OFF   (default ON)
#   -DCSM_BUILD_SHARED=ON|OFF     (default OFF; ON builds a shared library)
cmake_minimum_required(VERSION 3.15)
project(csm_tcp_router_client
        VERSION 0.1.0
        DESCRIPTION "C client SDK for CSM-TCP-Router"
        LANGUAGES C)

option(CSM_BUILD_TESTS    "Build the test suite"    ON)
option(CSM_BUILD_EXAMPLES "Build the example apps"  ON)
option(CSM_BUILD_SHARED   "Build as a shared library" OFF)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_C_EXTENSIONS OFF)

if(MSVC)
    add_compile_options(/W4 /D_CRT_SECURE_NO_WARNINGS)
else()
    add_compile_options(-Wall -Wextra -Wpedantic)
endif()

# -------------------------------------------------------------------------
# Library
# -------------------------------------------------------------------------
if(CSM_BUILD_SHARED)
    add_library(csm_tcp_router_client SHARED src/csm_tcp_router_client.c)
    target_compile_definitions(csm_tcp_router_client
        PRIVATE CSM_BUILD_LIBRARY CSM_BUILD_SHARED
        PUBLIC  CSM_BUILD_SHARED)
else()
    add_library(csm_tcp_router_client STATIC src/csm_tcp_router_client.c)
endif()

target_include_directories(csm_tcp_router_client
    PUBLIC  ${CMAKE_CURRENT_SOURCE_DIR}/include
    PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/src)

set_target_properties(csm_tcp_router_client PROPERTIES
    VERSION   ${PROJECT_VERSION}
    SOVERSION ${PROJECT_VERSION_MAJOR})

# Platform-specific link libraries.
if(WIN32)
    target_link_libraries(csm_tcp_router_client PUBLIC ws2_32)
else()
    find_package(Threads REQUIRED)
    target_link_libraries(csm_tcp_router_client PUBLIC Threads::Threads)
endif()

# -------------------------------------------------------------------------
# Tests
# -------------------------------------------------------------------------
if(CSM_BUILD_TESTS)
    enable_testing()
    add_executable(csm_tcp_router_client_tests
        tests/mock_server.c
        tests/test_protocol.c
        tests/test_client.c
        tests/test_integration.c
        tests/test_main.c)
    target_include_directories(csm_tcp_router_client_tests
        PRIVATE ${CMAKE_CURRENT_SOURCE_DIR}/tests)
    target_link_libraries(csm_tcp_router_client_tests
        PRIVATE csm_tcp_router_client)
    if(WIN32)
        target_link_libraries(csm_tcp_router_client_tests PRIVATE ws2_32)
    endif()
    add_test(NAME csm_tcp_router_client_tests
             COMMAND csm_tcp_router_client_tests)
endif()

# -------------------------------------------------------------------------
# Examples
# -------------------------------------------------------------------------
if(CSM_BUILD_EXAMPLES)
    add_executable(basic_usage      examples/basic_usage.c)
    add_executable(subscribe_status examples/subscribe_status.c)
    add_executable(client_console   examples/client_console.c)
    target_link_libraries(basic_usage      PRIVATE csm_tcp_router_client)
    target_link_libraries(subscribe_status PRIVATE csm_tcp_router_client)
    target_link_libraries(client_console   PRIVATE csm_tcp_router_client)
endif()

# -------------------------------------------------------------------------
# Install rules (header + library)
# -------------------------------------------------------------------------
install(TARGETS csm_tcp_router_client
        RUNTIME DESTINATION bin
        LIBRARY DESTINATION lib
        ARCHIVE DESTINATION lib)
install(FILES include/csm_tcp_router_client.h DESTINATION include)
