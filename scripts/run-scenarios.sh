#!/usr/bin/env bash
# run-scenarios.sh - Runs every fault scenario in scenarios/ and prints what happened.
#
# What this is: the deliverable of Phase 4. Each file in scenarios/ describes one way for
# something to go wrong, in data rather than in code (rule 5 (docs/proje-kurallari.md)), together with
# what SHOULD happen - written before the run, never adjusted to it.
#
# What the output says, in order: one line per scenario, then the coverage matrix with its
# empty cells named, then the summary. The number that matters is not how many passed - a
# pass means the system did what the scenario said it should. The numbers that say something
# about the system are how many scenarios end with an unexplained difference and, of those,
# where the difference was visible: at the moment, at the end of the day, or nowhere.
#
# Exits 0 when every scenario did what was expected of it, 1 when any did not - so this
# script can be used as a gate without anybody reading its output.
#
# Usage: ./scripts/run-scenarios.sh [klasör]
#   The folder defaults to scenarios/. Every run is reproducible: each scenario carries its
#   own seed and nothing here sleeps or asks the wall clock.
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
dotnet build ATMSIM.slnx -v q --nologo
dotnet run --project src/Atm.Audit/Atm.Audit.csproj --no-build -- senaryolar "${1:-scenarios}"
