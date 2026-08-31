# Frozen contracts (v1)

Everything in `src/GbxSizeTree.Core/Model/`, `Abstractions/`, `Actions/IMapAction.cs`, and
`Actions/ActionRegistry.cs` is FROZEN after the `contracts-v1` commit. Implementation agents:
if a contract seems wrong, STOP and report to the coordinator — do not edit these files.

## Rules for implementation agents

- Own only the files your task lists; never touch anyone else's, never touch Model/ or Abstractions/.
- Core must never reference Spectre.Console (its csproj has no such package — keep it that way).
- No `async`/`Task`/`Thread`/`Parallel`/`lock` — the tool is deliberately single-threaded.
- Do not re-research the Gbx format: `docs/FORMAT-NOTES.md` is the verified reference; cite it in comments.
- Build/test via `just build` / `just test` (flock-serialized, ≤2 threads). Warnings are errors.

## SizeNode ids (frozen dotted paths)

```
file
├ header
│ ├ header.thumbnail            (chunk 0x03043007; Detail: JPEG bytes + dims)
│ ├ header.xml                  (chunk 0x03043005)
│ └ header.other                (remaining header chunks; children header.chunk.0xXXXXXXXX)
└ body
  ├ body.blocks                 (0x0304301F)
  ├ body.items                  (0x03043040)
  ├ body.bakedblocks            (0x03043048)
  ├ body.lightmap               (0x0304305B)
  │ ├ body.lightmap.webp        (children body.lightmap.webp.frame0 … frameN)
  │ └ body.lightmap.cache       (zlib blob; Detail carries uncompressed size)
  ├ body.embedded               (0x03043054)
  │ └ body.embedded.entry:<zip path>   (top N by size; remainder body.embedded.other)
  ├ body.mediatracker           (0x03043049)
  ├ body.scriptmetadata         (0x03043044)
  ├ body.elemarrays             (0x03043062 colors, 0x03043068 lmquality, 0x03043069 macroblock)
  │ └ body.elemarrays.(colors|lmquality|macroblock)
  ├ body.freeblocks             (0x0304305F)
  ├ body.other                  (children body.other.0xXXXXXXXX)
  └ body.residual               (unattributed reconciliation gap — ALWAYS present, even at 0)
```

## Action ids and pipeline order

| Order | Id | Tier | Notes |
|---|---|---|---|
| 10 | `orphan-embeds` | Lossless | before embed-zip (don't recompress deleted entries) |
| 20 | `embed-zip` | Lossless | Deflate-max; Stored variant only with setting `stored=true` |
| 25 | `lighten-shadows` | BenignLossy | opt-in; setting `shadow-brightness-floor=0..255`; DD2 used 100 |
| 30 | `strip-lightmap` | BenignLossy | |
| 40 | `thumbnail` | BenignLossy | setting `mode=keep|strip|lossless|recompress:<q>|downscale:<px>` |
| 90 | `resave` | Lossless | implicit baseline, always last; savings = LZO999 re-save |
| — | `editor-shadows-static` | EditorOnly | Detect-only |
| — | `editor-shadows-quality` | EditorOnly | Detect-only |
| — | `editor-heavy-items` | EditorOnly | Detect-only |

Settings are `IReadOnlyDictionary<string,string>`; keys are per-action, kebab-case values.

## Key invariants (tests enforce)

- `Σ` scanner region lengths + gaps == decompressed body length; regions in-bounds, non-overlapping.
- `|WriterMeasurement.TotalWrittenBytes − Body.UncompressedSize| / total < 0.5%`.
- Scanner and writer-delta agree per skippable chunk within framing bytes (12 B).
- `Σ AttributedChunk.EstimatedOnDiskBytes == Body.CompressedSize ± 1`.
- Round-trip preserves MapUid, name, author, block/item/free-block counts, embed entry names.
- Batch and interactive frontends applying the same action set produce byte-identical output.
- Undo = reload original + replay: `apply → undo → Materialize()` == baseline bytes.

## Exit codes

0 ok · 1 usage · 2 parse/format · 3 output validation failed · 4 I/O · 5 internal.
`--json`: report (MapAnalysis) on stdout; errors as `JsonErrorEnvelope`; ALL status to stderr.
JSON property names are **camelCase** throughout (envelope and model; changed pre-release
2026-09-01); enum *values* stay PascalCase (`ExactOnDisk`). SizeNode `id` values are the frozen
ids above, unaffected by casing policy.

`IMapAction.RecommendByDefault` (default true): false means the action is applicable but the
tool must never *suggest* it — it lands in `RecommendationReport.Cautions` (rendered as a
warning, `cautions` in JSON), is excluded from `ranked` and from the under-limit verdict, and
stays available via flags and the interactive menu. `strip-lightmap` is the canonical case.

`--unknown-chunks` is a standalone debug view (exempt from the tree-first invariant): chunks
missing from `ChunkCatalog`, as plain lines or a `{schemaVersion, unknownChunksReport}` JSON
envelope. `--all-chunks` appends a full flat chunk table (no size cutoff) to the report.
