#!/bin/sh
set -eu

root=${GBX_SIZE_TREE_SB2:-/home/xertrov/tm-docs/Maps/SB2}
old=$root/'Sweet 2 burger v205.Map.gbx'
new=$root/'Sweet 2 burger v206.Map.Gbx'
old_sha256=b2802168218f1e6bfc26b3834d7934084eacaccfd5e108857bf3a35dabe88870
new_sha256=e784e1b1078ded693da232ae044b7effe2b60077e076ce241b08a3ebfcffec2e
out=${GBX_REAL_ARTIFACT_PREFIX:-/tmp/gbx-final-real}
dll=$PWD/src/GbxSizeTree/bin/Debug/net10.0/gbx-size-tree.dll

[ -f "$old" ] || { echo "missing OLD map: $old" >&2; exit 2; }
[ -f "$new" ] || { echo "missing NEW map: $new" >&2; exit 2; }

verify_fixture() {
    label=$1
    path=$2
    expected=$3
    actual=$(sha256sum -- "$path" | cut -d ' ' -f 1)
    [ "$actual" = "$expected" ] || {
        echo "incorrect $label fixture: $path" >&2
        echo "expected SHA-256: $expected" >&2
        echo "actual SHA-256:   $actual" >&2
        exit 2
    }
}

verify_fixture OLD "$old" "$old_sha256"
verify_fixture NEW "$new" "$new_sha256"

flock --close /tmp/gbx-size-tree.build.lock dotnet build -m:2

capture() {
    name=$1
    shift
    temporary=$out-$name.tmp
    trap 'rm -f -- "$temporary"' EXIT HUP INT TERM
    dotnet "$dll" diff "$@" -- "$old" "$new" > "$temporary"
    mv -f -- "$temporary" "$out-$name"
    trap - EXIT HUP INT TERM
}

capture forward-default.txt --no-color
capture forward-default.json --json
capture forward-default.md --markdown
capture forward-default-styled.html --html --styled
capture forward-default-plain.html --html --not-styled
capture forward-all.txt --all --no-color
capture forward-all.json --all --json
capture forward-all.md --all --markdown
capture forward-all-styled.html --all --html --styled
capture forward-all-plain.html --all --html --not-styled

dotnet "$dll" diff --json -- "$new" "$old" > "$out-reverse-default.json"
dotnet "$dll" diff --all --json -- "$new" "$old" > "$out-reverse-all.json"
dotnet "$dll" diff --json -- "$new" "$new" > "$out-self-default.json"
dotnet "$dll" diff --all --json -- "$new" "$new" > "$out-self-all.json"

GBX_SIZE_TREE_SB2=$root flock --close /tmp/gbx-size-tree.build.lock \
    dotnet test --no-build -- --filter-class '*RealMapDiffRegressionTests*'
