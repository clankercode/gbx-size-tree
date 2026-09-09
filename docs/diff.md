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

The default trial budget is an implementation setting and may change; consult the report note rather than relying on a fixed number.

## Output and color

The default format is the human-readable console report. Console color is automatic: it is disabled when stdout is redirected, when `NO_COLOR` is non-empty, or when `TERM=dumb`. `--color` and `--no-color` explicitly override automatic console detection, including those environment checks.

HTML color chips are enabled by default with styled HTML and disabled by `NO_COLOR` or `--no-color`; `--color` restores them. Unstyled HTML never emits inline color styles.

Available output formats are:

- `--html` — a complete HTML document, styled by default;
- `--html --styled` — explicitly include the default stylesheet;
- `--html --not-styled` — omit CSS and inline styles while retaining semantic HTML classes;
- `--markdown` or `--md` — Markdown; and
- `--json` — the machine-readable JSON report.

The format flags are mutually exclusive. `--styled` and `--not-styled` apply only to `diff --html` and cannot be combined. Reports are written to stdout. The command does not publish or upload them automatically.

## Local examples

Compare two maps in the terminal:

```text
gbx-size-tree diff OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx
```

Include baked blocks and serialized fingerprints:

```text
gbx-size-tree diff --all OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx
```

Save plain console output safely to a local file:

```text
gbx-size-tree diff --no-color OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx > diff.txt
```

Save styled or unstyled HTML locally:

```text
gbx-size-tree diff --html OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx > diff.html
gbx-size-tree diff --html --not-styled OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx > diff-plain.html
```

Save Markdown or JSON locally:

```text
gbx-size-tree diff --md OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx > diff.md
gbx-size-tree diff --json OLD_MAP.Map.Gbx NEW_MAP.Map.Gbx > diff.json
```

These examples use shell redirection and may replace an existing destination file. Choose a new local filename when existing reports must be preserved.
