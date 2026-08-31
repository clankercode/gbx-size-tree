# gbx-size-tree — common tasks. Builds/tests capped at 2 threads (system etiquette).

sample := env_var_or_default("GBX_SIZE_TREE_SAMPLE", "/home/xertrov/Downloads/sample.Map.Gbx")
version := `grep -oPm1 '(?<=<Version>)[^<]+' Directory.Build.props`

export MSBUILDDISABLENODEREUSE := "1"

default: build

# flock serializes builds when parallel implementation agents share this machine.
build:
    flock --close /tmp/gbx-size-tree.build.lock dotnet build -m:2

# NB: `-m:2` silently breaks MTP-mode `dotnet test` (runs 0 tests) — build first, test bare.
test: build
    flock --close /tmp/gbx-size-tree.build.lock dotnet test --no-build

# Run one test class, e.g. `just test-one SkippableScanner` (xunit v3 MTP wildcard filter)
test-one FILTER: build
    flock --close /tmp/gbx-size-tree.build.lock dotnet test --no-build -- --filter-class "*{{FILTER}}*"

# Rewrite tests/…/golden-fake-analysis.json from the current serializer output, then verify.
golden-update: build
    GBX_SIZE_TREE_UPDATE_GOLDEN=1 flock --close /tmp/gbx-size-tree.build.lock dotnet test --no-build -- --filter-class "*JsonReportWriter*"

# NB: `dotnet run -m:2` forwards -m:2 to the app; build first, then run --no-build.
run *ARGS: build
    dotnet run --project src/GbxSizeTree --no-build -- {{ARGS}}

analyze: build
    dotnet run --project src/GbxSizeTree --no-build -- "{{sample}}"

optimize: build
    dotnet run --project src/GbxSizeTree --no-build -- "{{sample}}" --optimize

publish-linux:
    dotnet publish src/GbxSizeTree -m:2 -c Release -r linux-x64 -o artifacts/linux-x64

publish-win:
    dotnet publish src/GbxSizeTree -m:2 -c Release -r win-x64 -o artifacts/win-x64

publish: publish-linux publish-win

# Release zips (binary + license/notices/readme), one per OS, under artifacts/release/.
release: publish
    rm -rf artifacts/release && mkdir -p artifacts/release/stage
    cp LICENSE THIRD-PARTY-NOTICES.md README.md artifacts/release/stage/
    cp artifacts/linux-x64/gbx-size-tree artifacts/release/stage/
    cd artifacts/release/stage && zip -q ../gbx-size-tree-{{version}}-linux-x64.zip gbx-size-tree LICENSE THIRD-PARTY-NOTICES.md README.md
    rm artifacts/release/stage/gbx-size-tree
    cp artifacts/win-x64/gbx-size-tree.exe artifacts/release/stage/
    cd artifacts/release/stage && zip -q ../gbx-size-tree-{{version}}-win-x64.zip gbx-size-tree.exe LICENSE THIRD-PARTY-NOTICES.md README.md
    rm -rf artifacts/release/stage
    ls -l artifacts/release/

clean:
    dotnet clean -m:2
    rm -rf artifacts
