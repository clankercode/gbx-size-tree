# Everyday map-diff and collaboration ideas

This brainstorm was written after the implementation, final verification, and installation were complete. These are proposals, not implemented commands or permission to publish anything.

## Goal and current starting point

A mapper should be able to save a new version, understand what changed, and hand it to another builder without manually reconstructing the history. The v205 → v206 comparison demonstrates why a concise image should accompany—not replace—the detailed report: placement changes, embedded content, metadata, and opaque serialized changes answer different questions.

The CLI now provides console, JSON, Markdown, styled/plain HTML, and dedicated PNG/lossless WebP output. It has transient terminal progress and safe image-file output. `docs/automation.md` describes currently supported local capture and manual sharing. Automatic watching, uploading, claims, locks, and merging are not implemented.

## Recommended next steps

| Proposal | User benefit | First useful scope | Important constraint |
|---|---|---|---|
| One local report bundle | Avoid repeating comparison work to obtain several formats | Compare once and write image, HTML, Markdown, JSON, and a manifest into one new directory | Publish the directory only after all requested files succeed; never leave an apparently complete partial bundle |
| Remember an explicit baseline | Stop selecting two long map paths every time | A local project configuration naming baseline, output directory, and preferred formats | Record and verify the baseline SHA-256; advance it only after a successful, explicit acceptance |
| Watch a map-save directory | Receive a fresh local diff after saving | Debounce file events and wait for a stable, readable save before comparison | Recheck the file hash after capture; collapse duplicate events and avoid comparing a half-written map |
| Prepare a sharing draft | Reduce the steps required for Discord or GitHub review | Create a local image and short Markdown summary with an explicit preview | Keep upload a separate confirmation step; show destination and exactly which files/text will leave the machine |
| Add an embedded-usage cleanup view | Make size optimization actionable | Show before/after reference counts and distinguish known zero use from unknown coverage | Existing backlog B061 owns this idea; aliases and unresolved references must not become false zero counts |
| Summarize omitted placement properties | Explain detail that does not fit in a table | Counts of changes such as lightmap quality or flying state after the detailed rows | Existing backlog B060 owns this idea; avoid counting the same placement twice in headline totals |

The best first improvement is the **local report bundle**. It removes repeated expensive embedded measurements, produces a consistent set of artifacts from one comparison, and requires neither credentials nor a remote service. Baseline configuration is a useful second step; directory watching should come after reliable baseline and save-stability rules.

## Sharing to Discord and GitHub

A practical draft should contain:

- Old/new map filenames and content hashes, author-supplied description, and explicit comparison direction.
- The compact infographic, plus a link or attachment for the detailed HTML/Markdown report.
- Coverage warnings and unavailable measurements, without presenting them as unchanged values.
- A choice to include or omit author identifiers, local paths, and script metadata. Reports can contain information beyond the map's visible name.

For Discord, start with a local draft and manual attachment. An optional webhook integration could later preview the channel, redact sensitive fields, and require confirmation. Store credentials outside project files and logs. Retry uploads using a persistent request identity so a timeout does not post duplicate updates.

For GitHub, a workflow could attach the bundle to a pull request or an Actions run. It should identify the exact old/new hashes and tool version, separate report generation from publication permissions, and require approval for untrusted contributions. Large map binaries need an explicit storage policy rather than silently committing generated copies. Neither GitHub automation nor a webhook should run solely because a map-save event occurred.

## Claims and collaborative map builds

### Start with advisory claims

A claim could identify the builder, baseline hash, intended area or task, creation time, and expiry. Whole-map claims are simpler than spatial claims for the first version. A spatial claim is useful context but cannot prove independence: shared embedded assets, global metadata, and scripts can affect multiple areas.

An advisory claim should:

1. Be acquired with an atomic compare-and-set operation in one shared service, not competing local lock files.
2. Have a renewable lease and visible expiry so an offline builder cannot block everyone indefinitely.
3. Make explicit takeover possible after expiry, recording who took over and why.
4. Clearly say that it coordinates people; it does not prevent edits inside the game editor.

### Verify a handoff against the claimed baseline

When a builder submits work, compare its claimed baseline hash with the team's current accepted version. If they match, generate the review bundle for acceptance. If they differ, report that the work is based on an older version and require reconciliation; do not silently replace the team's latest map.

A handoff record can link the claim, base hash, submitted hash, accepted hash, report manifest, and reviewer decision. Advancing the accepted version should be an atomic operation. Keep the previous version recoverable.

### Treat automatic merging as a separate research task

A visual diff does not establish a safe merge. Duplicate placements, shared embedded paths, script traits, global metadata, baked content, and opaque chunks can conflict even when visible edits look separate. Start with three-way review summaries that distinguish common-base changes from concurrent edits. Only attempt automatic merging once the supported edit types have explicit conflict rules and round-trip validation; retain manual reconciliation for unsupported data.

## Failure and usability checks for any follow-up

- A cancelled or failed comparison leaves the accepted baseline and previous report intact.
- A save changed during processing is retried or rejected, never labeled as one consistent snapshot.
- Estimated completion times remain estimates; unsupported stages display an unknown ETA.
- Offline sharing queues retain a preview and destination, and never publish after the user has cancelled.
- Expired claims and stale baselines are visible before submission.
- Re-running a job with identical hashes does not create duplicate report bundles or remote posts unless requested.
- Generated summaries preserve the distinction between entity counts, property observations, and byte-level evidence.

No new implementation tickets were created from this brainstorm. B060 and B061 remain the user's existing backlog additions, separate from the completed batch.
