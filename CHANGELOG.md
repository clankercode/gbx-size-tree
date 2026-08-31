# Changelog

## [0.1.0] - 2026-09-01

Initial release.

### Added
- Colorful exact-where-possible size breakdown of TM2020 `.Map.Gbx` files: header
  (thumbnail, XML), body (blocks, items, lightmap WebP frames vs zlib cache, embedded
  items per zip entry, MediaTracker, per-element arrays), with honest LZO framing and
  estimated on-disk attribution reconciled to the real file size.
- Optimization actions behind three frontends (report / batch flags / interactive
  session): LZO1x_999 resave, embedded-zip rebuild, orphan-embed removal, and dead-chunk
  pruning (chunks the game provably discards on load) — lossless, default-on; lightmap
  strip, shadow lightening, and thumbnail strip/lossless/recompress/downscale (opt-in).
  Output validation gate before anything is written; never overwrites input.
- Ranked recommendations against the 7,168 KiB online limit, including editor-only
  advice (re-bake static daylight / lower quality, heavy embedded items). Lossless
  actions rank first and the verdict prefers them: lossy/editor advice is never counted
  when lossless alone gets under the limit. Destructive actions (strip-lightmap)
  surface as warnings, never as suggestions. Resave and embedded-zip savings are
  measured (background trial resave started at file-read; in-memory ZIP rebuild).
- `--attribute` per-action marginal savings; `--all-chunks` full chunk table;
  `--unknown-chunks` debug view for ids missing from the chunk catalog; `--json`
  machine-readable envelope (camelCase). The catalog names every TM2020 body chunk,
  including the 18 small ones verified via Ghidra against the game's serializer.
- Windows/Wine double-click UX: file picker (Documents/Trackmania/Maps), interactive
  session, pause-before-close; trimmed self-contained single-file binaries for
  linux-x64 and win-x64 (~16/14 MB).

### Distribution
- Measured on the reference map: identity-safe default lossless pipeline 7,832,571 →
  7,410,663 B (5.4% saved), leaving it about 69 KiB over the online limit. The serializer
  preserves aliased embedded-item identities and orphan removal deletes only genuinely
  unused entries.
