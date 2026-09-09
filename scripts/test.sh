#!/usr/bin/env bash
# Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
# test.sh - Builds, then runs every automated test.
#
# Exits 0 when everything is green and 1 when anything is red, so this script can be
# used as a gate by another script without anybody reading its output.
#
# Note: this does not call "dotnet test". The test project carries its own runner
# (tests/Atm.Tests/TestKit/) because the build environment cannot reach the package
# feed (KARAR-006). From the outside the difference is invisible.
#
# Usage: ./scripts/test.sh
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build ATMSIM.slnx -v q --nologo
dotnet run --project tests/Atm.Tests/Atm.Tests.csproj --no-build
