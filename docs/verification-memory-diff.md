# Diff memory verification (B065)

## Fixture

Peak RSS was measured with `resource.getrusage(RUSAGE_CHILDREN).ru_maxrss` (exact child peak,
no sampling) around `dotnet <dll> diff --json -- OLD NEW`, .NET SDK 10.0, Debug and Release.

Pairs:

- **SB2 pair** (ticket pair substitute; the v206→v207 pair named in B065 does not exist on disk):
  `/home/xertrov/tm-docs/Maps/SB2/Sweet 2 burger v205.Map.gbx` → `Sweet 2 burger v206.Map.Gbx`
  (3.3 MB → 7.1 MB; the regression fixtures from `docs/verification-real-maps.md`).
- **Recompression pair**: `~/Downloads/Sweet 2 Burger v180 (Ultra2).Map.gbx` →
  `..._recompressed.Map.gbx` (7.8 MB → 2.7 MB; same map, outer body recompressed).
- **Stress pair** (synthetic, ~20× the ticket maps): v206 plus a 120 MB incompressible embedded
  blob each — `stress-v206-big1.Map.Gbx` vs `stress-v206-big2.Map.Gbx` (133 MB files, ~190 MB
  decompressed bodies, one added + one removed embedded entry, so both contribution sides run
  removal trials near the 256 MB body budget).

## Results

| Pair | Before (master) | After (this change) | Δ |
| --- | ---: | ---: | ---: |
| Stress pair (133 MB maps) | 4,533,008 KB (4.32 GB) | 947,540 KB (0.93 GB) | −79% |
| SB2 v205 → v206 | 869,488 KB (849 MB) | 302,828 KB (296 MB) | −65% |
| v180 → v180 recompressed | 656,608 KB (641 MB) | 282,316 KB (276 MB) | −57% |

Release-configuration stress run: 946,928 KB (0.90 GB) — the 1 GB target holds for the shipped
artifact as well. All three pairs produce **byte-identical JSON diff output** before and after
(`cmp` clean, Debug and Release). The real-map regression tests
(`RealMapDiffRegressionTests` with `GBX_SIZE_TREE_SB2` set) pass with their exact counts.

## What dominated and what changed

Phase tracing (`GC.GetTotalMemory` + private bytes at phase boundaries) plus a full heap dump
(`dotnet-dump`) showed:

1. The GBX.NET parse of both maps coexisted with file byte arrays and snapshot ZIP data because
   Debug-lifetime locals kept them reachable through later phases. Diff phases are now
   frame-scoped (`ParseSide`/`CaptureContent`/`ParseAndCompare`): file bytes, parsed maps, and
   snapshots die with their frame.
2. Embedded contribution measurement decompressed the file twice (plan + identity capture) and
   copied the body and embedded ZIP out of the decompressed buffer. The plan now slices the
   decompressed file buffer and captures identities from its own encapsulation pass.
3. Each removal trial allocated a full trial body plus a full LZO output buffer. Trials now build
   into one per-measurement buffer and compress with SharpLzo's span API (Lzo1x_999 — the exact
   mode `GBX.NET.LZO.Lzo` delegates to, so compressed lengths are unchanged).
4. Contribution measurement holds both maps' file bytes. Each side is re-read from disk and
   released before the other side is measured.
5. Smaller items: decompression stream pre-sized to the declared body length, JSON report streamed
   to stdout instead of materialized, full GC with LOH compaction at phase boundaries, and
   `System.GC.ConserveMemory=7` for the CLI.

The stress-pair peak now sits in the right-map parse phase (GBX.NET object graph), not in the
measurement pipeline; the remaining floor scales with parsed map content rather than with the
number of measured embedded entries.
