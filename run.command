#!/bin/zsh
set -eu
cd "${0:A:h}"
dotnet run --project src/Zenith.Mac -- "$@"
