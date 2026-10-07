#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

cd "$ROOT"
dotnet tool restore
cd "$ROOT/CSharpUnitTests"
dotnet stryker "$@"
