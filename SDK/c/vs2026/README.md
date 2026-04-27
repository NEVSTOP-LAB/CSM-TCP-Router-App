# Visual Studio 2026 build files

This directory contains a Visual Studio 2026 (VS 18) solution and projects
for building the C SDK and its test suite directly from the IDE on
Windows. The CMake build at `SDK/c/CMakeLists.txt` remains the
authoritative cross-platform build; these project files are provided as a
convenience for Windows developers who prefer the VS IDE.

## Layout

```
vs2026/
├── csm_tcp_router_client.sln                   – solution file
├── csm_tcp_router_client/
│   └── csm_tcp_router_client.vcxproj           – static library project
└── csm_tcp_router_client.tests/
    └── csm_tcp_router_client.tests.vcxproj     – test executable
```

## Toolset

| Setting           | Value                                   |
|-------------------|-----------------------------------------|
| Solution format   | Visual Studio 18 (2026)                 |
| Platform toolset  | `v144` (Visual Studio 2026 C/C++)       |
| C language        | `/std:c11` (`stdc11` MSBuild metadata)  |
| Configurations    | Debug, Release                          |
| Platforms         | Win32 (x86), x64                        |
| Runtime           | `/MT[d]` (statically linked CRT)        |
| Subsystem (tests) | Console                                 |
| Linked libraries  | `Ws2_32.lib`                            |

## Building

Open `csm_tcp_router_client.sln` in Visual Studio 2026 and build the
solution (Ctrl+Shift+B), or from a developer command prompt:

```cmd
msbuild csm_tcp_router_client.sln ^
        /p:Configuration=Release /p:Platform=x64
```

## Running tests

After building, run the test executable directly:

```cmd
build\x64\Release\csm_tcp_router_client.tests\csm_tcp_router_client.tests.exe
```

Exit code `0` indicates all tests passed; any other exit code indicates
one or more failures (failed test names are printed to stderr).

## Why VS2026 and not an older version?

The user explicitly requested VS2026 (the latest Visual Studio at the
time the SDK was added). The project files use the new `v144` platform
toolset shipped with VS 2026; older VS versions will refuse to load
them. If you need to build with an older VS, prefer the `CMakeLists.txt`
in the SDK root – it works with every supported MSVC version (and on
Linux / macOS).
