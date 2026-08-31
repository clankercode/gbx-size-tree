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

## P3 measured lossless optimization (2026-09-01)

- **LZO1x_999 resave only**: 7,832,571 B → **7,412,564 B**, saving 420,007 B
  (**5.4%**). It remains 72,532 B over the online-play limit.
- **Default T1 pipeline** (`orphan-embeds`, `embed-zip`, `resave`): the embedded ZIP was
  already optimal; removing 12 unreferenced entries plus the LZO1x_999 resave produced
  **7,245,560 B**, saving 587,011 B (**7.5%**) and finishing 94,472 B under the limit.
- The orphan-removal increment over the resave-only baseline was 167,004 B. Both outputs
  reparsed and passed the identity/count validation gate before they were written.

## Post-v0.1.0 improvement batch (2026-09-01)

- **Trimmed publish is now the default** (16.1 MB linux / 14.4 MB win vs ~41 MB untrimmed,
  zero trim warnings). Gates run before flipping: trimmed report output byte-identical to
  untrimmed, `--optimize` output SHA256-identical, wine render + pty interactive clean.
  Escape hatch: `-p:PublishTrimmed=false`.
- **Resave Detect estimate**: a background `ResaveTrial` (parse + LZO1x_999 save on a worker
  thread, started the moment the file is read) gives a MEASURED number by recommendation
  time — it overlaps the analyzer, costing <1s wall on the sample (4.7s total at 136% CPU).
  Fallback when the trial is unavailable/slow (30s cap): 5% of the compressed body,
  `Heuristic` (measured 5.4% on the sample). Never 0/`MeasuredOnSave` — that ranked the
  guaranteed win last and the under-limit verdict ignored it.
- **Strip-lightmap is a caution, not a recommendation** (Max's call): applicable-but-
  destructive actions (`RecommendByDefault == false`) render as ⚠ warnings, excluded from
  ranking and the verdict, still available via `--strip-lightmap`/menu.
- **`--attribute`** replays cumulative prefixes to attribute savings per action; the baseline
  prefix must apply `resave` explicitly because an empty session set short-circuits to the
  original bytes. Sample attribution: recompression 420,007 B + orphan-embeds 167,004 B.
- **JSON is camelCase throughout** (pre-release contract change; enum values stay PascalCase).
  Golden regen: `just golden-update`.
- `just package` (local zips) and `just release X.Y.Z` (bump+tag, RELEASE.md flow) both
  refuse a dirty worktree — a stale HEAD hash was once stamped into shipped binaries when
  zips were built before the commit.
- **ChunkCatalog covers all 18 small body chunks** after Max's 2026-09-01 Ghidra pass over
  `CGameCtnChallenge_SerializeChunk` (table in FORMAT-NOTES.md; full layouts in his private
  notes). Ghidra overrides GBX.NET naming where they conflict: 0x018 is not laps in TM2020,
  0x036 is medal times + comments (not a thumbnail camera), and 0x05D — the former 5,357 B
  reverse-engineering candidate — is a sparse 3D byte octree over the block grid.
  `--unknown-chunks` reports 0 on the sample; the debug view stays for out-of-set ids.

## Test stack gotchas (xunit v3 + .NET 10)

- xunit.v3 4.0 under the new `dotnet test` (MTP mode): opt-in lives in **global.json** `"test": {"runner": "Microsoft.Testing.Platform"}` (NOT dotnet.config); the test csproj needs `UseMicrosoftTestingPlatformRunner=true` (else xunit's own console runner answers and MTP flags fail).
- **`dotnet test -m:2` silently runs ZERO tests in MTP mode** — justfile builds with `-m:2` first, then runs `dotnet test` bare. Test-run parallelism capped via xunit.runner.json `maxParallelThreads: 2`.
- Use `flock --close` for the build lock. Without `--close`, Roslyn's persistent
  `VBCSCompiler` inherits the lock descriptor and can block later builds/tests indefinitely.
- The `test` recipes depend on `build` and therefore invoke `dotnet test --no-build`; this
  avoids a redundant project evaluation/build pass without passing the broken `-m:2` flag.
- Exe project publish props (`SelfContained` etc.) must be gated on `'$(_IsPublishing)' == 'true'` or the test project can't reference the exe (NETSDK1151).
- SDK 10 `dotnet new sln` creates `.slnx` (not `.sln`).

## Library pins

- GBX.NET **[2.4.4] exact** — `Measure/CumulativeChunkMeasurer` mirrors `CMwNod.Write` internals; upgrading requires re-verifying that loop.
- GBX.NET.LZO [2.1.6] (GPL-3 ⇒ this project is GPL-3.0-or-later), GBX.NET.ZLib [1.1.2].
