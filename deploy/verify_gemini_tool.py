"""Fail-fast live Google ADK function-calling probe. No Unity or recovery side effects."""
from __future__ import annotations

import asyncio
import os
import sys
import time
from pathlib import Path

from dotenv import load_dotenv


ROOT = Path(__file__).resolve().parents[1]
BACKEND = ROOT / "backend"
sys.path.insert(0, str(BACKEND))
load_dotenv(BACKEND / ".env.local")

from protocolrun.agents import ADKAgents  # noqa: E402


FAILURE_IDS = ["probe_failure_1", "probe_failure_2", "probe_failure_3"]


def snapshot() -> dict:
    failure_data = {
        "object_id": "CUBE_B",
        "attempt_id": "",
        "hand": "right",
        "tracked": True,
        "collider_enabled": True,
        "near_target": True,
        "input_received": True,
        "expected_grabbable": True,
        "restore_allowed": True,
        "held": False,
        "baseline_match": False,
        "hand_grab_count": 1,
        "enabled_hand_grab_count": 0,
        "companion_grab_count": 1,
        "enabled_companion_grab_count": 0,
        "distance_m": 0.0,
        "head_position": [0.12, 1.67, -0.31],
        "head_rotation": [0.0, 0.71, 0.0, 0.71],
        "left_hand_position": [-0.2, 1.2, 0.4],
        "right_hand_position": [0.2, 1.2, 0.4],
        "right_hand_rotation": [0.1, 0.2, 0.3, 0.9],
    }
    failures = []
    for index, event_id in enumerate(FAILURE_IDS, start=1):
        data = {**failure_data, "attempt_id": f"{index:032x}"}
        failures.append({"event_id": event_id, "seq": 80 + index,
                         "occurred_at": "2026-08-30T07:00:00Z", "kind": "grab_failed", "data": data})
    telemetry = [
        {"event_id": f"probe_telemetry_{index}", "seq": 90 + index,
         "occurred_at": "2026-08-30T07:00:00Z", "kind": "telemetry",
         "data": {**failure_data, "attempt_id": "", "fps": 72,
                  "frame": index, "participant_note": "raw telemetry must not enter the model prompt"}}
        for index in range(12)
    ]
    return {
        "protocol": {
            "adapter": "meta_hands",
            "target_id": "CUBE_B",
            "failure_threshold": 3,
            "allowed_actions": ["restore_hand_grab_baseline"],
        },
        "step": 3,
        "registered_baselines": {
            "CUBE_B": {
                "hand_grab_count": 1,
                "enabled_hand_grab_count": 1,
                "companion_grab_count": 1,
                "enabled_companion_grab_count": 1,
                "restore_allowed": True,
            }
        },
        "normal_practice_release_objects": ["CUBE_A"],
        "recovery_candidate": {
            "server_verified": True,
            "reason": "live_tool_connectivity_probe",
            "required_action": "restore_hand_grab_baseline",
            "failure_threshold": 3,
            "distinct_failed_attempt_count": 3,
            "qualifying_failure_event_ids": FAILURE_IDS,
        },
        "qualifying_failure_events": failures,
        # This mirrors the noisy production snapshot. ADKAgents must compact it before
        # making the real call; positions, rotations, notes and telemetry are excluded.
        "recent_events": failures + telemetry,
    }


async def run() -> None:
    if not os.environ.get("GOOGLE_API_KEY") and not os.environ.get("GOOGLE_CLOUD_PROJECT"):
        raise RuntimeError("No GOOGLE_API_KEY or Vertex AI project was loaded from backend/.env.local")
    started = time.perf_counter()
    result = await asyncio.wait_for(ADKAgents().diagnose(snapshot()), timeout=45)
    elapsed = time.perf_counter() - started
    if result.get("mode") != "google-adk":
        raise RuntimeError("The response did not come from Google ADK")
    if result.get("action") != "restore_hand_grab_baseline":
        raise RuntimeError(f"Unexpected action: {result.get('action')}")
    calls = result.get("tool_calls", [])
    if len(calls) != 1 or calls[0].get("tool") != "propose_recovery":
        raise RuntimeError(f"Expected one propose_recovery tool call, got: {calls}")
    if not set(FAILURE_IDS).issubset(set(result.get("evidence_ids", []))):
        raise RuntimeError("The tool call did not cite all three supplied failure IDs")
    print("[PASS] Real Google ADK function calling completed.")
    print(f"Model: {result.get('model')}")
    print("Tool: propose_recovery")
    print("Action: restore_hand_grab_baseline")
    print(f"Payload: {result.get('input_bytes')} -> {result.get('model_payload_bytes')} bytes (compact server-verified evidence)")
    print(f"Elapsed: {elapsed:.2f} seconds")
    print("No Unity command was executed; this was a connectivity probe only.")


if __name__ == "__main__":
    try:
        asyncio.run(run())
    except Exception as error:
        print(f"[FAIL] Real Google ADK tool call did not complete: {type(error).__name__}: {error}")
        raise SystemExit(1)
