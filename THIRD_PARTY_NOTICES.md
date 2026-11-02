# Third-party notices

AwakeLid source is MIT licensed. It uses Windows system APIs and the user's installed .NET Desktop Runtime. Microsoft PowerToys binaries and source are not redistributed. PowerToys and Microsoft names identify compatibility, not endorsement.

The native DLL is built with Zig 0.14.1's Clang/mingw-w64 toolchain. Native runtime components can be linked into the DLL. The distributed mingw-w64 copyright/license notice is retained in `licenses/mingw-w64-COPYING.txt`; Zig's own license is retained in `licenses/zig-LICENSE.txt`. The compiler itself is not shipped in the runtime ZIP. .NET and Windows runtimes retain their own licenses.

Toolchain archive: https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip

Pinned SHA-256: `554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c`.
