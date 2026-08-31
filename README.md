# gbx-size-tree

<p align="center">
  <img
    src="docs/assets/readme-hero.png"
    alt="gbx-size-tree showing an exact on-disk Trackmania map breakdown and a verified 7.5 percent lossless size reduction"
    width="800"
  />
</p>

Colorful size breakdown + optimizer for Trackmania 2020 `.Map.Gbx` files. See exactly what's
eating your map's bytes — lightmap, embedded items, block/item placements, thumbnail — then
shrink it: lossless recompression, embedded-zip optimization, dead-chunk pruning, and
flag-gated lossy trims, with ranked recommendations (lossless first — editor advice is
never suggested when a lossless pass suffices).

> ⚠️ Always keep your original map. The tool never overwrites its input
> (output defaults to `<name>_recompressed.Map.Gbx`).

## Features

- **At-a-glance on-disk overview** — mutually exclusive category contributions reconcile to
  the exact file size, followed by the map's delta to Trackmania's 7 MiB online limit.
- **Lightmap diagnostics** — WebP images, compressed mapping cache, framing overhead, frame
  sizes, and VP8/VP8L/VP8X resolutions. Mixed and partially unreadable resolutions are called
  out explicitly.
- **Detailed size tree and drilldowns** — header/body chunks, embedded ZIP entries with usage
  and vertex counts, element counts, confidence markers, residual bytes, and optional complete
  or unknown-only chunk views. Recovered counts for legacy items are prefixed with `~`.
- **Measured lossless optimization** — trial resave, orphaned-embed removal, embedded-ZIP
  recompression, and exact post-save validation. `--attribute` measures each action's marginal
  contribution.
- **Explicit visual edits** — thumbnail controls, baked-lightmap removal, and DD2-style shadow
  lightening. Destructive or quality-changing actions are opt-in and kept out of the default
  lossless recommendation verdict.
- **Automation-friendly output** — stable camelCase JSON, dry runs, deterministic action
  selection, explicit color control, and useful parse/validation exit codes.

## Usage

```
gbx-size-tree <file.Map.Gbx>              # interactive TUI: inspect, pick actions, save
gbx-size-tree <file.Map.Gbx> -n           # report only (--non-interactive)
gbx-size-tree <file.Map.Gbx> -i           # explicitly request the interactive TUI
gbx-size-tree <file.Map.Gbx> --optimize   # apply all lossless actions, write output
gbx-size-tree <file.Map.Gbx> --json       # machine-readable report
gbx-size-tree <file.Map.Gbx> -O --attribute  # optimize + per-action savings breakdown
gbx-size-tree <file.Map.Gbx> --all-chunks # include every known and unknown chunk
gbx-size-tree <file.Map.Gbx> --unknown-chunks  # unknown-chunk debugging view
```

Double-clicking the exe (Windows/Wine) opens a file picker (defaults to your
`Documents/Trackmania/Maps`) and an interactive session, and pauses before closing.
When stdin or stdout is redirected, the CLI stays non-interactive automatically.

### Optimization and visual-edit examples

```sh
# Preview the default lossless pipeline without writing anything.
gbx-size-tree MyMap.Map.Gbx --optimize --dry-run

# Recompress a thumbnail, writing to the safe default *_recompressed.Map.Gbx path.
gbx-size-tree MyMap.Map.Gbx --thumbnail recompress:80

# Reproduce Deep Dip 2's baked-shadow adjustment: floor only the map's
# shadow-brightness channel at byte value 100; WebP images remain unchanged.
gbx-size-tree MyMap.Map.Gbx --lighten-shadows 100 -o MyMap_lighter.Map.Gbx

# Remove baked shadows entirely. This is deliberately never recommended by default.
gbx-size-tree MyMap.Map.Gbx --strip-lightmap -o MyMap_unshadowed.Map.Gbx
```

`--lighten-shadows MIN` accepts a raw byte floor from `0` to `255`; `0` is off and `100`
matches the DD2 treatment. It changes only lightmap mapping channel 0, preserving the other
mapping channels and embedded WebPs. It cannot be combined with `--strip-lightmap`.

Use `--action LIST` and `--no-action LIST` for explicit pipeline composition, `--force` to
replace an existing *output* file, and `--json` for the versioned machine-readable report.
The input map itself is never overwritten.

## Why sizes are "uncompressed"

A `.Map.Gbx` body is a single LZO stream — individual chunks don't have a real "compressed
size". The report shows exact uncompressed sizes, the real whole-body compressed size, and an
honest per-chunk on-disk *estimate* (already-compressed payloads like WebP/JPEG/zip count
~1:1; the rest is scaled to match the true total).

The report leads with that on-disk category view, a conditional lightmap composition table,
and online-limit headroom/overage. The uncompressed tree and detailed tables follow, so the
most actionable answers appear before the lower-level forensic detail.

## Building

.NET 10 SDK, then `just build` / `just test` / `just publish` (self-contained single-file for
linux-x64 + win-x64, trimmed, ~15 MB each; `just package` builds the zips locally). Releases
are cut with `just release X.Y.Z` — see `RELEASE.md`. **Do not enable NativeAOT on
CachyOS-style v4-CRT hosts** — see the note in `Directory.Build.props`.

## License

GPL-3.0-or-later — required by the [GBX.NET.LZO](https://www.nuget.org/packages/GBX.NET.LZO)
dependency (LZO body compression). See `THIRD-PARTY-NOTICES.md`.

Created by [Max Kaye (XertroV)](https://xk.io) + AI.
