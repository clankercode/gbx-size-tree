# Comparing map versions

Use `diff` with exactly two map files:

```text
gbx-size-tree diff [OPTIONS] OLD_MAP NEW_MAP
```

`OLD_MAP` is the earlier or baseline map and appears on the left. `NEW_MAP` is the later or candidate map and appears on the right. Added values belong to the new map, removed values belong to the old map, and signed byte deltas are new minus old.

## Default comparison

Without `--all`, the report compares:

- total file size;
- embedded ZIP entries by ordinal path, SHA-256 content fingerprint, ZIP-compressed entry bytes, and raw entry bytes;
- placed anchored items using the currently captured placement fields;
- ordinary blocks using the currently captured placement fields; and
- map metadata exposed by the current GBX.NET-backed snapshot, including map, author, medal, display, lighting, validation, security-presence, script-trait, embedded-identity, and embedded-texture fields when available.

The item and block tables are useful placement comparisons, not a promise of deep semantic support for every serialized property. In particular, do not treat an absent table row as proof that every possible item field is equal.

Baked blocks are deliberately excluded by default. Pass `--all` to include baked-block placements and serialized content fingerprints.

## `--all` and opaque content

`--all` adds byte-level fingerprints for the complete container prefix and decompressed body, the stored compressed body when present, header chunk payloads, and body chunk candidates found by the diagnostic scanner. These entries contain sizes and SHA-256 digests.

Fingerprints answer whether the captured bytes changed. They do not interpret what an opaque payload means, and body chunk identification is not exhaustive. If normal map parsing fails while `--all` is active, the command can still produce a content-only comparison and warns that semantic fields are unavailable. Other warnings identify metadata that could not be interpreted because a chunk was opaque, a value was unsupported, or a bounded capture limit was reached. Treat `unavailable` as unknown, not as equal or absent.

## Embedded sizes and marginal measurements

ZIP and raw values are entry sizes, not the total ZIP archive or outer map size. Archive overhead and compression settings are not measured directly. A changed ZIP size can accompany changed content without proving that only compression settings changed.

For changed embedded entries, the command may also report an outer-map marginal measurement. Each marginal is the signed difference between recompressing the original body and recompressing that same original body after removing one entry. The trials are intentionally bounded. Consequently:

- marginals are context-dependent and non-additive;
- they must not be summed as an allocation of map size;
- negative results are valid; and
- a result can be unavailable because of the trial budget, container/body limits, an uncompressed body, ZIP constraints, or a measurement failure.

The default trial budget is currently 256 removal trials per map. The report repeats the effective limit; entries beyond it are unavailable. The limit remains an implementation setting, so automation should consume the report note rather than assume every changed entry was measured.

## Output formats, color, and progress

The default format is the human-readable console report. Console color is automatic: it is disabled when stdout is redirected, when `NO_COLOR` is non-empty, or when `TERM=dumb`. `--color` and `--no-color` explicitly override automatic console detection, including those environment checks.

HTML color chips are enabled by default with styled HTML and disabled by `NO_COLOR` or `--no-color`; `--color` restores them. Unstyled HTML never emits inline color styles.

Available output formats are:

- `--html` — a complete HTML document, styled by default;
- `--html --styled` — explicitly include the default stylesheet;
- `--html --not-styled` — omit CSS and inline styles while retaining semantic HTML classes;
- `--markdown` or `--md` — Markdown;
- `--json` — the machine-readable JSON report;
- `--png` — a purpose-built PNG diff infographic; and
- `--webp` — the same purpose-built infographic encoded as lossless WebP.

HTML, Markdown, JSON, PNG, and WebP are mutually exclusive output formats. PNG and WebP are rendered directly from the diff data; they are not screenshots of the HTML report. `--all` has the same meaning for every format, including both image formats. `--styled` and `--not-styled` apply only to `diff --html` and cannot be combined.

Text reports are written to stdout. Binary image bytes are written to stdout only when stdout is redirected. If stdout is a terminal, image output requires `-o PATH` or `--output PATH`; this avoids writing binary data to a terminal. Image output also rejects `--pause`, `-i`/`--interactive`, `--color`, `--no-color`, `--styled`, and `--not-styled`. The harmless `--no-pause` and `-n`/`--non-interactive` flags are allowed. `gbx-size-tree diff --help` remains pathless and does not require `OLD_MAP` or `NEW_MAP`.

For an image file destination, the selected `--png` or `--webp` flag determines the encoding; the filename extension never infers or changes it. The CLI writes a temporary file in the destination directory and atomically renames it into place after success. It refuses to write over either input map, refuses an existing destination unless `--force` is present, and does not create a missing parent directory. Successful file output leaves stdout empty. Errors go to stderr.

While a diff runs in a terminal, transient progress is written only to terminal stderr. It reports actual stages and elapsed time. ETA remains unknown until measurable trials provide enough information. The progress display is cleared before the final report or image result and is omitted when stderr is redirected, so pipes and captured stdout/stderr are not polluted.

The command does not publish or upload reports or images automatically.

## Local examples

Compare two real map versions in the terminal. Keep the earlier map first:

```text
gbx-size-tree diff 'Sweet 2 burger v205.Map.Gbx' 'Sweet 2 burger v206.Map.Gbx'
```

Include baked blocks and serialized fingerprints:

```text
gbx-size-tree diff --all 'Sweet 2 burger v205.Map.Gbx' 'Sweet 2 burger v206.Map.Gbx'
```

Save plain console output locally:

```text
gbx-size-tree diff --no-color Before.Map.Gbx After.Map.Gbx > diff.txt
```

Save styled or unstyled HTML locally:

```text
gbx-size-tree diff --html Before.Map.Gbx After.Map.Gbx > diff.html
gbx-size-tree diff --html --not-styled Before.Map.Gbx After.Map.Gbx > diff-plain.html
```

Save Markdown or JSON locally:

```text
gbx-size-tree diff --md Before.Map.Gbx After.Map.Gbx > diff.md
gbx-size-tree diff --json Before.Map.Gbx After.Map.Gbx > diff.json
```

Atomically create a PNG infographic, including `--all` coverage:

```text
gbx-size-tree diff --png --all -o diff.png Before.Map.Gbx After.Map.Gbx
```

Write lossless WebP bytes to redirected stdout:

```text
gbx-size-tree diff --webp Before.Map.Gbx After.Map.Gbx > diff.webp
```

The `-o` image example uses the CLI's temporary-file-and-rename path. The shell-redirection examples do not: the shell opens and truncates the destination before the CLI succeeds. Use the capture helper in `docs/automation.md` when an existing redirected report must survive a failed command. All destinations are local; no example uploads anything.
