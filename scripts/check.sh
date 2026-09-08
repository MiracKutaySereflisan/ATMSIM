#!/usr/bin/env bash
# Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
# check.sh - Runs one day through the simulator and reports whether the money adds up.
#
# This is the money-conservation checker of Phase 2g in its one-command form. It plays a
# fixed, seeded day - twelve withdrawals with dispenser faults and lost answers scattered
# through them - and then compares the machine's cash, the host's ledger, the host's
# record and the terminal's record against each other.
#
# Exits 0 when nothing is unexplained and 1 when something is, so it can be used as a gate
# by another script without anybody reading its output.
#
# Usage: ./scripts/check.sh [seed]
#   The seed decides which faults happen where. The same seed always produces the same day
#   (rule 5: a run that cannot be repeated is not a result).
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build ATMSIM.slnx -v q --nologo
dotnet run --project src/Atm.Audit/Atm.Audit.csproj --no-build -- "${1:-}"
