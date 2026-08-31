# Changelog

## [0.1.0] - 2026-09-01

Initial release.

### Added
- Colorful exact-where-possible size breakdown of TM2020 `.Map.Gbx` files: header
  (thumbnail, XML), body (blocks, items, lightmap WebP frames vs zlib cache, embedded
  items per zip entry, MediaTracker, per-element arrays), with honest LZO framing and
  estimated on-disk attribution reconciled to the real file size.
- Optimization actions behind three frontends (report / batch flags / interactive
  session): LZO1x_999 resave, embedded-zip rebuild, orphan-embed removal (lossless,
  default-on); lightmap strip and thumbnail strip/lossless/recompress/downscale
  (opt-in). Output validation gate before anything is written; never overwrites input.
- Ranked recommendations against the 7,168 KiB online limit, including editor-only
  advice (re-bake static daylight / lower quality, heavy embedded items). Destructive
  actions (strip-lightmap) surface as warnings, never as suggestions. Resave savings
  are measured by a background trial resave started at file-read.
- `--attribute` per-action marginal savings; `--all-chunks` full chunk table;
  `--unknown-chunks` debug view for ids missing from the chunk catalog; `--json`
  machine-readable envelope (camelCase).
- Windows/Wine double-click UX: file picker (Documents/Trackmania/Maps), interactive
  session, pause-before-close; trimmed self-contained single-file binaries for
  linux-x64 and win-x64 (~16/14 MB).

### Distribution
- Measured on the reference map: default lossless pipeline 7,832,571 → 7,245,560 B
  (7.5% saved), taking it from ~481 KiB over the online limit to 94 KiB under.
