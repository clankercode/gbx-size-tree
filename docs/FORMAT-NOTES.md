# Verified Gbx / .Map.Gbx format facts

Verified 2026-09-01 against GBX.NET 2.4.4 source, wiki.xaseco.org GBX spec, gbx-py structs,
and direct byte-level parses of the sample map. Implementation agents: cite this file, do not re-research.

## Container layout (little-endian)

| Offset | Size | Field |
|---|---|---|
| 0 | 3 | magic `"GBX"` |
| 3 | 2 | version (u16) — 6 for TM2020 maps |
| 5 | 1 | format `'B'` binary |
| 6 | 1 | ref-table compression `'U'`/`'C'` (always 'U' in practice) |
| 7 | 1 | body compression `'U'`/`'C'` |
| 8 | 1 | unknown byte `'R'`/`'E'` |
| 9 | 4 | classId (u32) — 0x03043000 = CGameCtnChallenge |
| 13 | 4 | userDataSize (u32) |
| 17 | uds | user data (header chunks) |
| … | 4 | numNodes (u32) — AFTER user data, NOT counted in userDataSize |
| … | 4 | numExternalNodes (u32) — 0 for maps ⇒ ref table is just these 4 bytes |
| … | var | body |

User data: `i32 numChunks`, then per chunk `u32 id + i32 size` (bit31 0x80000000 = "heavy",
mask it off for the real size), then concatenated payloads in the same order.

Compressed body: `u32 uncompressedSize + u32 compressedSize + LZO1x data` to EOF.
Uncompressed body: raw bytes to EOF.

## Body chunk framing (decompressed body)

- Skippable: `u32 chunkId` + `u32 0x534B4950` ("SKIP") + `i32 size` + `size` bytes. 12 B overhead, exact size.
- Non-skippable: `u32 chunkId` + data (size only via parsing). TM2020 non-skippables: 0x03043011
  (params), **0x0304301F (blocks — usually the largest non-skippable)**, 0x0304302A, 0x03043049 (MediaTracker).
- Node terminator: `0xFACADE01`.
- Encapsulated chunks (0x040, 0x043, 0x044, 0x054): body starts `i32 0` + `i32 innerSize` + payload
  with a FRESH lookback-string table.
- SCANNER WARNING: 'SKIP' bytes can occur inside webp/zip/jpeg payloads. Chain by declared size
  (anchored), validate candidate ids' class prefix, byte-scan only inside unknown gaps.

## CGameCtnChallenge chunk map (TM2020)

Header: 0x002 legacy desc (~57 B) · 0x003 common (ident, uid, LightmapCacheUid, LightmapVersion)
· 0x004 version · 0x005 XML string · **0x007 thumbnail** (layout: i32 version, i32 thumbSize,
literal `<Thumbnail.jpg>`, JPEG bytes, `</Thumbnail.jpg>`, `<Comments>`, string, `</Comments>`
— markers are bare ASCII, not length-prefixed) · 0x008 author.

Body (S = skippable):
| Id | S | Meaning |
|---|---|---|
| 0x03043011 | no | collector list + challenge params node refs |
| 0x0304301F | no | **blocks**: MapInfo/name/decoration/size, then per block: lookback name, u8 dir, byte3 coord, i32 flags + conditional skin/waypoint/decal/phys extras |
| 0x0304302A | no | bool |
| 0x03043040 | S | **items (AnchoredObjects)**, encapsulated, v7: per item ident, YPR, coord, pos, pivot, scale + snapping i32 arrays |
| 0x03043042 | S | author info |
| 0x03043043 | S | zone genealogies (encapsulated) |
| 0x03043044 | S | **script metadata** (encapsulated CScriptTraitsMetadata) |
| 0x03043048 | S | **baked blocks** (same per-block shape as 0x01F) + BakedClipsAdditionalData |
| 0x03043049 | no | **MediaTracker** clips (intro/podium/in-game/end-race/ambiance) + trigger size |
| 0x0304304B | S | objectives text (4 strings) |
| 0x03043050 | S | offzones |
| 0x03043051 | S | title id + build version |
| 0x03043052 | S | deco base height |
| 0x03043053 | S | bot paths |
| 0x03043054 | S | **embedded items**: i32 version, encapsulated { ident[] filesMeta, i32-prefixed ZIP, string[] Textures } |
| 0x03043055 | S | (TMUnlimiter) |
| 0x03043056/0x0304306B | S | day time / dynamic daylight |
| 0x03043059 | S | world distortion |
| 0x0304305B | S | **lightmap** — see below |
| 0x0304305F | S | free blocks: vec3 pos + vec3 rot per free block (24 B each) |
| 0x03043062 | S | difficulty colors: 1 B per block + baked + item |
| 0x03043063 | S | anim phase offsets: 1 B per element |
| 0x03043065 | S | foreground pack desc |
| 0x03043067 | S | launched checkpoints |
| 0x03043068 | S | **per-element lightmap qualities**: 1 B per block + baked + item |
| 0x03043069 | S | macroblock indexes: i32 per block + item, then id/flags pairs |
| 0x0304306C | S | color palette |

### Small body chunks — Ghidra-verified (2026-09-01)

All 18 small body chunks on a current-map dump, identified from Max's Ghidra pass over
`Trackmania.exe`'s `CGameCtnChallenge_SerializeChunk` (private notes:
`~/src/openplanet/research-priv/2026-09-01-MapGbxBodyChunks-Focused.md` — wire layouts,
struct offsets, helper addresses). Sizes are from that dump (matches the sample map).
Every one is now in `ChunkCatalog`, so `--unknown-chunks` reports 0 here; the debug view
remains for ids outside this set. Skippable framing overhead is 12 B (`id + 'SKIP' + len`).

| Id | S | Sample B | Meaning (Ghidra) |
|---|---|---|---|
| 0x0304300D | no | 20 | player-model (vehicle) ident triple; body's first TaggedId also emits the archive codec version u32 |
| 0x03043018 | S | 20 | two u32s; GBX.NET says "laps" but TM2020 semantics unproven (laps live in 0x036's group) |
| 0x03043019 | S | 53 | ModPackDesc (texture mod), V3 resource ref |
| 0x03043022 | no | 8 | map flags u32 (DecoBaseHeightOffset = word − 4) |
| 0x03043024 | no | 45 | CustomMusicPackDesc, V3 resource ref |
| 0x03043025 | no | 20 | MapCoordOrigin + MapCoordTarget (2 × vec2) |
| 0x03043029 | S | 32 | password raw16 + CRC32 over hex+MapUid string; reader tolerates a mismatch (resets to {1,0}) |
| 0x03043034 | S | 16 | length-prefixed byte buffer, empty (GBX.NET: decals) |
| 0x03043036 | S | 60 | **medal times + comments**: TMObjective author/gold/silver/bronze, NbLaps, IsLapRace, 5 more u32s, Comments string — NOT the "realtime thumbnail camera" GBX.NET names |
| 0x0304303E | S | 24 | CarMarksBuffer v10 nod-ref list, empty |
| 0x0304304F | S | 17 | v3 + one byte; unidentified |
| 0x03043057 | S | 20 | v5 record list (index + Vec3 polyline + Vec4s + ident) — path-like; empty |
| 0x0304305A | S | 20 | nested-challenge grid (sub-maps); empty read paints the XZ grid 0xFF |
| 0x0304305D | S | 5,357 | **sparse 3D byte octree** over the block grid (terrain/occupancy-style; only nonzero cells become leaves). Wire: root extent, dims XYZ, record count, then per record parent index + (leaf u8 \| 8 child indices). The only sizeable one |
| 0x0304305E | S | 32 | legacy macroblock list stub — writer always emits empty, reader discards |
| 0x03043060 | S | 20 | v0 + one u32; unidentified |
| 0x03043061 | S | 32 | write-side snapshot (u32 + u32[] + bytes + u32) — reader clears it, does not survive load |
| 0x03043064 | S | 28 | always-empty CGameCtnMediaClipGroup list stub |

## Lightmap chunk 0x0304305B (TM2020) — layout VERIFIED on-disk 2026-09-01

Confirmed against the sample's bytes (payload at body offset 11,616,752, length 4,696,337:
`00000000 01000000 00000000 00000000 0A000000 03000000 BC330C00 'RIFF'...`), matching gbx-py's
struct and the E++ Ghidra research; the earlier "bool HasLightmaps first / version 8 / per-frame
scalars" description was WRONG.

```
i32 chunkVersion                  // 0 observed
bool(i32) u01                     // effectively HasLightmaps; false ⇒ chunk ends here
bool(i32) u02, u03                // 0, 0 observed
-- if !u01: stop --
SHmsLightMapCacheSmall:
  i32 cacheVersion                // 10 observed (E++: writer emits 10)
  i32 frameCount                  // when cacheVersion >= 5; 3 observed (dynamic daylight); else 1
  frameCount × frame:
      3 × (i32 len + bytes)       // three counted buffers per frame, WebP ('RIFF..WEBP') in
                                  // non-zero slots, len MAY be 0 — NO leading scalars in v10
  i32 zlibUncompressedSize        // only when any buffer non-empty
  i32 zlibLen + zlib bytes        // deflate stream: CHmsLightMapCache metadata node (0x0602200B)
```
Sample: zlib cache 1,570,755 B (1,838,848 B uncompressed); webp frames additional.
Strip pattern (gbx-io-proven): `map.HasLightmaps = false; map.LightmapFrames = [new() { Version = 6 }]`.
Ultra2 bake preset ⇒ stronger texture compression ⇒ smaller files.

Ghidra-verified detail (Max's E++ research, `~/src/openplanet/my-plugins/tm-editor-plus-plus/research/2026-09-01-MapGbxLightMapEncoding.md`):
- What the map embeds is the **LightMapCacheSmall** archive (`SHmsLightMapCacheSmall_Archive`): u32 version;
  (v≥5) u32 sprite/frame count (max 32, production often 3); per frame **three counted byte buffers**
  — WebP in the non-zero slots, and **slots may be zero-length**; then uncompressed_size + zlib payload.
- The inflated zlib payload starts with a `CHmsLightMapCache` chunk **0x0602200B** + SKIP — metadata only
  (quality, samples, mapping vectors, frames); NO atlas pixels inside the zlib. Its chunk 0x0602201A
  mapping `colorData[0]` is the per-entry shadow-brightness byte channel.
- The full-resolution bake atlases (`LightMap%u_HSH%c.webp`, `LightMap%u_LocalBig_Avg.webp`,
  `ProbeGrid.webp`) live in a separate cache PACK outside the map file (game cache dir) — they are NOT
  part of .Map.Gbx size and out of scope for this tool.
- Saved RGB is always half-res of the bake (`YCbCr_to_RGB_Down2x2`); frame webps are reconstructed-RGB /
  HSH planes. DD2 lightened shadows inside the Map.Gbx by clamping mapping `colorData[0]` to minimum 100;
  this changes shadow brightness without modifying WebP pixels, mapping sizes, or the other color channels.

## GBX.NET 2.4.4 API essentials

- Startup: `Gbx.LZO = new Lzo(); Gbx.ZLib = new ZLib();`
- Parse: `Gbx.Parse<CGameCtnChallenge>(path)` — NEVER set `GbxReadSettings.ReadRawBody`
  (incompatible with node parse; also disables recompression on save).
- `Gbx.ParseHeader(path)` — no LZO needed (header-only mode).
- Exact sizes for free: `gbx.Body.UncompressedSize/.CompressedSize/.CompressionRatio`,
  `map.Thumbnail` (JPEG byte[]), `map.EmbeddedZipData` (byte[]) + `map.OpenReadEmbeddedZipData()`,
  `map.LightmapCacheData` (ZlibData: `.Data.Length` compressed, `.UncompressedSize`).
- LAZY TRAP: `map.LightmapCache`/`LightmapFrames`/`LightmapCacheSmall` trigger a zlib parse of the
  whole cache. Analysis must not touch them; only StripLightmapAction may.
- Save: `gbx.BodyCompression = GbxCompression.Compressed; gbx.Save(path)` → re-serializes and
  compresses with **LZO1x_999** (stronger than Nadeo's fast level — resave alone shrinks maps).
- Streaming utils: `Gbx.Decompress(input, output)` / `Gbx.Compress(...)` — header-copy body
  (de)compression without node parsing; used on embedded item gbx files.
- `map.UpdateEmbeddedZipData(Action<ZipArchive>)` — mutate the embedded zip in place.
- Header chunk sizes are NOT retained by the lib — read the user-data table yourself.
- Per-chunk writer measurement: the lib's `GbxWriter.LoadFrom` is internal, so isolated per-chunk
  writes OVER-COUNT (lookback strings re-written per chunk). Use ONE GbxWriter over ONE MemoryStream,
  dispatch `IReadableWritableChunk.ReadWrite(node, rw)` / `IWritableChunk.Write(node, w)`, skip
  `IHeaderChunk`, record position deltas. Mirrors `CMwNod.Write` — GBX.NET pinned to [2.4.4].

## Sample map ground truth (sample.Map.Gbx)

File 7,832,571 B · user data 65,473 B · header chunks 0x002=57 0x003=241 0x004=4 0x005=6135
0x007=58889 0x008=95 · 688 nodes · body 18,529,821 → 7,767,065 B (41.9%) · 35,374 blocks ·
50,749 items · embedded zip 1,216,568 B · lightmap zlib 1,570,755 B · thumbnail JPEG 58,825 B.
Online-play limit ≈ 7,168 KiB (7,340,032 B) — the sample exceeds it by ~481 KiB.

## Embedded-items optimization (gbx-io-proven)

**Empirical finding (sample map, 2026-09-01):** every embedded `.gbx` in the sample's zip already
has an UNCOMPRESSED body (`GBX v6 'B' 'U' 'U'`) — TM2020 itself embeds items with decompressed
bodies. So the decompress-inner-bodies step is a no-op for game-embedded items (still needed for
items embedded by other tools); the realistic wins here are max-level re-deflate of the zip and
orphan removal. Detection of 'C' bodies must still exist and be tested synthetically.


For each `*.gbx`/`*.Gbx` zip entry: `Gbx.Decompress` its body ('C'→'U'), then re-zip everything
Deflate level 9 (LZO'd bodies are high-entropy and defeat deflate; raw bodies deflate well).
Handle the "got bigger" case by keeping the original. The game accepts uncompressed-body gbx
in embeds. Stored (uncompressed) zip entries would let the outer LZO compress across entries —
UNVERIFIED in-game, keep behind `--embed-stored`.
