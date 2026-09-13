# RELEASE.md — gbx-size-tree release procedure

**Agents and humans: keep this file accurate.** If you change
[`.github/workflows/release.yml`](.github/workflows/release.yml), the version
source, artifact layout, or any step below, update this document in the same
commit. Stale release docs are a bug.

## What a release produces

Pushing a `vX.Y.Z` tag triggers `release.yml`, which:

1. verifies the tag matches `Directory.Build.props` `<Version>` and that
   `CHANGELOG.md` has a `## [X.Y.Z]` section (fails otherwise);
2. re-runs the full test gate (`dotnet build` + `dotnet test`);
3. publishes trimmed self-contained single-file binaries for **linux-x64** and
   **win-x64** and zips each with `LICENSE`, `THIRD-PARTY-NOTICES.md`, `README.md`:

   | Asset | Contents |
   |-------|----------|
   | `gbx-size-tree-X.Y.Z-linux-x64.zip` | `gbx-size-tree` (~16 MB) + docs |
   | `gbx-size-tree-X.Y.Z-win-x64.zip`   | `gbx-size-tree.exe` (~14 MB) + docs |

4. creates a GitHub Release named `gbx-size-tree X.Y.Z` with **auto-generated**
   notes and the zips attached.

Every push to `master` and every PR runs the test gate via `ci.yml`.

**No NativeAOT** — plain trimmed single-file only (see `Directory.Build.props`;
AVX-512 CRT stamp hazard on some build hosts).

## Version source (single)

`Directory.Build.props` `<Version>` is the only version to bump. The binary
stamps `X.Y.Z+<git-sha>` into `--version` at build time.

## Cutting a release

From a clean `master`:

```bash
# 1. Write the changelog section (from real git history, not stubs):
PREV=$(git describe --tags --abbrev=0 2>/dev/null || echo "")
git log --oneline ${PREV:+${PREV}..}HEAD
$EDITOR CHANGELOG.md        # add "## [X.Y.Z] - YYYY-MM-DD" at the top

# 2. Bump + commit + tag (+ push, when an 'origin' remote exists):
just release X.Y.Z

# 3. Watch CI:
gh run list --workflow=release.yml --limit 3
gh run watch
```

`just release` refuses a dirty worktree and a missing changelog section. If the
repo has no `origin` remote yet, it stops after tagging and prints the push
commands.

## Post-CI — finalize the release notes (mandatory)

CI's auto-generated notes are not the finished product. Once `release.yml` is
green, the releasing agent must replace them with the matching changelog
section before calling the release complete. Do not leave the auto-generated
notes as the published release body:

```bash
TAG=vX.Y.Z; VERSION=X.Y.Z

# Confirm assets:
gh release view "$TAG" --json assets --jq '.assets[].name'

# Replace auto-notes with the changelog section (keep assets untouched):
{
  awk "/^## \\[${VERSION}\\]/{flag=1; next} /^## \\[/{flag=0} flag" CHANGELOG.md
  echo
  echo "Binaries attached below (linux-x64, win-x64). Unzip and run — no install."
} > /tmp/gh-release-${TAG}.md
gh release edit "$TAG" --title "gbx-size-tree ${VERSION}" --notes-file /tmp/gh-release-${TAG}.md
```

Then smoke one published asset (`unzip`, `./gbx-size-tree --version`).

## Dry run (no publish)

Build + pack + smoke without creating a release — use after workflow changes:

```bash
gh workflow run release.yml -f dry_run=true
gh run watch
```

Manual dispatch is deliberately dry-run-only; the workflow rejects a manual run
with `dry_run=false` and tag-deletion events.

## Local packaging (no CI)

```bash
just package     # publish both RIDs + zips under artifacts/release/ (clean tree required)
```

## Done checklist

- [ ] `CHANGELOG.md` has the `## [X.Y.Z]` section (committed)
- [ ] Tag `vX.Y.Z` pushed, `release.yml` green
- [ ] Both zips attached to the GitHub Release
- [ ] Release body replaced with the changelog (`gh release edit`)
- [ ] Published binary smoke-tested (`--version`)
- [ ] This file still matches reality

## Troubleshooting

| Symptom | Fix |
|---------|-----|
| version job: props version ≠ tag | `just release X.Y.Z` bumps it; retag as a new patch, don't move tags |
| version job: missing changelog section | Write `## [X.Y.Z]` in `CHANGELOG.md`, commit, retag as new patch |
| `dotnet test` ran 0 tests | Something passed `-m:2` to `dotnet test` (MTP mode gotcha, docs/DECISIONS.md) |
| Release body still auto-notes only | You skipped Post-CI — run `gh release edit` |
| Binaries missing from release | Check the `Package release zips` step log; assets come from `artifacts/release/*.zip` |
