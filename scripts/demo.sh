#!/usr/bin/env bash
# Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
# demo.sh - Starts the whole machine with one command: host, terminal, browser.
#
# What it does, in order: builds, frees the two ports if a previous run left them held,
# starts the host and waits until its port is really accepting connections, starts the
# terminal, waits for the screen port the same way, then opens the browser.
#
# Why it waits for the ports instead of sleeping for a couple of seconds: a fixed sleep is
# right on the machine it was written on and wrong on a slower one, and it fails in front
# of an audience. Asking whether the port answers is the same question, asked properly.
#
# Why it runs the built binaries directly instead of "dotnet run", and this is the fix for
# a real bug that hit a live demo:
#
#   "dotnet run" is not the program. It is a launcher that BUILDS a child process and waits
#   for it. So $! - the PID the shell remembers - is the launcher's, not the machine's.
#   Ctrl+C killed the launcher, the launcher died, and Atm.Host went on holding port 9099
#   as an orphan. The next run then failed with "Address already in use", and "killall
#   dotnet" did not help either: by then the surviving process was not called dotnet, it
#   was called Atm.Host.
#
#   Running the binary directly makes $! the machine itself, so the trap below actually
#   stops what it says it stops.
#
# Usage: ./scripts/demo.sh [host-port] [screen-port] [card]
set -euo pipefail
cd "$(dirname "$0")/.."
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

# "--kapat" bir port degildir. Konumsal argumanlar okunmadan once ayiklanir, yoksa
# HOST_PORT "--kapat" olur ve hata mesajlari anlamsiz bir port numarasi gosterir.
KAPAT_MODU=0
if [ "${1:-}" = "--kapat" ] || [ "${1:-}" = "--stop" ]; then
  KAPAT_MODU=1
  shift
fi

# "--yeni-gun" arsivler ve temiz bir gunle baslar.
#
# Neden var: defter (_veri/host-journal.jsonl) eklemeli bir kayittir ve hicbir zaman
# silinmez - silinebilseydi kayit olmazdi. Ama bir demo gunu bittiginde bir sonrakine
# temiz baslamak istemek dogal bir istektir, ve gercek bankalarda da gun sonunda defter
# arsivlenir. Bu bayrak tam olarak onu yapar: siler degil, tasir.
if [ "${1:-}" = "--yeni-gun" ]; then
  DAMGA="$(date +%Y%m%d-%H%M%S)"
  HEDEF="_veri/_eski/$DAMGA"
  if [ -d "_veri" ]; then
    mkdir -p "$HEDEF"
    for f in _veri/*.jsonl; do
      [ -e "$f" ] || continue
      mv "$f" "$HEDEF/"
      echo "Arsivlendi: $f -> $HEDEF/"
    done
  fi
  echo "Temiz gun hazir. Simdi ./scripts/demo.sh calistirabilirsiniz."
  exit 0
fi

HOST_PORT="${1:-9099}"
SCREEN_PORT="${2:-8080}"
CARD="${3:-4111111111111111}"


# Is anything listening on this port right now?
#
# Asked with bash's own /dev/tcp rather than lsof, and that choice is the fix for the bug
# this function used to have. The first version asked lsof, lsof answered "nothing", and
# the script cheerfully started a second host onto an occupied port. Whatever the reason
# lsof stayed quiet - a flag it read differently, a process it would not report - the
# script trusted a tool's silence over the observable fact that the port answered.
#
# /dev/tcp cannot be quiet in that way: it either connects or it does not. It is also
# exactly what "bekle" below uses to decide the machine has started, so start-up and
# clean-up now judge the same thing by the same measure. A port cannot be simultaneously
# "occupied enough to prove the host is up" and "free enough to bind".
dolu_mu() {
  if (exec 3<>"/dev/tcp/127.0.0.1/$1") 2>/dev/null; then
    exec 3>&- 3<&-
    return 0
  fi
  return 1
}

# Stops leftover copies of THIS project's two programs and nothing else.
#
# Matched by command line rather than by port, because the process holding the port is the
# thing we could not see. Anything whose command line contains Atm.Host/bin/ is a build
# output of this project - there is nothing else on a machine that looks like that - so the
# match is narrow enough to be safe and wide enough to catch a copy started by the old
# "dotnet run" version of this script, which is where the orphans came from.
#
# Deliberately NOT done: killing whatever happens to hold the port. One day that is
# somebody's own web server on 8080, and a demo script that kills a stranger's process is
# worse than a demo script that refuses to start.
kalinti_temizle() {
  pkill -f "Atm\.Host/bin/" 2>/dev/null || true
  pkill -f "Atm\.Terminal/bin/" 2>/dev/null || true
}

# Makes sure a port is free before anything is started, and says out loud what it did.
serbest_birak() {
  local port="$1" ad="$2" i

  if ! dolu_mu "$port"; then
    return 0
  fi

  echo "Port $port dolu - onceki bir calistirmadan kalmis olabilir, kapatiliyor..."
  kalinti_temizle

  # A killed process does not release its port on the same instant. Wait for the port to
  # actually go quiet rather than assuming it did; the assumption is what produced the
  # error this whole function exists to prevent.
  for i in $(seq 1 50); do
    if ! dolu_mu "$port"; then
      echo "Port $port bosaldi."
      return 0
    fi
    sleep 0.1
  done

  echo "" >&2
  echo "Port $port hala dolu ve bizim programimiz degil." >&2
  echo "Onu kullanan seye dokunulmadi. Iki secenegin var:" >&2
  echo "  1) Baska port ver:  ./scripts/demo.sh $((HOST_PORT + 10)) $((SCREEN_PORT + 10))" >&2
  echo "  2) Kim tuttugunu gor:  lsof -nP -iTCP:$port -sTCP:LISTEN" >&2
  exit 1
}

# "./scripts/demo.sh --kapat" stops everything this project left running and exits.
# A one-line escape hatch, so that a stuck demo never needs a remembered kill command.
if [ "$KAPAT_MODU" = "1" ]; then
  echo "Bu projeden kalan surecler kapatiliyor..."
  kalinti_temizle
  sleep 0.5
  for p in "$HOST_PORT" "$SCREEN_PORT"; do
    if dolu_mu "$p"; then
      echo "  port $p hala dolu (baska bir program)."
    fi
  done
  echo "Bitti."
  exit 0
fi

echo "Derleniyor..."
dotnet build ATMSIM.slnx -v q --nologo

HOST_BIN="src/Atm.Host/bin/Debug/net10.0/Atm.Host"
TERM_BIN="src/Atm.Terminal/bin/Debug/net10.0/Atm.Terminal"

for bin in "$HOST_BIN" "$TERM_BIN"; do
  if [ ! -x "$bin" ]; then
    echo "Beklenen dosya bulunamadi: $bin" >&2
    echo "Derleme basarili gorunuyor ama cikti baska bir yerde. ./scripts/build.sh koşun." >&2
    exit 1
  fi
done


serbest_birak "$HOST_PORT" "Host"
serbest_birak "$SCREEN_PORT" "Ekran"

HOST_PID=""
TERM_PID=""

kapat() {
  echo ""
  echo "Kapatiliyor..."
  [ -n "$TERM_PID" ] && kill "$TERM_PID" 2>/dev/null || true
  [ -n "$HOST_PID" ] && kill "$HOST_PID" 2>/dev/null || true

  # Belt and braces. Killing by pid is the right thing and normally enough, but it only
  # works if this script is still alive to do it - and it was not, in the test that found
  # this line: the script was killed from outside and both machines outlived it. That is
  # exactly how the orphans that caused the original bug were born, so the clean-up now
  # ends by asking the same question the start-up asks, by name rather than by pid.
  kalinti_temizle

  wait 2>/dev/null || true
}
trap kapat EXIT INT TERM

# Waits until something is listening on a port, or gives up after ten seconds.
bekle() {
  local port="$1" ad="$2" i
  for i in $(seq 1 100); do
    if (exec 3<>"/dev/tcp/127.0.0.1/$port") 2>/dev/null; then
      exec 3>&- 3<&-
      return 0
    fi
    sleep 0.1
  done
  echo "$ad ($port) acilmadi." >&2
  return 1
}

echo "Host baslatiliyor (port $HOST_PORT)..."
"./$HOST_BIN" "$HOST_PORT" &
HOST_PID=$!
bekle "$HOST_PORT" "Host"

echo "Terminal baslatiliyor (ekran portu $SCREEN_PORT)..."
"./$TERM_BIN" "$HOST_PORT" "$SCREEN_PORT" "$CARD" &
TERM_PID=$!
bekle "$SCREEN_PORT" "Ekran"

ADRES="http://127.0.0.1:$SCREEN_PORT/"
echo ""
echo "ATM ekrani: $ADRES"

# The screen must be opened from the address the terminal serves, never as a file.
# A page opened from disk has no host to connect back to - ortam notları.
if command -v open >/dev/null 2>&1; then
  open "$ADRES"
elif command -v xdg-open >/dev/null 2>&1; then
  xdg-open "$ADRES"
else
  echo "Tarayiciyi kendiniz acin: $ADRES"
fi

echo "Durdurmak icin Ctrl+C."
wait
