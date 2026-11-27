#!/usr/bin/env bash
# Builds the browser demo reader into the client's public folder. The build is reproducible
# (-trimpath, pinned Go toolchain in go.mod), so CI can rebuild it and check the committed file.
set -euo pipefail
cd "$(dirname "$0")"
out=../RosterSensei.Client/public/demo-reader
mkdir -p "$out"
GOOS=js GOARCH=wasm go build -trimpath -buildvcs=false -ldflags="-s -w" -o "$out/demo-reader.wasm" ./cmd/wasm
cp "$(go env GOROOT)/lib/wasm/wasm_exec.js" "$out/wasm_exec.js"
