# Local map comparison and Discord sharing

Use `diff` when reviewing a saved map against an earlier version. The command is read-only: it
compares two input files and writes a selected report or purpose-built infographic. It never
publishes or uploads the result.

## Create a report safely

The positional order is always **OLD, then NEW**. OLD is the baseline/left side; NEW is the
candidate/right side. Quote every path, and put `--` before the paths so a filename beginning
with `-` cannot be parsed as an option.

A plain redirect truncates an existing report before the CLI has succeeded. This helper writes
to a temporary file in the destination directory and renames it only after a zero exit status:

```sh
capture() (
    output=$1
    shift
    directory=$(dirname -- "$output") || exit
    mkdir -p -- "$directory" || exit
    temporary=$(mktemp -- "$directory/.gbx-size-tree.XXXXXX") || exit
    trap 'rm -f -- "$temporary"' EXIT

    "$@" >"$temporary" || exit "$?"
    mv -f -- "$temporary" "$output"
)

OLD='/path/to/Sweet 2 burger v205.Map.Gbx'
NEW='/path/to/Sweet 2 burger v206.Map.Gbx'
REPORT='./reports/v205-to-v206.md'

capture "$REPORT" gbx-size-tree diff --markdown -- "$OLD" "$NEW" || {
    status=$?
    printf 'gbx-size-tree failed with status %s; report was not replaced\n' "$status" >&2
    exit "$status"
}
```

The temporary file is on the same filesystem as the destination, so the successful rename is
atomic. On failure, the helper returns the CLI's status, removes the temporary file, and leaves
an existing report untouched. stderr remains visible for diagnostics. This wrapper is needed
because shell `>` redirection opens and truncates the destination before `gbx-size-tree` runs;
shell redirection by itself is not atomic.

## Capture image output

PNG and WebP are mutually exclusive with each other and with HTML, Markdown, and JSON. They
are purpose-built diff infographics, not browser screenshots of HTML. `--all` applies to image
coverage in the same way as the text reports. WebP is lossless.

Prefer the image-specific `-o`/`--output` path for automation because the CLI itself performs
temporary-file output followed by atomic rename:

```sh
PNG='./reports/v205-to-v206.png'

gbx-size-tree diff --png --all --output "$PNG" -- "$OLD" "$NEW" || {
    status=$?
    printf 'gbx-size-tree failed with status %s; image was not replaced\n' "$status" >&2
    exit "$status"
}
```

The destination directory must already exist. The CLI refuses either input map as the output
path, refuses an existing destination unless `--force` is present, and does not create parent
directories. The explicit `--png` or `--webp` flag selects the encoding; a filename extension
never infers or changes it. Successful image file output leaves stdout empty, while errors go
to stderr.

Binary image output can instead use stdout only when stdout is redirected:

```sh
capture './reports/v205-to-v206.webp' \
    gbx-size-tree diff --webp -- "$OLD" "$NEW"
```

Do not omit `-o` at a terminal: the CLI refuses to emit binary bytes to an attached terminal.
Image output also rejects pause, interactive/non-interactive, color, and HTML style flags.

Terminal progress uses stderr only when stderr is an actual terminal. It shows actual stages
and elapsed time; ETA stays unknown until measurable trials provide enough information. The
display is cleared before final output and is disabled for redirected stderr, so the capture
helper receives neither terminal control sequences nor progress lines.

Open and review the Markdown report locally. To share it on Discord, attach the `.md` file
manually to the intended message or thread. `gbx-size-tree` does not publish, upload, contact a
webhook, or operate a Discord bot. Reports can be too large to paste into a message, and the
sender remains responsible for checking the report and the server's attachment limit.

## Capture JSON and make a small summary

The diff JSON root is a direct **PascalCase** object; it is not the camelCase
`{schemaVersion, analysis}` envelope produced by single-map analysis. Capture it only after a
successful command:

```sh
JSON='./reports/v205-to-v206.json'

capture "$JSON" gbx-size-tree diff --json -- "$OLD" "$NEW" || {
    status=$?
    printf 'gbx-size-tree failed with status %s; JSON was not replaced\n' "$status" >&2
    exit "$status"
}

jq '{
  oldBytes: .LeftBytes,
  newBytes: .RightBytes,
  deltaBytes: (.RightBytes - .LeftBytes),
  changedEntries: {
    blocks: (.Blocks | length),
    bakedBlocks: (.BakedBlocks | length),
    items: (.Items | length),
    embedded: (.EmbeddedChanges | length),
    metadata: (.MetadataChanges | length),
    chunks: (.Chunks | length)
  },
  warningCount: (.Warnings | length),
  warnings: .Warnings,
  unavailableMarginals: [
    .EmbeddedContributions[]
    | [.Left, .Right][]
    | select(. != null and .MarginalCompressedBodyBytes == null)
    | {path: .Path, reason: .UnavailableReason}
  ]
}' "$JSON"
```

Treat a nonzero process status as failure before running `jq`. Error output is an error object,
not a diff object, and the process status is the authoritative success check. Also inspect
`.Warnings` even after status 0: warnings can say that semantic fields were unavailable while
a byte-level comparison still completed.

The row counts are review workload indicators, not counts of unique editor operations. A
property edit can appear as one removal plus one addition.

## Do not double-count compatibility fields

Several JSON fields intentionally overlap:

- `Blocks`, `BakedBlocks`, `Items`, and `Embedded` are legacy string-valued change arrays.
  `EmbeddedChanges` is the typed version of `Embedded`; count one or the other, not both.
- `LeftBlockSnapshots`, `RightBlockSnapshots`, `LeftItemSnapshots`,
  `RightItemSnapshots`, and the corresponding embedded/baked arrays are complete side
  inventories, not additional changes.
- `MapUid`, `MapName`, `AuthorLogin`, `AuthorNickname`, and `Password` are legacy metadata
  changes also represented in `MetadataChanges`.
- `EmbeddedContributions` measures the entries already represented by `EmbeddedChanges`; it
  is not another set of changed files.

`EmbeddedContributions[].Left` and `.Right` contain independent ZIP/raw entry sizes and an
outer-LZO removal trial. `MarginalCompressedBodyBytes` is signed baseline-minus-removal
savings. It may be negative, and `null` means unavailable rather than zero. Each entry is
measured against its side's original body, so marginals are context-dependent and
**non-additive**: do not sum them as an allocation of map size or expect them to equal the map
size delta. `UnavailableReason` explains skipped or unsafe measurements.

## Semantic comparison versus byte-level comparison

The default command compares interpreted map data: placements, embedded files, metadata, and
file size. Equal default counts do not prove that the files are byte-identical.

Use `--all` when serialized differences matter:

```sh
ALL_JSON='./reports/v205-to-v206-all.json'

capture "$ALL_JSON" gbx-size-tree diff --json --all -- "$OLD" "$NEW" || {
    status=$?
    printf 'gbx-size-tree failed with status %s; JSON was not replaced\n' "$status" >&2
    exit "$status"
}
```

`--all` adds baked blocks and `Chunks`, including hashes for the complete container prefix,
stored body, and decompressed body, plus identified header/body chunk records. Chunk
identification is diagnostic and is not exhaustive; the complete-content hashes cover bytes
outside identified chunks. A stored-body-only change can be an encoding/compression change
without a decompressed semantic change.

If GBX.NET cannot parse a map, `--all` can fall back to serialized-content comparison. In that
case `.Warnings` states that semantic fields are unavailable; do not describe empty semantic
arrays as semantic equality.
