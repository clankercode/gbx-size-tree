# Decisions & measured facts

## P0 toolchain + publish spike (2026-09-01)

- **SDK**: dotnet-sdk 10.0.111 via pacman (needed `dotnet-runtime` 10 as well — SDK tools wouldn't run with only runtime 9). TFM `net10.0`.
- **Hard gate PASSED**: GBX.NET.LZO 2.1.6 (NativeSharpLzo native lib) works under self-contained single-file publish with `IncludeNativeLibrariesForSelfExtract=true` on BOTH linux-x64 (run from outside the repo) and win-x64 under wine 11.16. Identical output both platforms.
- **No AVX-512 ISA stamp** on the linux binary (`readelf -n` shows no `x86 ISA needed`) — Microsoft-built runtime objects, as predicted. NativeAOT remains banned on xsm (see Directory.Build.props).
- **Binary sizes**: untrimmed 78.1 MB (both RIDs); `PublishTrimmed=true` linux-x64 → **16.9 MB, zero trim warnings**, sample smoke passes. Trimming stays off for release until Spectre-heavy UI passes the full smoke (risk: Spectre/ImageSharp reflection under trim).
- **`dotnet run` gotcha**: `-m:2` is forwarded to the app, not MSBuild — justfile builds first (`-m:2`) then runs `--no-build`.

## Sample map ground truth (reference map, Ultra2 bake)

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
- **Recommendations receive the parsed map in all three frontends** (Analyze exposes it via
  an out-param; batch/interactive pass `session.DetectMap`), so `embed-zip`'s Detect always
  runs its measured in-memory rebuild. On the sample it measures "already optimal" and drops
  out of the report entirely — previously report mode hit the null-map fallback and ranked a
  misleading 0 B row.
- **Ranked recommendations are lossless-first** (Max's call): every tool-applicable T1 action
  outranks lossy/editor advice regardless of size, so the under-limit verdict never counts a
  re-bake or thumbnail step the lossless set alone could cover. Rows past the verdict cutoff
  render dim ("further options, not needed for the limit").
- **`prune-chunks` (T1, default-on, Order 5)** removes the three body chunks the game
  provably discards on load (Ghidra: 0x05E parsed-then-freed, 0x061 cleared after read,
  0x064 temp-vector stub) — 92 B uncompressed on the sample, 29 B on-disk marginal. Default
  pipeline is now 7,832,571 → **7,245,531 B** (587,040 B, 7.5%), 94,501 B under the limit;
  output SHA256 7b74bbe1…. after Max's 2026-09-01 Ghidra pass over
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

## Lightmap WebP recompression spike (2026-09-01)

Measured on the sample's 5 non-empty lightmap blobs (of 9 slots; all **lossy VP8**, RGB;
3,125,514 B total — stored as bare length-prefixed buffers in chunk 0x0304305B, NOT zipped;
the zlib cache holds only mapping metadata, no pixels):

| Re-encode (Pillow, method=6) | Total bytes | vs original | PSNR range |
|---|---|---|---|
| WebP lossless | 7,886,488 | **+152%** | exact |
| WebP q95 | 3,074,512 | −1.6% | 32.3–46.9 dB |
| WebP q90 | 2,624,684 | **−16.0%** | 31.5–44.7 dB |
| WebP q75 | 1,892,244 | −39.5% | 29.2–40.2 dB |
| JPEG q90 | 2,922,618 | −6.5% | 30.1–39.7 dB |

- **CAVEAT (discovered after the table): two of the five blobs are concatenations of
  multiple WebP images** (f0s1 = 3×1024², f0s2 = 4×652²), and Pillow only read the first
  image of each — so the table's f0s1/f0s2 rows compare one re-encoded sub-image against a
  whole multi-image buffer. See the corrected per-sub-image math below (+0.8% at q90).
- **No lossless win exists**: the source is lossy VP8; only decode→re-encode is possible
  (generational loss), and lossless re-encoding balloons. Any future `recompress-lightmap`
  action is T2 (BenignLossy) at best.
- Nadeo's encoder is roughly q95-equivalent on the 1024² diffuse slots; the earlier
  "wasteful 652² slot" claim was the multi-image artifact above, now retracted.
- Per Max's Ghidra research (E++ lightmap-encoding note), the map-embedded CacheSmall
  drives the editor/preview lighting; in-game lighting loads from the external cache pack.
- Test artifacts built via GBX.NET frame-data swap, in `~/Downloads`: `…_lmq90.Map.Gbx`
  (6,909,807 B — under the online limit from the WebP re-encode + resave alone) and
  `…_lmjpg.Map.Gbx` (7,208,355 B, all five blobs JFIF JPEG).
- **In-game results (2026-09-01, corrected)**: the **q90 WebP swap CRASHED**; the **JPEG
  swap LOADED but took 726 s** — the game fails every sprite's WebP header probe, silently
  falls into the regenerate path, and re-bakes the whole lightmap at load. (First report
  guessed the JPEG variant had crashed; Max's later jpg load test flipped the attribution.)
  Control experiment exonerates the save path: a no-op frame swap is byte-identical to the
  validated resave-only output (SHA256 7daaeee8…), so the crash is blob-content-driven.
- **Crash root cause — multi-sprite buffers (2026-09-01, measured)**: the lightmap blobs are
  NOT one WebP each. On the sample, frame 0's Data2 holds **3 concatenated 1024² WebPs**
  (HBasis coefficient planes) and Data3 holds **4 concatenated 652² WebPs**; only
  Data/f1/f2 are single images. Pillow reads just the first RIFF, so the q90 swap replaced
  those buffers with single images **shorter than the mapping cache's recorded sprite
  offsets/lengths** → out-of-bounds slice in `Hms_ModelCreateForZone` → crash. JPEG survives
  because the first header probe fails before any slicing. Consequence: ANY blob-length
  change is unsafe unless the zlib mapping-cache descriptor lengths are rewritten too.
- **Corrected recompression math**: re-encoding every sub-image individually at WebP q90 is
  **+0.8% overall** (3,125,514 → 3,149,006 B) — the earlier −16% compared single re-encodes
  against whole multi-image buffers. Only the three 1024² diffuse buffers shrink (−7–8.5%,
  ~194 KB total); the HBasis planes balloon +60–148%. A `recompress-lightmap` action would
  need selective re-encoding PLUS a mapping-cache metadata rewrite for ~194 KB of T2-lossy
  savings — dropped as not worth it (resave+prune already puts the sample 92 KiB under).
- **Load-path facts Ghidra-confirmed (2026-09-01)**: `Hms_ModelCreateForZone` decodes each
  embedded sprite via a WebP-only path (`FileWebP::ReadHeader` + libwebp YCbCr import) whose
  results are IGNORED at two levels. No format sniffing; WebP is mandatory. The zlib mapping
  cache is REQUIRED (not editor-only): the per-sprite descriptor table {format=5, buffer
  index, len} and the mapping vectors come from it. The game stores save-time webp quality in
  cache metadata and its reuse gate wants ≥91%, but never re-inspects blob bytes. Full trace:
  research-priv lightmap-encoding addendum (functions renamed in the shared Ghidra DB).

## Library pins

- GBX.NET **[2.4.4] exact** — `Measure/CumulativeChunkMeasurer` mirrors `CMwNod.Write` internals; upgrading requires re-verifying that loop.
- GBX.NET.LZO [2.1.6] (GPL-3 ⇒ this project is GPL-3.0-or-later), GBX.NET.ZLib [1.1.2].
