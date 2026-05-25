#!/usr/bin/env python3
import argparse
import json
import math
import socket
import threading
import time


JOINT_NAMES = ["joint_1", "joint_2", "joint_3", "joint_4", "joint_5", "joint_6"]
DEFAULT_CHANNELS = {
    "joint_position": True,
    "joint_velocity": False,
    "joint_effort": False,
    "tool_force": True,
    "tcp_pose": False,
    "extra_signals": False,
}


class MockState:
    def __init__(self, hz):
        self.lock = threading.Lock()
        self.running = True
        self.streaming = False
        self.recording = False
        self.hz = float(hz)
        self.experiment_id = "exp_manual"
        self.session_id = ""
        self.source_id = "dart_mock_py"
        self.mode = "idle_stream"
        self.motion = "idle"
        self.motion_error = ""
        self.phase_id = 0
        self.segment_id = 0
        self.channels = dict(DEFAULT_CHANNELS)
        self.target_deg = [0.0] * 6
        self.last_command = ""

    def snapshot(self):
        with self.lock:
            return {
                "running": self.running,
                "streaming": self.streaming,
                "recording": self.recording,
                "hz": max(1.0, self.hz),
                "experiment_id": self.experiment_id,
                "session_id": self.session_id,
                "source_id": self.source_id,
                "mode": self.mode,
                "motion": self.motion,
                "motion_error": self.motion_error,
                "phase_id": self.phase_id,
                "segment_id": self.segment_id,
                "channels": dict(self.channels),
                "target_deg": list(self.target_deg),
                "last_command": self.last_command,
            }

    def apply_command(self, cmd):
        name = str(cmd.get("cmd", "")).upper()
        with self.lock:
            self.last_command = name or "UNKNOWN"
            if cmd.get("experiment_id"):
                self.experiment_id = str(cmd["experiment_id"])
            if cmd.get("session_id"):
                self.session_id = str(cmd["session_id"])

            if name == "CREATE_SESSION":
                if not self.session_id:
                    self.session_id = "mock_" + time.strftime("%Y%m%d_%H%M%S")
                self.streaming = False
                self.recording = False
                self.segment_id = 0
                self.motion = "session_created"
            elif name in ("START_STREAM", "RESUME_STREAM"):
                self.streaming = True
                self.motion = "streaming"
            elif name in ("STOP_STREAM", "PAUSE_STREAM"):
                self.streaming = False
                self.recording = False
                self.motion = "stream_paused"
            elif name == "START_RECORD":
                self.streaming = True
                self.recording = True
                self.segment_id = int(cmd.get("segment_id", self.segment_id or 1) or 1)
                self.motion = "recording"
            elif name in ("STOP_RECORD", "PAUSE_RECORD"):
                self.recording = False
                self.motion = "streaming" if self.streaming else "idle"
            elif name == "RESUME_RECORD":
                self.streaming = True
                self.recording = True
                self.segment_id = int(cmd.get("segment_id", self.segment_id + 1) or self.segment_id + 1)
                self.motion = "recording"
            elif name == "CLOSE_SESSION":
                self.streaming = False
                self.recording = False
                self.motion = "closed"
            elif name == "SET_CHANNELS":
                channels = cmd.get("channels")
                if isinstance(channels, dict):
                    self.channels.update({k: bool(v) for k, v in channels.items() if k in self.channels})

                freqs = cmd.get("frequencies")
                if isinstance(freqs, dict) and float(freqs.get("dart_hz", 0) or 0) > 0:
                    self.hz = float(freqs["dart_hz"])
                elif float(cmd.get("dart_hz", 0) or 0) > 0:
                    self.hz = float(cmd["dart_hz"])
                self.motion = "channels_applied"
            elif name == "SET_MODE":
                self.mode = str(cmd.get("mode", self.mode))
                self.motion = "mode_switch"
            elif name == "MOVE_JOINT":
                target = cmd.get("target")
                if isinstance(target, list) and target:
                    self.target_deg = [float(v) for v in target[:6]]
                    while len(self.target_deg) < 6:
                        self.target_deg.append(0.0)
                self.motion = "moving"
            elif name == "HALT":
                self.motion = "halted"
            elif name == "SET_NETWORK_IMPAIRMENT":
                pass
            elif name == "RESET_NETWORK_IMPAIRMENT":
                pass

        return {"ok": True, "cmd": name, "status": "ACK", "ts_ms": wall_ms()}


def wall_ms():
    return time.time() * 1000.0


def build_packet(seq, snap, start_perf):
    t = time.perf_counter() - start_perf
    base = [
        20.0 * math.sin(t * 0.7),
        18.0 * math.sin(t * 0.55 + 0.7),
        15.0 * math.sin(t * 0.45 + 1.4),
        25.0 * math.sin(t * 0.8 + 2.1),
        12.0 * math.sin(t * 0.6 + 2.8),
        30.0 * math.sin(t * 0.5 + 3.5),
    ]
    target = snap["target_deg"]
    alpha = 0.15 if snap["motion"] == "moving" else 0.0
    positions = [round((1.0 - alpha) * base[i] + alpha * target[i], 6) for i in range(6)]
    now_wall = wall_ms()

    packet = {
        "schema_version": "dart_mock_v1",
        "experiment_id": snap["experiment_id"],
        "session_id": snap["session_id"],
        "source_id": snap["source_id"],
        "stream_id": "main",
        "seq": seq,
        "ts_ms": now_wall,
        "channel": "robot_state",
        "mode": snap["mode"],
        "motion": snap["motion"],
        "motion_error": snap["motion_error"],
        "phase_id": snap["phase_id"],
        "segment_id": snap["segment_id"],
        "stream_enabled": snap["streaming"],
        "record_enabled": snap["recording"],
        "exp": {
            "type": "STATE",
            "send_perf_ns": time.perf_counter_ns(),
            "send_wall_ms": now_wall,
        },
    }

    channels = snap["channels"]
    if channels.get("joint_position", True):
        packet["joint_states"] = {
            "name": JOINT_NAMES,
            "position": positions,
        }
    if channels.get("tool_force", True):
        packet["tool_force"] = {
            "frame_id": "tool0",
            "force": {
                "x": round(8.0 * math.sin(t * 1.1), 6),
                "y": round(5.0 * math.cos(t * 0.9), 6),
                "z": round(20.0 + 2.5 * math.sin(t * 0.4), 6),
            },
            "torque": {
                "x": round(0.4 * math.sin(t * 0.8), 6),
                "y": round(0.3 * math.cos(t * 0.75), 6),
                "z": round(0.2 * math.sin(t * 1.3), 6),
            },
        }
    return packet


def udp_stream_loop(args, state):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    seq = 0
    sent = 0
    last_report = time.perf_counter()
    start_perf = time.perf_counter()
    next_tick = time.perf_counter()
    target = (args.unity_ip, args.unity_port)

    while state.snapshot()["running"]:
        snap = state.snapshot()
        if args.send_without_stream or snap["streaming"]:
            seq += 1
            packet = build_packet(seq, snap, start_perf)
            payload = json.dumps(packet, separators=(",", ":")).encode("utf-8")
            sock.sendto(payload, target)
            sent += 1

        now = time.perf_counter()
        if now - last_report >= 1.0:
            print(
                f"[UDP] sent={sent}/s seq={seq} hz={snap['hz']:.1f} "
                f"stream={snap['streaming']} record={snap['recording']} cmd={snap['last_command']}"
            )
            sent = 0
            last_report = now

        interval = 1.0 / max(1.0, snap["hz"])
        next_tick += interval
        sleep_s = next_tick - time.perf_counter()
        if sleep_s > 0:
            time.sleep(sleep_s)
        else:
            next_tick = time.perf_counter()


def tcp_server_loop(args, state):
    server = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    server.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    server.bind((args.bind_ip, args.tcp_port))
    server.listen(4)
    server.settimeout(0.5)
    print(f"[TCP] listening on {args.bind_ip}:{args.tcp_port}")

    while state.snapshot()["running"]:
        try:
            client, addr = server.accept()
        except socket.timeout:
            continue
        print(f"[TCP] connected {addr}")
        threading.Thread(target=handle_tcp_client, args=(client, addr, state), daemon=True).start()


def handle_tcp_client(client, addr, state):
    with client:
        file = client.makefile("rwb", buffering=0)
        while state.snapshot()["running"]:
            line = file.readline()
            if not line:
                break
            try:
                cmd = json.loads(line.decode("utf-8").strip())
                ack = state.apply_command(cmd)
                file.write((json.dumps(ack, separators=(",", ":")) + "\n").encode("utf-8"))
                print(f"[TCP] {ack['cmd']} from {addr}")
            except Exception as exc:
                err = {"ok": False, "status": "ERROR", "error": str(exc), "ts_ms": wall_ms()}
                file.write((json.dumps(err, separators=(",", ":")) + "\n").encode("utf-8"))


def heartbeat_loop(args, state):
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock.bind((args.bind_ip, args.heartbeat_port))
    sock.settimeout(0.5)
    print(f"[UDP] heartbeat listening on {args.bind_ip}:{args.heartbeat_port}")
    last = 0.0

    while state.snapshot()["running"]:
        try:
            data, addr = sock.recvfrom(4096)
        except socket.timeout:
            continue
        now = time.perf_counter()
        if now - last > 1.0:
            print(f"[UDP] heartbeat from {addr}: {data[:80].decode('utf-8', errors='replace')}")
            last = now


def parse_args():
    parser = argparse.ArgumentParser(description="DartStudio-compatible configurable-rate Python data source mock for Unity.")
    parser.add_argument("--unity-ip", default="127.0.0.1")
    parser.add_argument("--unity-port", type=int, default=9090)
    parser.add_argument("--heartbeat-port", type=int, default=9091)
    parser.add_argument("--tcp-port", type=int, default=9092)
    parser.add_argument("--bind-ip", default="127.0.0.1")
    parser.add_argument("--hz", type=float, default=100.0)
    parser.add_argument("--send-without-stream", action="store_true")
    return parser.parse_args()


def main():
    args = parse_args()
    state = MockState(args.hz)

    threads = [
        threading.Thread(target=tcp_server_loop, args=(args, state), daemon=True),
        threading.Thread(target=heartbeat_loop, args=(args, state), daemon=True),
        threading.Thread(target=udp_stream_loop, args=(args, state), daemon=True),
    ]
    for thread in threads:
        thread.start()

    print("[MOCK] ready. In Unity: Play -> ExperimentSessionController: Connect, Create Session, Apply Channels + Hz, Start Stream, Start Record.")
    try:
        while True:
            time.sleep(0.5)
    except KeyboardInterrupt:
        with state.lock:
            state.running = False
        print("\n[MOCK] stopped")


if __name__ == "__main__":
    main()
