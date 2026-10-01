cmake_minimum_required(VERSION 3.25)
project(ZML VERSION 0.1.0 LANGUAGES C CXX)
if(NOT WIN32 OR NOT CMAKE_SIZEOF_VOID_P EQUAL 8)
  message(FATAL_ERROR "Windows x64 is required")
endif()
set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
add_compile_options($<$<COMPILE_LANGUAGE:CXX>:/utf-8> $<$<COMPILE_LANGUAGE:CXX>:/W4> $<$<COMPILE_LANGUAGE:CXX>:/permissive->)
add_compile_definitions(UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
set(CMAKE_RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/package/$<CONFIG>")
if(NOT EXISTS "${CMAKE_CURRENT_SOURCE_DIR}/third_party/minhook/CMakeLists.txt")
  message(FATAL_ERROR "Run tools/bootstrap.ps1 to obtain the pinned upstream MinHook")
endif()
add_subdirectory(third_party/minhook EXCLUDE_FROM_ALL)
add_library(zml_common STATIC src/lua_codec.cpp src/manifest.cpp)
target_include_directories(zml_common PUBLIC include src)
target_link_libraries(zml_common PUBLIC crypt32)
add_executable(ZML src/launcher.cpp)
target_link_libraries(ZML PRIVATE zml_common)
add_library(ZMLRuntime SHARED src/runtime.cpp src/il2cpp.cpp)
target_link_libraries(ZMLRuntime PRIVATE zml_common minhook)
add_library(CustomMenu SHARED mods/custom-menu/mod.cpp)
target_include_directories(CustomMenu PRIVATE include mods/custom-menu)
set_target_properties(CustomMenu PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/package/$<CONFIG>/mods/custom-menu")
add_custom_target(package ALL
  COMMAND ${CMAKE_COMMAND} -E copy_directory "${CMAKE_CURRENT_SOURCE_DIR}/mods/custom-menu" "${CMAKE_BINARY_DIR}/package/$<CONFIG>/mods/custom-menu"
  COMMAND ${CMAKE_COMMAND} -E rm -f "${CMAKE_BINARY_DIR}/package/$<CONFIG>/mods/custom-menu/mod.cpp" "${CMAKE_BINARY_DIR}/package/$<CONFIG>/mods/custom-menu/patch.hpp"
  COMMAND ${CMAKE_COMMAND} -E copy "${CMAKE_CURRENT_SOURCE_DIR}/README.md" "${CMAKE_BINARY_DIR}/package/$<CONFIG>/README.md"
  COMMAND ${CMAKE_COMMAND} -E copy "${CMAKE_CURRENT_SOURCE_DIR}/loader.ini.example" "${CMAKE_BINARY_DIR}/package/$<CONFIG>/loader.ini.example"
  COMMAND ${CMAKE_COMMAND} -E make_directory "${CMAKE_BINARY_DIR}/package/$<CONFIG>/docs"
  COMMAND ${CMAKE_COMMAND} -E copy "${CMAKE_CURRENT_SOURCE_DIR}/docs/VALIDATION.md" "${CMAKE_BINARY_DIR}/package/$<CONFIG>/docs/VALIDATION.md"
  COMMAND ${CMAKE_COMMAND} -E copy "${CMAKE_CURRENT_SOURCE_DIR}/third_party/minhook/LICENSE.txt" "${CMAKE_BINARY_DIR}/package/$<CONFIG>/MinHook-LICENSE.txt"
  DEPENDS ZML ZMLRuntime CustomMenu)
enable_testing()
add_executable(CoreTests tests/core_tests.cpp)
set_target_properties(CoreTests PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/tests/$<CONFIG>")
target_link_libraries(CoreTests PRIVATE zml_common)
target_include_directories(CoreTests PRIVATE mods/custom-menu)
add_test(NAME core COMMAND CoreTests)
add_executable(InjectionFixture tests/injection_fixture.cpp)
set_target_properties(InjectionFixture PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/tests/$<CONFIG>")
add_test(NAME launcher_list COMMAND ZML --list)
set_tests_properties(launcher_list PROPERTIES DEPENDS core)
add_library(RejectedRuntime SHARED tests/rejected_runtime.cpp)
set_target_properties(RejectedRuntime PROPERTIES OUTPUT_NAME ZMLRuntime RUNTIME_OUTPUT_DIRECTORY "${CMAKE_BINARY_DIR}/tests/rejected/$<CONFIG>")
add_test(NAME injection_e2e COMMAND powershell -NoProfile -ExecutionPolicy Bypass -File "${CMAKE_CURRENT_SOURCE_DIR}/tests/injection_e2e.ps1"
  -Launcher "$<TARGET_FILE:ZML>" -Fixture "$<TARGET_FILE:InjectionFixture>" -Rejected "$<TARGET_FILE:RejectedRuntime>")
set_tests_properties(injection_e2e PROPERTIES DEPENDS core TIMEOUT 70)
