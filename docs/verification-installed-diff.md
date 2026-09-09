# Installed diff release verification

The final implementation was installed from clean `master` at `28d8ade230b4446e51fb6357ee894e89226628fe` using `just install`, with the shared build lock held. Linux trimmed publication and installation to `~/.local/bin/gbx-size-tree` succeeded.

The installed executable reports `0.1.0+28d8ade230b4446e51fb6357ee894e89226628fe`. Pathless `diff --help` succeeded and includes the final image and progress contracts.

Installed-executable self-diffs of `Sweet 2 burger v206.Map.Gbx` produced `/tmp/gbx-installed-self.png` and `/tmp/gbx-installed-self.webp`. Both decode at 1400×1288 and have identical RGBA pixels. All four stdout/stderr captures were empty. This exercises embedded font loading and both lossless encoders in the trimmed release, rather than only the debug build.

Evidence:

- `/tmp/gbx-final-install.log`
- `/tmp/gbx-installed-help.txt`
- `/tmp/gbx-installed-{image,webp}.{stdout,stderr}`
- `docs/verification-final-diff.md` for the 677-test gate, real forward/reverse/self comparisons, browser interactions, and terminal progress checks.

Only documentation and backlog bookkeeping follow this installed source revision. No push or release publication was performed.
