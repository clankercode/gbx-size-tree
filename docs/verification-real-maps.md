# Real-map diff verification

## Fixture

This lane validates the exact SB2 pair, with v206 as the regression target:

- OLD: `/home/xertrov/tm-docs/Maps/SB2/Sweet 2 burger v205.Map.gbx`
  - 3,334,387 bytes
  - SHA-256 `b2802168218f1e6bfc26b3834d7934084eacaccfd5e108857bf3a35dabe88870`
- NEW: `/home/xertrov/tm-docs/Maps/SB2/Sweet 2 burger v206.Map.Gbx`
  - 7,099,215 bytes
  - SHA-256 `e784e1b1078ded693da232ae044b7effe2b60077e076ce241b08a3ebfcffec2e`

The verification was run from commit `e9627d4c82caf27b13d1799b00e9f3d34b0a5dad` with .NET SDK 10.0.111. The committed regression is optional when the user-owned maps are absent:

```sh
GBX_SIZE_TREE_SB2=/home/xertrov/tm-docs/Maps/SB2 \
  flock --close /tmp/gbx-size-tree.build.lock \
  dotnet test --no-build -- --filter-class '*RealMapDiffRegressionTests*'
```

To rebuild all data and rendered artifacts under `/tmp/gbx-final-real-*`:

```sh
GBX_SIZE_TREE_SB2=/home/xertrov/tm-docs/Maps/SB2 \
  scripts/verify-real-map-diff.sh
```

The script uses temporary output followed by rename for forward captures. It runs default and `--all` console, JSON, Markdown, styled HTML, and plain HTML, plus default/`--all` reverse and NEW self comparisons.

## Results

All 14 captures exited zero with empty stderr. Forward full-format runs took 87–98 seconds each on this host; NEW self runs took 7–9 seconds. The JSON observations were:

| Check | Default | `--all` |
| --- | ---: | ---: |
| blocks | 4 | 4 |
| baked blocks | 0 | 128 |
| placed items | 127 | 127 |
| embedded entry changes | 35 | 35 |
| deep embedded property entries | 17 | 17 |
| metadata changes | 141 | 141 |
| diagnostic chunk changes | 0 | 23 |
| warnings | 0 | 0 |

Default output therefore keeps baked blocks and diagnostic chunks hidden. `--all` exposes both, including container-prefix, stored-body, and decompressed-body SHA-256 records; body candidates explicitly say that identification is not exhaustive.

The 17 modified embedded entries produce 89 rendered deep-property rows. Their bounded diagnostics contain 34 `partial-coverage` and 38 `unsupported` side issues. This is explicit opaque-content diagnostic coverage, not a claim that opaque properties are semantically equal. Metadata includes 23 changed `script.*` paths, including the added `_EKV_` data. There was no report-level metadata warning for this pair because capture remained available; the deep-property coverage warnings are present in every output format.

All 52 applicable left/right embedded contribution sides were measured. No side was unavailable, no `Removal trial budget exhausted.` result occurred, and the default 256-trial budget was not exhausted. Marginals remain context-dependent and non-additive.

Reverse comparison swapped file sizes, blocks, items, embedded changes, metadata, deep property hashes/values/issues, and contribution sides. NEW-to-NEW self comparison was empty in both default and `--all`: no block, baked-block, item, embedded, chunk, metadata, contribution, deep-property, or warning entries.

Representative reusable artifacts:

- `/tmp/gbx-final-real-forward-default.{txt,json,md}`
- `/tmp/gbx-final-real-forward-default-{styled,plain}.html`
- `/tmp/gbx-final-real-forward-all.{txt,json,md}`
- `/tmp/gbx-final-real-forward-all-{styled,plain}.html`
- `/tmp/gbx-final-real-{reverse,self}-{default,all}.json`
- `/tmp/gbx-final-real-captures.log`

## Browser verification

Chromium 151.0.7922.173 and Playwright 1.58.1 rendered default/`--all`, styled/plain HTML at 1440×900 and 390×844. The browser check inspected every table and captured each deep-property and metadata section, not only the initial viewport. `/tmp/gbx-final-real-browser.json` records the DOM observations.

- Default HTML had seven shared tables; `--all` had nine after adding baked blocks and chunks.
- Both sizes rendered 18 added/removed embedded rows, 17 modified embedded rows, 89 deep-property rows, 35 contribution rows, 127 item rows, 4 block rows, and 137 metadata-table rows. `--all` additionally rendered 128 baked-block rows and 23 chunk rows.
- Every page had zero console errors and page errors, no scripts, stable table IDs/classes, and visible deep `partial-coverage`/`unsupported` diagnostics.
- Styled pages had one stylesheet, no inline styles, and no document-level horizontal overflow at desktop or mobile widths; wide tables remained in their scroll containers.
- Plain pages had no stylesheet or inline styles. Native unstyled wide-table overflow is expected at both widths and remains easy to parse or restyle.

Overview screenshots are `/tmp/gbx-final-real-{default,all}-{styled,plain}-{desktop,mobile}.png`. Targeted full-section screenshots add `-deep.png` or `-metadata.png`. Visual inspection found readable styled hierarchy, wrapping paths and mobile-contained tables. Plain HTML retained native semantic table rendering and expected horizontal overflow without presentation markup.

## Safe-error regression

The initial missing-NEW-map check exposed a JSON/process status mismatch. Fix `c816790` was then verified against the same quoted missing path:

```sh
set +e
dotnet src/GbxSizeTree/bin/Debug/net10.0/gbx-size-tree.dll diff --json -- \
  '/home/xertrov/tm-docs/Maps/SB2/Sweet 2 burger v205.Map.gbx' \
  '/tmp/gbx-final-real-missing-"quote.Map.Gbx' \
  > /tmp/gbx-final-real-error.json \
  2> /tmp/gbx-final-real-error.stderr
printf 'exit=%s\n' "$?"
```

The corrected process exits `4` (`IoError`) and the JSON envelope also reports code `4`. The message remains valid escaped JSON and stderr preserves the quoted path. The post-fix artifacts are `/tmp/gbx-final-real-error-fixed.json` and `/tmp/gbx-final-real-error-fixed.stderr`; the original failing capture remains available without the `-fixed` suffix.

## Test gate

The focused regression passed 2/2 tests in 5m59s. The final full suite used both real-map fixture roots and passed 560/560 tests in 6m04s:

```sh
GBX_SIZE_TREE_SAMPLE='/home/xertrov/tm-docs/Maps/SB2/Sweet 2 Burger v180 (Ultra2).Map.gbx' \
GBX_SIZE_TREE_SB2='/home/xertrov/tm-docs/Maps/SB2' \
  flock --close /tmp/gbx-size-tree.build.lock dotnet test --no-build
```

The sample path is the existing 7,832,571-byte canonical fixture identified by the test fixture ground truth. No tests were skipped in this exact environment.

Image-diff formats are not validated or claimed here; they remain assigned to B044.
