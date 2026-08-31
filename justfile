# gbx-size-tree — common tasks. Builds/tests capped at 2 threads (system etiquette).

sample := env_var_or_default("GBX_SIZE_TREE_SAMPLE", "/home/xertrov/Downloads/sample.Map.Gbx")

export MSBUILDDISABLENODEREUSE := "1"

default: build

# flock serializes builds when parallel implementation agents share this machine.
build:
    flock /tmp/gbx-size-tree.build.lock dotnet build -m:2

# NB: `-m:2` silently breaks MTP-mode `dotnet test` (runs 0 tests) — build first, test bare.
test: build
    flock /tmp/gbx-size-tree.build.lock dotnet test

# Run one test class, e.g. `just test-one SkippableScanner` (xunit v3 MTP wildcard filter)
test-one FILTER: build
    flock /tmp/gbx-size-tree.build.lock dotnet test -- --filter-class "*{{FILTER}}*"

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

clean:
    dotnet clean -m:2
    rm -rf artifacts
