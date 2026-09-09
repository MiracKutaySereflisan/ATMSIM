#!/usr/bin/env python3
# Copyright (c) 2026 Mirac Kutay Sereflisan. MIT lisansi - LICENSE dosyasina bakiniz.
# ekran-provasi.py - Uçtan uca prova: iki süreci başlatır, tarayıcının yerine geçer,
# ve demo arıza anahtarlarının uçtan uca çalıştığını gösterir.
#
# Neden var: demo yolunun provası da yapılmalı ve bu prova bitmiş sayılma koşullarından
# biridir. Elle yapılan bir prova, bir sonraki
# değişiklikten sonra tekrarlanmaz; koşulabilir bir prova tekrarlanır.
#
# Ne kapsıyor: gerçek host süreci, gerçek terminal süreci, gerçek TCP soketi, gerçek
# WebSocket el sıkışması ve çerçevelemesi, gerçek ekran sözleşmesi, servis paneli.
# Yani tarayıcı dışındaki her şey. Tarayıcının kendi çizimi (animasyonlar, tam ekran)
# bir insanın bakmasını gerektiriyor ve elle denetlenir.
#
# Neden Python: tek dosya, ek bağımlılık istemiyor. python3 kurulu olmayan bir makinede
# çalışmaz - o yüzden bu prova bir kapı değil, bir araçtır;
# aynı yolun testlerle kapsanan kısmı ScreenServerTests içindedir ve ./scripts/test.sh
# ile her zaman koşar.
#
# Kullanım: python3 scripts/ekran-provasi.py
#   Host 9501, ekran 8501 portlarını kullanır. Bitince iki süreci de kapatır.

import base64, json, os, socket, struct, subprocess, sys, time, hashlib

def ws_connect(host, port):
    s = socket.create_connection((host, port), timeout=5)
    key = base64.b64encode(os.urandom(16)).decode()
    s.sendall((f"GET /ws HTTP/1.1\r\nHost: {host}:{port}\r\nUpgrade: websocket\r\n"
               f"Connection: Upgrade\r\nSec-WebSocket-Key: {key}\r\n"
               f"Sec-WebSocket-Version: 13\r\n\r\n").encode())
    buf = b""
    while b"\r\n\r\n" not in buf:
        buf += s.recv(4096)
    assert b"101" in buf.split(b"\r\n")[0], buf.split(b"\r\n")[0]
    return s, buf.split(b"\r\n\r\n", 1)[1]

def send(s, text):
    data = text.encode()
    mask = os.urandom(4)
    masked = bytes(b ^ mask[i % 4] for i, b in enumerate(data))
    n = len(data)
    if n < 126: hdr = struct.pack("!BB", 0x81, 0x80 | n)
    else: hdr = struct.pack("!BBH", 0x81, 0x80 | 126, n)
    s.sendall(hdr + mask + masked)

class Reader:
    def __init__(self, sock, rest): self.s, self.buf = sock, rest
    def _need(self, n):
        while len(self.buf) < n:
            d = self.s.recv(4096)
            if not d: raise EOFError
            self.buf += d
    def frame(self):
        self._need(2)
        b1, b2 = self.buf[0], self.buf[1]
        n = b2 & 0x7F; off = 2
        if n == 126:
            self._need(4); n = struct.unpack("!H", self.buf[2:4])[0]; off = 4
        self._need(off + n)
        payload = self.buf[off:off+n]; self.buf = self.buf[off+n:]
        return payload.decode()

host = subprocess.Popen(["src/Atm.Host/bin/Debug/net10.0/Atm.Host", "9501"],
                        stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
time.sleep(1.5)
term = subprocess.Popen(["src/Atm.Terminal/bin/Debug/net10.0/Atm.Terminal", "9501", "8501",
                         "4111111111111111"], stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
time.sleep(2.0)
try:
    s, rest = ws_connect("127.0.0.1", 8501)
    r = Reader(s, rest)
    def step(ev, val, label, bekle=True):
        send(s, json.dumps({"event": ev, "value": val}))
        if not bekle:
            print(f"  {label:28} -> (cevap beklenmedi)")
            return None
        g = json.loads(r.frame())
        print(f"  {label:28} -> {g.get('screen'):10} {g.get('title','')[:34]:34} demo={g.get('demo','')!r}")
        return g
    step("hello", "", "bağlan")
    step("card", "inserted", "kart tak")
    for d in "1234": step("key", d, f"pin {d}")
    g = step("key", "enter", "GİRİŞ")
    g = step("demo", "hat-kes", "DEMO: hattı kes")
    assert "hat kesik" in g.get("demo",""), g
    g = step("soft", "L1", "BAKİYE (hat kesikken)")
    assert "hat kesik" in g.get("demo",""), g
    assert g.get("screen") != "balance", g
    g = step("demo", "hat-gelsin", "DEMO: hat gelsin")
    assert g.get("demo","") == "", g
    g = step("demo", "sikisma", "DEMO: sıkışma")
    assert "sıkışık" in g.get("demo",""), g
    g = step("demo", "makine-duzelsin", "DEMO: makine düzelsin")
    assert g.get("demo","") == "", g
    print("PROVA TAMAM")
finally:
    term.terminate(); host.terminate()
    time.sleep(0.5)
    out = term.stdout.read() if term.stdout else ""
    print("--- terminal cikti (DEMO satirlari) ---")
    for line in out.splitlines():
        if "DEMO" in line or "ekran ->" in line: print("   ", line)
