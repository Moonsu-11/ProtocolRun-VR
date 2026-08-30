"""Pure state transitions. Model text is never executable configuration."""
from __future__ import annotations

import copy
import hashlib
import json
import time
import uuid
from typing import Literal

from pydantic import BaseModel, ConfigDict, Field, model_validator


DIAGNOSIS_EVIDENCE_TTL_SECONDS = 240


def uid() -> str:
    return uuid.uuid4().hex


class StrictModel(BaseModel):
    model_config = ConfigDict(extra="forbid")


class Protocol(StrictModel):
    id: str = Field(default="object-study-v1", pattern=r"^[a-zA-Z0-9_-]{1,64}$")
    title: str = Field(default="Object interaction study", min_length=1, max_length=120)
    target_id: str = Field(default="target_blue", pattern=r"^[a-zA-Z0-9_-]{1,64}$")
    adapter: Literal["xri_layers", "meta_hands"] = "xri_layers"
    practice_id: str = Field(default="CUBE_A", pattern=r"^[a-zA-Z0-9_-]{1,64}$")
    protected_id: str = Field(default="CUBE_C", pattern=r"^[a-zA-Z0-9_-]{1,64}$")
    demo_faults_allowed: bool = False
    auto_inject_target_fault: bool = False
    near_distance_m: float = Field(default=0.18, ge=0.03, le=0.3)
    baseline_mask: int = Field(default=1, ge=1, le=2147483647)
    max_recoveries: int = Field(default=1, ge=0, le=3)
    failure_threshold: int = Field(default=3, ge=2, le=10)
    allowed_actions: list[Literal["restore_interaction_layer", "restore_hand_grab_baseline"]] = Field(default_factory=lambda: ["restore_interaction_layer"], max_length=1)

    @model_validator(mode="after")
    def valid_adapter(self):
        if self.adapter == "meta_hands":
            if len({self.target_id, self.practice_id, self.protected_id}) != 3:
                raise ValueError("Target, practice and protected object IDs must differ")
            if any(a != "restore_hand_grab_baseline" for a in self.allowed_actions):
                raise ValueError("Meta hands permits only baseline component restoration")
            if self.auto_inject_target_fault and not self.demo_faults_allowed:
                raise ValueError("Automatic Meta fault injection requires demo_faults_allowed")
        else:
            if self.auto_inject_target_fault:
                raise ValueError("Automatic target fault injection is supported only by the Meta hands adapter")
            if any(a != "restore_interaction_layer" for a in self.allowed_actions):
                raise ValueError("XRI permits only layer restoration")
        return self


def meta_protocol() -> Protocol:
    return Protocol(id="meta-hands-v1", title="Three-cube hand interaction study", adapter="meta_hands",
                    target_id="CUBE_B", demo_faults_allowed=True, auto_inject_target_fault=True,
                    allowed_actions=["restore_hand_grab_baseline"])


def recovery_action(s: dict) -> str:
    return "restore_hand_grab_baseline" if s["protocol"].get("adapter") == "meta_hands" else "restore_interaction_layer"


def steps(s: dict) -> list[dict]:
    if s["protocol"].get("adapter") != "meta_hands":
        return STEPS
    p = s["protocol"]
    instructions = ["Confirm consent and show both hands.",
                    f"Grab and release {p['practice_id']} once. Do not practice with {p['target_id']}.",
                    f"Bring your hand close to {p['target_id']}.", f"Pinch to pick up {p['target_id']}.",
                    f"Release {p['target_id']} inside the drop zone.",
                    "Rate task difficulty from 1 (easy) to 7 (hard), then submit."]
    return [{**step, "instruction": instruction} for step, instruction in zip(STEPS, instructions)]


STEPS = [
    {"id": "equipment", "instruction": "양손 컨트롤러와 목표 물체 설정을 확인해주세요.", "event": "equipment_ready"},
    {"id": "practice", "instruction": "연습 물체를 잡았다가 놓아주세요.", "event": "practice_completed"},
    {"id": "find", "instruction": "파란색 목표 물체를 찾아 바라봐주세요.", "event": "target_found"},
    {"id": "grab", "instruction": "파란색 목표 물체를 잡아주세요.", "event": "grab_success"},
    {"id": "place", "instruction": "목표 물체를 지정된 영역에 놓아주세요.", "event": "placed"},
    {"id": "survey", "instruction": "과제를 수행하는 과정에서 어려웠던 점을 말씀해주세요.", "event": "survey_completed"},
]


class Event(StrictModel):
    event_id: str = Field(pattern=r"^[a-zA-Z0-9_-]{1,64}$")
    seq: int = Field(ge=1)
    occurred_at: str = Field(max_length=40)
    kind: Literal["consent", "object_registered", "object_observation", "non_grabbable_attempt", "equipment_ready", "practice_completed", "target_found", "grab_attempt", "grab_failed", "grab_success", "released", "placed", "telemetry", "help_request", "pause_request", "survey_completed", "fault_injected", "client_error"]
    data: dict = Field(default_factory=dict)


class EventBatch(StrictModel):
    events: list[Event] = Field(min_length=1, max_length=25)


class Ack(StrictModel):
    success: bool
    observed_mask: int = Field(default=0, ge=0, le=2147483647)
    message: str = Field(default="", max_length=240)
    baseline_id: str = Field(default="", max_length=64)
    run_id: str = Field(default="", max_length=64)
    baseline_match: bool = False
    held: bool = False


def new_session(protocol: Protocol, token: str, sid: str, now: float | None = None) -> dict:
    now = now or time.time()
    p = protocol.model_dump()
    return {"id": sid, "protocol": p, "protocol_hash": hashlib.sha256(json.dumps(p, sort_keys=True).encode()).hexdigest(),
            "token_hash": hashlib.sha256(token.encode()).hexdigest(), "created_at": now,
            "last_seen": None, "step": 0, "status": "running", "revision": 0,
            "last_seq": 0, "event_count": 0, "recent": [], "commands": [], "audit": [],
            # Keep bounded failure records outside the rolling UI/event window. Meta telemetry can
            # otherwise evict the exact attempt/failure evidence while Gemini is still diagnosing.
            "failure_evidence": [],
            "diagnosis_evidence_ids": [], "diagnosis_started_at": None,
            "recoveries": 0, "failures": 0, "needs_diagnosis": False,
            "lease": None, "diagnosis": None, "verification": None, "segments": [],
            "step_history": [], "step_started": now, "survey": None, "telemetry": {},
            "invalid_from_seq": None, "retest_after_seq": None, "report": None,
            "consent": None, "objects": {}, "practice_grabbed": [], "practice_released": [],
            "meta_attempts": {}, "restore_command_id": None, "retest_command_id": None}


def public_session(s: dict, researcher: bool = False) -> dict:
    data = copy.deepcopy(s)
    diagnosis_pending = bool(data.get("needs_diagnosis")) and data.get("status") == "running"
    data["agent_busy"] = bool(data.get("lease")) or diagnosis_pending
    data["agent_job"] = (data.get("lease", {}).get("job") if data.get("lease") else
                         "diagnosis" if diagnosis_pending else None)
    data.pop("token_hash", None)
    data.pop("lease", None)
    data["current_step"] = steps(s)[s["step"]] if s["step"] < len(STEPS) else {"id": "complete", "instruction": "Study complete. Thank you."}
    data["steps"] = steps(s)
    data["progress"] = round(s["step"] / len(STEPS) * 100)
    if not researcher:
        # No diagnosis, hypothesis, or raw participant answers in the participant response.
        data = {k: data[k] for k in ["id", "protocol", "protocol_hash", "step", "status", "agent_busy", "current_step", "progress", "last_seq", "revision"]}
    return data


def audit(s: dict, kind: str, detail: dict, now: float) -> None:
    s["audit"].append({"id": uid(), "kind": kind, "at": now, "detail": detail})
    s["audit"] = s["audit"][-100:]


def command(s: dict, action: str, now: float) -> None:
    # Model never supplies the target, mask, or task identity.
    s["commands"].append({"id": uid(), "action": action, "status": "pending", "issued_at": now,
                          "expires_at": now + 60, "object_id": s["protocol"]["target_id"],
                          "baseline_mask": s["protocol"]["baseline_mask"], "step": s["step"]})
    if s["protocol"].get("adapter") == "meta_hands":
        b = s["objects"].get(s["protocol"]["target_id"], {})
        s["commands"][-1].update(baseline_id=b.get("baseline_id", ""), run_id=b.get("run_id", ""), protocol_hash=s["protocol_hash"])
    if action == recovery_action(s):
        s["restore_command_id"] = s["commands"][-1]["id"]
    if action == "retest":
        s["retest_command_id"] = s["commands"][-1]["id"]


def manual_review(s: dict, reason: str, now: float) -> None:
    s["status"] = "manual_review"
    s["needs_diagnosis"] = False
    for c in s["commands"]:
        if c["status"] == "pending":
            c["status"] = "cancelled"
    if not any(seg["to_seq"] is None for seg in s["segments"]):
        failures = [e["seq"] for e in s["recent"] if e["kind"] == "grab_failed"]
        start = s["invalid_from_seq"] or (min(failures) if failures else max(1, s["last_seq"]))
        s["segments"].append({"from_seq": start, "to_seq": None, "label": "unresolved_review_required",
                              "exclude_from_primary_analysis": True})
    audit(s, "manual_review", {"reason": reason}, now)
    s["diagnosis_evidence_ids"] = []
    s["diagnosis_started_at"] = None


def active_diagnosis_evidence_ids(s: dict, now: float) -> set[str]:
    started = s.get("diagnosis_started_at")
    ids = s.get("diagnosis_evidence_ids") or []
    if not ids or not isinstance(started, (int, float)) or not 0 <= now - started <= DIAGNOSIS_EVIDENCE_TTL_SECONDS:
        return set()
    return set(ids)


def evidence(s: dict, now: float) -> list[dict]:
    pinned = active_diagnosis_evidence_ids(s, now)
    failures = s.get("failure_evidence") or [e for e in s["recent"] if e["kind"] == "grab_failed"]
    failures = [e for e in failures if (now - e["received_at"] <= 90 or e["event_id"] in pinned)
                and e["data"].get("object_id") == s["protocol"]["target_id"]]
    if s["protocol"].get("adapter") == "meta_hands":
        from .meta import meta_evidence
        return meta_evidence(s, failures, now, pinned)
    return failures


def eligible(s: dict, now: float, ids: list[str]) -> tuple[bool, str]:
    if s["step"] != 3 or s["status"] != "running":
        return False, "wrong_step_or_status"
    p = s["protocol"]
    if recovery_action(s) not in p["allowed_actions"] or s["recoveries"] >= p["max_recoveries"]:
        return False, "recovery_not_permitted"
    evs = evidence(s, now)
    if not ids or not set(ids).issubset({e["event_id"] for e in evs}):
        return False, "invalid_evidence_references"
    if p.get("adapter") == "meta_hands":
        from .meta import eligible_meta
        return eligible_meta(s, evs, ids, now, active_diagnosis_evidence_ids(s, now))
    qualifying = []
    for e in evs:
        d = e["data"]
        mask = d.get("observed_mask")
        hand = d.get("interactor_mask")
        if (isinstance(mask, int) and isinstance(hand, int) and mask != p["baseline_mask"]
                and (p["baseline_mask"] & hand) != 0 and (mask & hand) == 0
                and all(d.get(k) is True for k in ["tracked", "collider_enabled", "near_target", "input_received"])):
            qualifying.append(e["event_id"])
    if len(qualifying) < p["failure_threshold"] or not set(ids).intersection(qualifying):
        return False, "insufficient_technical_evidence"
    # A later successful grab or restored mask invalidates the stale diagnosis.
    last_bad_seq = max(e["seq"] for e in evs)
    for e in s["recent"]:
        if e["seq"] > last_bad_seq and e["data"].get("object_id") == p["target_id"]:
            if e["kind"] == "grab_success" or e["data"].get("observed_mask") == p["baseline_mask"]:
                return False, "evidence_superseded"
    return True, "verified_layer_mismatch_evidence"


def ingest(s: dict, event: dict, now: float) -> None:
    if event["seq"] != s["last_seq"] + 1:
        raise ValueError("sequence_gap: replay unacknowledged events in order")
    if len(json.dumps(event, ensure_ascii=False, allow_nan=False)) > 4000:
        raise ValueError("event_too_large")
    is_meta = s["protocol"].get("adapter") == "meta_hands"
    if is_meta:
        from .meta import observe_meta, matches_baseline, meta_completion, meta_retest
        observe_meta(s, event, now)
    event = copy.deepcopy(event)
    event["received_at"] = now
    event["server_step"] = s["step"]
    event["server_status"] = s["status"]
    s["last_seq"] = event["seq"]
    s["event_count"] += 1
    s["last_seen"] = now
    s["recent"] = (s["recent"] + [event])[-60:]
    s["revision"] += 1
    kind, d = event["kind"], event["data"]
    if kind == "grab_failed":
        s["failure_evidence"] = (s.get("failure_evidence", []) + [copy.deepcopy(event)])[-30:]
    if kind == "telemetry":
        s["telemetry"] = d
    if kind == "client_error" and is_meta and s["status"] != "completed":
        manual_review(s, "client_guard_stopped_study", now)
    if s["status"] == "completed":
        return
    if s["status"] == "manual_review":
        return
    if kind == "pause_request":
        manual_review(s, "participant_requested_pause", now)
        return
    if s["status"] in ["manual_review", "recovering"]:
        return
    if kind == "grab_failed" and s["step"] == 3 and d.get("object_id") == s["protocol"]["target_id"]:
        s["failures"] += 1
        if s["status"] == "retest" and s["failures"] >= s["protocol"]["failure_threshold"]:
            manual_review(s, "retest_failed", now)
        elif s["failures"] >= s["protocol"]["failure_threshold"]:
            verified = evidence(s, now)
            distinct = {e["data"].get("attempt_id") for e in verified}
            if len(distinct) >= s["protocol"]["failure_threshold"]:
                selected = verified[-s["protocol"]["failure_threshold"]:]
                s["diagnosis_evidence_ids"] = [e["event_id"] for e in selected]
                s["diagnosis_started_at"] = now
            s["needs_diagnosis"] = True
    if kind == "help_request" and s["step"] == 3 and s["status"] == "running":
        s["needs_diagnosis"] = True
    if s["status"] == "retest":
        verified = (kind == "grab_success" and event["seq"] > s["retest_after_seq"]
                    and d.get("object_id") == s["protocol"]["target_id"]
                    and (meta_retest(s, d) if is_meta else d.get("observed_mask") == s["protocol"]["baseline_mask"])
                    and d.get("tracked") is True and d.get("collider_enabled") is True)
        if not verified:
            return
        s["verification"] = {"result": "passed", "event_id": event["event_id"], "seq": event["seq"], "at": now,
                             "reason": "New same-target grab after acknowledged restoration and retest; baseline observed."}
        s["segments"].append({"from_seq": s["invalid_from_seq"], "to_seq": event["seq"],
                              "label": "technical_interruption_and_retest", "exclude_from_primary_analysis": True})
        s["status"] = "running"
        audit(s, "verification_passed", s["verification"], now)
    if s["status"] != "running" or s["step"] >= len(STEPS) or kind != STEPS[s["step"]]["event"]:
        return
    if kind in ["target_found", "grab_success", "placed"] and d.get("object_id") != s["protocol"]["target_id"]:
        return
    if is_meta and not meta_completion(s, kind, d):
        return
    if not is_meta and kind == "equipment_ready" and not (d.get("tracked") is True and d.get("observed_mask") == s["protocol"]["baseline_mask"]):
        return
    if kind == "survey_completed":
        difficulty = d.get("difficulty")
        if not isinstance(difficulty, int) or not 1 <= difficulty <= 7 or len(str(d.get("text", ""))) > 1000:
            raise ValueError("invalid_survey")
        s["survey"] = {"difficulty": difficulty, "text": d.get("text", ""), "event_id": event["event_id"]}
    s["step_history"].append({"id": STEPS[s["step"]]["id"], "started_at": s["step_started"], "ended_at": now,
                               "elapsed_seconds": round(now - s["step_started"], 3), "completion_event": event["event_id"]})
    s["step"] += 1
    s["step_started"] = now
    s["needs_diagnosis"] = False
    s["failures"] = 0
    if s["step"] == len(STEPS):
        s["status"] = "completed"
        audit(s, "session_completed", {}, now)


def apply_proposal(s: dict, proposal: dict, now: float) -> None:
    s["needs_diagnosis"] = False
    s["diagnosis"] = proposal
    action = proposal.get("action")
    ids = proposal.get("evidence_ids", [])
    ok, reason = eligible(s, now, ids)
    if action != recovery_action(s):
        ok, reason = False, "no_allowed_recovery_proposed"
    audit(s, "firewall", {"allowed": ok, "reason": reason, "proposal": proposal}, now)
    if not ok:
        manual_review(s, reason, now)
        return
    s["status"] = "recovering"
    s["recoveries"] += 1
    verified = evidence(s, now)
    s["invalid_from_seq"] = min(e["seq"] for e in verified)
    s["diagnosis_evidence_ids"] = []
    s["diagnosis_started_at"] = None
    command(s, "pause", now)


def expire_commands(s: dict, now: float) -> None:
    if any(c["status"] == "pending" and c["expires_at"] < now for c in s["commands"]):
        manual_review(s, "command_expired", now)
    if s["status"] == "retest" and now - s.get("retest_started", now) > 90:
        manual_review(s, "retest_timeout", now)


def acknowledge(s: dict, command_id: str, ack: Ack, now: float) -> None:
    expire_commands(s, now)
    c = next((c for c in s["commands"] if c["id"] == command_id), None)
    if not c:
        raise ValueError("unknown_command")
    if c["status"] == "acked":
        if c["ack"] != ack.model_dump():
            raise ValueError("conflicting_ack")
        return
    if c["status"] != "pending":
        raise ValueError("command_not_pending")
    c["status"] = "acked"
    c["ack"] = ack.model_dump()
    c["acked_at"] = now
    audit(s, "command_ack", {"command_id": c["id"], "action": c["action"], **ack.model_dump()}, now)
    if not ack.success:
        manual_review(s, "client_rejected_command", now)
    elif s["protocol"].get("adapter") == "meta_hands" and (ack.baseline_id != c["baseline_id"] or ack.run_id != c["run_id"] or ack.held):
        manual_review(s, "ack_identity_or_release_mismatch", now)
    elif c["action"] == "pause":
        command(s, recovery_action(s), now)
    elif c["action"] == recovery_action(s):
        baseline_ok = ack.baseline_match if s["protocol"].get("adapter") == "meta_hands" else ack.observed_mask == s["protocol"]["baseline_mask"]
        if not baseline_ok:
            manual_review(s, "baseline_not_observed", now)
        else:
            command(s, "retest", now)
    elif c["action"] == "retest":
        if s["protocol"].get("adapter") == "meta_hands" and not ack.baseline_match:
            manual_review(s, "baseline_changed_before_retest", now)
            return
        s["status"] = "retest"
        s["retest_after_seq"] = s["last_seq"]
        s["retest_started"] = now
        s["failures"] = 0


def report(s: dict) -> dict:
    return {"session_id": s["id"], "status": s["status"], "protocol_hash": s["protocol_hash"],
            "event_count": s["event_count"], "step_history": s["step_history"], "diagnosis": s["diagnosis"],
            "verification": s["verification"], "excluded_segments": s["segments"], "survey": s["survey"],
            "agent_report": s["report"], "protocol": s["protocol"], "consent": s.get("consent"),
            "interpretation": "Elapsed times include interruptions. Excluded segments are retained in raw logs. This is a descriptive demo report, not a validated research finding."}
