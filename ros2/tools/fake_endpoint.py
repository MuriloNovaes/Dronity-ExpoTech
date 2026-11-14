"""Endpoint de TESTE que imita o ROS-TCP-Endpoint (protocolo ROS 2), sem precisar de ROS.

Serve para validar o lado Unity: aceita a conexão do ROSConnection, faz o handshake,
lista os tópicos que o Unity registra/publica, envia /cmd_vel e decodifica /odom.

Uso (com o Unity em Play Mode ou entrando nele em seguida):
    python fake_endpoint.py --seconds 12 --drive
"""

from __future__ import annotations

import argparse
import json
import math
import re
import socket
import struct
import threading
import time
from collections import Counter


def frame(topic: str, payload: bytes) -> bytes:
    t = topic.encode("ascii")
    return struct.pack("<I", len(t)) + t + struct.pack("<I", len(payload)) + payload


def twist_cdr(lin_x: float, ang_z: float) -> bytes:
    # Cabeçalho CDR little-endian + 6 float64 (linear xyz, angular xyz).
    return b"\x00\x01\x00\x00" + struct.pack("<6d", lin_x, 0, 0, 0, 0, ang_z)


class Cdr:
    """Leitor CDR mínimo, com alinhamento relativo ao fim do cabeçalho de 4 bytes."""

    def __init__(self, data: bytes):
        self.d, self.o = data, 4

    def align(self, n: int):
        rel = self.o - 4
        self.o += (-rel) % n

    def u32(self) -> int:
        self.align(4); v = struct.unpack_from("<I", self.d, self.o)[0]; self.o += 4; return v

    def i32(self) -> int:
        self.align(4); v = struct.unpack_from("<i", self.d, self.o)[0]; self.o += 4; return v

    def f64(self) -> float:
        self.align(8); v = struct.unpack_from("<d", self.d, self.o)[0]; self.o += 8; return v

    def string(self) -> str:
        n = self.u32(); s = self.d[self.o:self.o + n - 1].decode("utf-8", "replace"); self.o += n; return s


def parse_odom(data: bytes) -> dict:
    r = Cdr(data)
    r.i32(); r.u32()  # stamp
    frame_id, child = r.string(), r.string()
    x, y, z = r.f64(), r.f64(), r.f64()
    qx, qy, qz, qw = r.f64(), r.f64(), r.f64(), r.f64()
    yaw = math.degrees(math.atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz)))
    return {"frame": frame_id, "child": child, "x": round(x, 3), "y": round(y, 3), "z": round(z, 3), "yaw_deg": round(yaw, 1)}


def read_exact(sock: socket.socket, n: int) -> bytes:
    buf = b""
    while len(buf) < n:
        chunk = sock.recv(n - len(buf))
        if not chunk:
            raise ConnectionError("Unity desconectou")
        buf += chunk
    return buf


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--port", type=int, default=10000)
    ap.add_argument("--seconds", type=float, default=12)
    ap.add_argument("--drive", action="store_true", help="envia /cmd_vel: frente 3 s, gira 2 s, para")
    ap.add_argument("--debug", action="store_true", help="mostra os primeiros frames recebidos")
    args = ap.parse_args()

    # Sem SO_REUSEADDR: no Windows ele deixaria dois endpoints escutando a mesma porta.
    srv = socket.socket()
    srv.bind(("0.0.0.0", args.port)); srv.listen(4)
    print(f"[fake-endpoint] aguardando o Unity na porta {args.port}...", flush=True)

    counts: Counter[str] = Counter()
    registrations: list[str] = []
    odoms: list[dict] = []
    stop = threading.Event()
    latest: dict = {}
    debug_frames: list | None = [] if args.debug else None

    def acceptor():
        # O Unity reconecta (ex.: reload de domínio ao entrar no Play Mode): sempre usar a última conexão.
        while not stop.is_set():
            try:
                c, addr = srv.accept()
            except OSError:
                return
            print(f"[fake-endpoint] Unity conectou de {addr}", flush=True)
            c.sendall(frame("__handshake", json.dumps({"version": "v0.7.0", "metadata": json.dumps({"protocol": "ROS2"})}).encode()))
            latest["conn"] = c
            threading.Thread(target=reader, args=(c,), daemon=True).start()

    def reader(conn: socket.socket):
        try:
            while not stop.is_set():
                tlen = struct.unpack("<I", read_exact(conn, 4))[0]
                # Em ROS 2 o nome vem com um byte nulo no fim (string CDR).
                topic = read_exact(conn, tlen).decode("utf-8").rstrip("\x00")
                plen = struct.unpack("<I", read_exact(conn, 4))[0]
                payload = read_exact(conn, plen)
                if debug_frames is not None and len(debug_frames) < 40:
                    debug_frames.append((topic, tlen, plen, payload[:8].hex()))
                if topic.startswith("__"):
                    m = re.search(rb"\{.*\}", payload, re.S)
                    if m:
                        info = json.loads(m.group(0))
                        registrations.append(f"{topic[2:]:9} {info.get('topic', '')} [{info.get('message_name', '')}]")
                    else:
                        registrations.append(f"{topic[2:]:9} (sem JSON, {len(payload)} bytes)")
                    continue
                if not topic:
                    continue  # keepalive
                counts[topic] += 1
                if topic == "/odom":
                    odoms.append(parse_odom(payload))
        except (ConnectionError, OSError) as exc:
            print(f"[fake-endpoint] conexão encerrada: {exc!r} (tópicos até aqui: {dict(counts)})", flush=True)
        except Exception as exc:  # erro de parsing: mostra em vez de morrer calado
            print(f"[fake-endpoint] leitor parou: {str(exc)[:200]}", flush=True)
            for f in debug_frames or []:
                print("   frame", f, flush=True)

    threading.Thread(target=acceptor, daemon=True).start()
    t0 = time.time()
    # O plano de comandos só começa quando a simulação já publica odometria.
    while not odoms and time.time() - t0 < 120:
        time.sleep(0.05)
    if not odoms:
        raise SystemExit("[fake-endpoint] nenhuma /odom em 120 s: o Unity está em Play Mode com o ROSConnection?")
    print(f"[fake-endpoint] primeira /odom {time.time() - t0:.1f} s após abrir a porta", flush=True)
    t0 = time.time()
    plan = [(2.0, 0.0, 0.0), (3.0, 0.3, 0.0), (2.0, 0.0, 1.0), (99, 0.0, 0.0)] if args.drive else [(99, 0, 0)]
    run_start = phase_start = time.time()
    i, sent, marks = 0, 0, {}
    while time.time() - run_start < args.seconds and i < len(plan):
        dur, lin, ang = plan[i]
        if time.time() - phase_start > dur:
            marks[i] = odoms[-1] if odoms else None
            i, phase_start = i + 1, time.time()
            continue
        if args.drive and i in (1, 2):
            latest["conn"].sendall(frame("/cmd_vel", twist_cdr(lin, ang))); sent += 1
        time.sleep(0.1)
    stop.set()
    elapsed = time.time() - t0

    print("\n== Registros feitos pelo Unity")
    for r in registrations:
        print("  ", r)
    print("\n== Mensagens recebidas do Unity (taxa média)")
    for topic, n in sorted(counts.items()):
        print(f"   {topic:40} {n:5d} msgs  ~{n / elapsed:5.1f} Hz")
    if args.drive:
        print(f"\n== /cmd_vel enviados: {sent}")
        for k, label in ((0, "antes de andar"), (1, "após 3 s a 0,3 m/s"), (2, "após girar 2 s a 1 rad/s")):
            print(f"   odom {label:28} {marks.get(k)}")
    latest["conn"].close(); srv.close()


if __name__ == "__main__":
    main()
