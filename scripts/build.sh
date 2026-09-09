#!/usr/bin/env bash
# Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
# build.sh - Builds every project in the solution.
#
# Why a script instead of typing the dotnet command: every operation this repository
# supports is one script in this folder, named after what it does. Nobody has to
# remember a command line.
#
# Usage: ./scripts/build.sh
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build ATMSIM.slnx "$@"
