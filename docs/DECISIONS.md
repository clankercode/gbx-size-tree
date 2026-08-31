# Decisions & measured facts

## P0 toolchain + publish spike (2026-09-01)

- **SDK**: dotnet-sdk 10.0.111 via pacman (needed `dotnet-runtime` 10 as well — SDK tools wouldn't run with only runtime 9). TFM `net10.0`.
- **Hard gate PASSED**: GBX.NET.LZO 2.1.6 (NativeSharpLzo native lib) works under self-contained single-file publish with `IncludeNativeLibrariesForSelfExtract=true` on BOTH linux-x64 (run from outside the repo) and win-x64 under wine 11.16. Identical output both platforms.
- **No AVX-512 ISA stamp** on the linux binary (`readelf -n` shows no `x86 ISA needed`) — Microsoft-built runtime objects, as predicted. NativeAOT remains banned on xsm (see Directory.Build.props).
- **Binary sizes**: untrimmed 78.1 MB (both RIDs); `PublishTrimmed=true` linux-x64 → **16.9 MB, zero trim warnings**, sample smoke passes. Trimming stays off for release until Spectre-heavy UI passes the full smoke (risk: Spectre/ImageSharp reflection under trim).
- **`dotnet run` gotcha**: `-m:2` is forwarded to the app, not MSBuild — justfile builds first (`-m:2`) then runs `--no-build`.

## Sample map ground truth (sample.Map.Gbx)

- File 7,832,571 B; header/user data 65,473 B; body 18,529,821 B → 7,767,065 B LZO (41.9%).
- Header chunks: 0x002=57, 0x003=241, 0x004=4, 0x005(XML)=6,135, 0x007(thumbnail)=58,889, 0x008=95.
- Parsed: 35,374 blocks; 50,749 anchored objects; embedded zip 1,216,568 B; lightmap zlib cache 1,570,755 B (1,838,848 B uncompressed; webp frames NOT included in this number); thumbnail JPEG 58,825 B.
- Online-play limit ≈ 7,168 KiB → this map is ~481 KiB over.

## Test stack gotchas (xunit v3 + .NET 10)

- xunit.v3 4.0 under the new `dotnet test` (MTP mode): opt-in lives in **global.json** `"test": {"runner": "Microsoft.Testing.Platform"}` (NOT dotnet.config); the test csproj needs `UseMicrosoftTestingPlatformRunner=true` (else xunit's own console runner answers and MTP flags fail).
- **`dotnet test -m:2` silently runs ZERO tests in MTP mode** — justfile builds with `-m:2` first, then runs `dotnet test` bare. Test-run parallelism capped via xunit.runner.json `maxParallelThreads: 2`.
- Exe project publish props (`SelfContained` etc.) must be gated on `'$(_IsPublishing)' == 'true'` or the test project can't reference the exe (NETSDK1151).
- SDK 10 `dotnet new sln` creates `.slnx` (not `.sln`).

## Library pins

- GBX.NET **[2.4.4] exact** — `Measure/CumulativeChunkMeasurer` mirrors `CMwNod.Write` internals; upgrading requires re-verifying that loop.
- GBX.NET.LZO [2.1.6] (GPL-3 ⇒ this project is GPL-3.0-or-later), GBX.NET.ZLib [1.1.2].
