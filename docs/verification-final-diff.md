# Final diff verification receipt

Date: 2026-09-10

## Result

**PASS.** B044's integrated diff, image, progress, help, password-chunk, and HTML acceptance checks passed. No production source was edited during this verification. The installed-release B045 lane and B046 brainstorming were not run.

## Source and runtime identity

- Claimed worktree: `/home/xertrov/src/gbx-size-tree-final-verification`
- Receipt base HEAD: `4344c521053e88ec7b6f25ac9295a0b46bc940d4`
- Tested source commit: `e4635cea5991ee24d005fff45d63255dde34b167` (`merge: validate final help against image and progress runtime`)
- `e4635cea` is an ancestor of the receipt base. The only paths changed between it and `4344c52` are `.backlog` bookkeeping files; production and test source are identical.
- Reused immutable runtime: `/tmp/gbx-b044/runtime/gbx-size-tree.dll`
- Runtime informational version: `gbx-size-tree 0.1.0+e4635cea5991ee24d005fff45d63255dde34b167`
- Runtime DLL SHA-256: `2e2297630fbe3fc2545a674fd9e62127823e0d4729e18aff223116481ac62a1c`

The runtime hash above identifies the compiled DLL. It is intentionally distinct from the current Git receipt commit: the claimed worktree had no local build output, and all bounded CLI commands reused the already compiled final-help DLL from the same production source instead of rebuilding or racing another worktree.

## Automated test evidence

The parent gate log `/tmp/gbx-integrated-image-progress-help-test.log` records a successful build with zero warnings/errors followed by the complete integrated test suite:

| Total | Passed | Failed | Skipped | Duration |
|---:|---:|---:|---:|---:|
| 677 | 677 | 0 | 0 | 7m 11s |

The full suite was not repeated. The remaining focused matrix reused the compiled final-help test assembly and ran:

```text
GBX_SIZE_TREE_SAMPLE="$SAMPLE" GBX_SIZE_TREE_SB2="$(dirname "$OLD")" \
GBX_SIZE_TREE_INFOGRAPHIC_PREVIEW=/tmp/gbx-b044/focused/preview \
flock --close /tmp/gbx-size-tree.build.lock dotnet test \
  --project /home/xertrov/src/gbx-size-tree-final-help/tests/GbxSizeTree.Tests/GbxSizeTree.Tests.csproj \
  --no-build -- --filter-class "*CLASS*"
```

| Focused class | Passed | Failed | Skipped |
|---|---:|---:|---:|
| `ArgParserTests` | 119 | 0 | 0 |
| `HelpTextTests` | 6 | 0 | 0 |
| `DiffImageWriterTests` | 12 | 0 | 0 |
| `TerminalProgressTests` | 16 | 0 | 0 |
| `ProgramErrorTests` | 2 | 0 | 0 |
| `PasswordChunkMetadataTests` | 6 | 0 | 0 |
| `DiffInfographicTests` | 19 | 0 | 0 |

Logs are under `/tmp/gbx-b044/focused/`. `DiffImageWriterTests` supplies deterministic race/move/open-failure cleanup coverage that is unsafe or awkward to force through the CLI. The full zero-skip parent gate also includes `DiffProgressIntegrationTests`, including its real-map PTY test; the focused unit run covered progress terminal policy, clipping, ETA, throttling, cleanup, and writer failure without repeating that costly integration comparison.

### Password-chunk evidence

`PasswordChunkMetadataTests` ran six cases with zero failures/skips. Its exact test methods are:

- `Capture_ChunkPresenceIsSeparateFromNullEmptyAndNonemptyPlaintext`
- `Capture_OpaquePasswordChunkHasKnownPresenceButUnknownHash`
- `CompareFiles_ActualSerializedChunkRemoval_DefaultAndAll(false)`
- `CompareFiles_ActualSerializedChunkRemoval_DefaultAndAll(true)`
- `RemovePassword_ClearsProtectionAndRemovesChunk`
- `OpaqueFileFallback_DoesNotInventPasswordChunkAbsence`

The serialized-removal theory creates actual chunk `03043029`, removes and re-saves it, verifies the raw scanner no longer finds it, and checks default and `--all`, reverse direction, self diff, and the `--all` removed-chunk row. It verifies `security.passwordChunkPresent` in JSON, console, Markdown, and HTML while `report.Password` remains null, so no password value is disclosed.

## Real PNG, WebP, and PTY progress

Existing bounded real-map evidence in `/tmp/gbx-image-progress-verification.md` was reused; no expensive real-map image was regenerated. It used:

- OLD: `/home/xertrov/tm-docs/Maps/SB2/Sweet 2 burger v205.Map.gbx` (3,334,387 bytes)
- NEW: `/home/xertrov/tm-docs/Maps/SB2/Sweet 2 burger v206.Map.Gbx` (7,099,215 bytes)
- Commands: `dotnet "$DLL" diff --png --all -o real.png -- "$OLD" "$NEW"` and the equivalent `--webp` command, with stdout/stderr captured separately.

Both commands exited 0 with empty stdout and redirected stderr. Pillow reconfirmed:

| Artifact | Decode | Size | File SHA-256 |
|---|---|---:|---|
| `/tmp/gbx-b044/real.png` | PNG RGBA | 1400×1854 | `e2497a603c51a072c2ee9894cb23198a124d32cebbfe0d21d4a6bc896edfedc3` |
| `/tmp/gbx-b044/real.webp` | lossless WebP RGB | 1400×1854 | `d60dfcfb4d49a8affd35d5228a2e476f38d947381ee603999d9d02a25f9bd667` |

After conversion to RGBA, both decoded byte streams have SHA-256 `2be6efd307470f2c562d56a5369568b29c36376f50c0a70e6176ac501b486d62`: exact pixel parity is true.

The real 80-column PTY run exited 0, started progress 0.073 seconds after launch, produced 430 nonblank `Processing:` frames, never exceeded 79 display cells, showed real read/parse/compare/deep/OLD-trial/NEW-trial transitions, and progressed from honest `ETA unknown` to measured ETA. Its one final all-space clear preceded atomic output publication by 1.390 seconds, and no progress row, exception, failure, or temporary file remained. Raw artifacts are `/tmp/gbx-b044/pty.stderr.raw`, `/tmp/gbx-b044/pty.events.json`, and `/tmp/gbx-b044/pty.analysis.txt`.

Visual inspection of both real encodings and the newly generated `/tmp/gbx-b044/focused/preview/diff-infographic-synthetic.png` (PNG RGBA, 1400×1512, SHA-256 `94d121ac0e4ad47384b6fd57ca6e42ec5538ac8230ce8a553476b40a9fe24952`) passed. The hierarchy, old/new labels, file delta, added/removed/modified counts, spatial plot and legend, highlights, metadata, deep-property/chunk panels, omission counts, coverage warnings, and footer are readable; panel text and plot labels remain within their bounds with no visible clipping.

## Bounded CLI command matrix

Commands below used `/tmp/gbx-b044/runtime/gbx-size-tree.dll`. Captured files and a concise result list are under `/tmp/gbx-b044/cheap/`.

| Check | Result |
|---|---|
| Existing image output without `--force` | Exit 4; stdout empty; original contents remained `keep`; error only on stderr. |
| Output aliases NEW input, with `--force` | Exit 4 before comparison; stdout empty; NEW remained SHA-256 `e784e1b1078ded693da232ae044b7effe2b60077e076ce241b08a3ebfcffec2e`. |
| Missing output parent | Exit 4; stdout empty; `/tmp/gbx-b044/cheap/missing` was not created. |
| Conflicting `--png --json --help` | Exit 1; stdout empty; one usage error on stderr. |
| Missing path containing `"` with `--json` | Exit 4; JSON parsed; `.error.code` was 4; quote was escaped in JSON and literal on stderr; no progress/ETA text. |
| Pathless `diff --help` and `diff --png --help` | Exit 0; stderr empty; help contains `--png`, `--webp`, atomic rename, parent/input/existing-output safety, terminal progress, coverage limits, and no-upload language. |
| Redirected output | Real PNG/WebP file runs had empty redirected stderr, proving no transient progress/control output; quoted missing-path JSON also remained parseable. |
| `TERM=dumb` | Missing-map run exited 4 with empty stdout and exactly one durable error; stderr contained no `Processing:`, ETA, CR, or ESC bytes. |
| PTY failure cleanup | Missing-map PTY run exited 4 after two real read-stage frames; an all-space clear occurred before the sole durable error, JSON remained parseable with envelope code 4, and the application payload had no escape bytes. |
| Temporary cleanup | No same-directory `.*.tmp` file remained after any error check. Focused writer tests also passed force-replace, move-failure, read-failure, and input/symlink safety cases. |

Representative invocations were:

```text
dotnet "$DLL" diff --png -o existing.png -- "$OLD" "$NEW"
dotnet "$DLL" diff --png -o "$NEW" --force -- "$OLD" "$NEW"
dotnet "$DLL" diff --png -o missing/result.png -- "$OLD" "$NEW"
dotnet "$DLL" diff --png --json --help
dotnet "$DLL" diff --json -- "$OLD" 'missing-"quote.Map.Gbx'
dotnet "$DLL" diff --help
dotnet "$DLL" diff --png --help
TERM=dumb dotnet "$DLL" diff --no-color -- "$OLD" missing.Map.Gbx
script -qefc "stty cols 62; TERM=xterm-256color dotnet '$DLL' diff --json -- '$OLD' missing.Map.Gbx >pty-error.json" pty-error.typescript
```

## Browser verification

No HTML renderer rerender was required: `DiffRenderer.cs` and `TmText.cs` have not changed since the reusable current-renderer evidence. Existing real-map captures were reopened instead of regenerating 14 costly comparisons.

Two cheap browser harnesses were rerun against current artifacts:

```text
node /tmp/gbx-chip-current/verify.mjs
node /tmp/gbx-parent-html-check.mjs
```

- `/tmp/gbx-chip-current/browser-results.json`: 10 mode/viewport records, zero browser errors, and six successful table interactions. Styled color produced 25 selectable rounded chips per viewport with exact palette matching and contrast from 5.14:1 to 21:1. Styled `--no-color`, unstyled, and default `NO_COLOR` produced no chips; explicit `--color` overrode `NO_COLOR`. Unstyled output had no style tags or inline styles. Placed-item, block, and baked-block tables scrolled and chip text selected at 1440×900 and 390×844. Hostile text stayed inert.
- `/tmp/gbx-b044/html-browser-current.json` (regenerated from existing real HTML): eight default/all × styled/plain × desktop/mobile records and zero browser errors. Styled pages had one stylesheet, contained horizontal overflow, and all scrollable tables were exercised. Plain pages had no stylesheet or inline style. Default had seven tables; `--all` had nine, including baked blocks and chunks.
- `/tmp/gbx-final-real-browser.json` and `/tmp/gbx-parent-html-check.json` remain the original real-map browser evidence; `/tmp/gbx-chip-verification.md` records the renderer-source identity and earlier detailed review.

## Generated and retained artifacts

- `/tmp/gbx-b044/real.png`, `/tmp/gbx-b044/real.webp`: real-map infographic encodings.
- `/tmp/gbx-b044/focused/preview/diff-infographic-synthetic.png`: focused-test visual preview.
- `/tmp/gbx-b044/focused/*.log`: focused class logs and summary.
- `/tmp/gbx-b044/cheap/`: CLI stdout, stderr, JSON, help, PTY failure transcript, and result captures.
- `/tmp/gbx-b044/chip-browser-current.log`, `/tmp/gbx-b044/html-browser-current.json`: rerun browser receipts.
- `/tmp/gbx-b044/pty.*`, `/tmp/gbx-b044/parity.txt`, `/tmp/gbx-b044/post-pty-parity.txt`: real PTY and exact-pixel supporting data.

All required B044 verification lanes pass. B045 installation remains intentionally unstarted, and B046 remains out of scope.
