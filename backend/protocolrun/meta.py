"""Deterministic policy for Meta hand tracking; no model-authored scene mutations."""
from datetime import datetime
import math
import re


def identity_matches(s, d):
    b = s["objects"].get(d.get("object_id"), {})
    return bool(b) and all(d.get(k) == b.get(k) for k in ("baseline_id", "run_id", "hand_grab_count", "companion_grab_count"))


def matches_baseline(s, d):
    b = s["objects"].get(d.get("object_id"), {})
    return (identity_matches(s, d) and d.get("baseline_match") is True
            and d.get("enabled_hand_grab_count") == b.get("enabled_hand_grab_count")
            and d.get("enabled_companion_grab_count") == b.get("enabled_companion_grab_count")
            and d.get("collider_enabled") is True)


def observe_meta(s, e, now):
    d, kind, p = e["data"], e["kind"], s["protocol"]
    if kind == "consent":
        if s["consent"] is not None or d.get("accepted") is not True or d.get("version") != "demo-consent-v1":
            raise ValueError("invalid_or_repeated_consent")
        s["consent"] = {"event_id": e["event_id"], "at": now, "version": "demo-consent-v1"}
        return
    if s["consent"] is None:
        raise ValueError("consent_required_before_telemetry")
    if kind == "object_registered":
        oid = d.get("object_id")
        if oid not in {p["target_id"], p["practice_id"], p["protected_id"]} or s["step"] != 0:
            raise ValueError("unexpected_object_registration")
        if oid in s["objects"]:
            raise ValueError("baseline_is_immutable_create_new_session")
        if not all(isinstance(d.get(k), str) and re.fullmatch(r"[a-f0-9]{32}", d[k]) for k in ["run_id", "baseline_id"]):
            raise ValueError("invalid_baseline_identity")
        fields = ["hand_grab_count", "companion_grab_count", "enabled_hand_grab_count", "enabled_companion_grab_count"]
        if not all(type(d.get(k)) is int and 0 <= d[k] <= 16 for k in fields):
            raise ValueError("invalid_component_counts")
        protected = oid == p["protected_id"]
        if d.get("expected_grabbable") is not (not protected) or d.get("restore_allowed") is not (oid == p["target_id"]):
            raise ValueError("object_role_disagrees_with_protocol")
        if d.get("collider_enabled") is not True or d.get("held") is not False or d.get("baseline_match") is not True:
            raise ValueError("registration_requires_released_healthy_baseline")
        if protected:
            if any(d[k] != 0 for k in fields):
                raise ValueError("protected_object_has_grab_components")
        elif not (d["hand_grab_count"] > 0 and d["enabled_hand_grab_count"] == d["hand_grab_count"]
                  and d["enabled_companion_grab_count"] <= d["companion_grab_count"]):
            raise ValueError("invalid_healthy_grab_baseline")
        s["objects"][oid] = {k: d[k] for k in fields + ["run_id", "baseline_id", "expected_grabbable", "restore_allowed"]}
        s["objects"][oid]["registration_event_id"] = e["event_id"]
    if kind == "fault_injected":
        if not p["demo_faults_allowed"] or d.get("object_id") != p["target_id"]:
            raise ValueError("demo_fault_not_permitted")
        if (not identity_matches(s, d) or d.get("baseline_match") is not False
                or d.get("collider_enabled") is not True or d.get("held") is not False
                or d.get("enabled_hand_grab_count") != 0 or d.get("enabled_companion_grab_count") != 0):
            raise ValueError("fault_event_does_not_match_registered_target")
    if kind == "grab_attempt":
        aid = d.get("attempt_id")
        if not isinstance(aid, str) or not re.fullmatch(r"[a-f0-9]{32}", aid) or aid in s["meta_attempts"]:
            raise ValueError("invalid_or_repeated_attempt")
        s["meta_attempts"][aid] = {"seq": e["seq"], "event_id": e["event_id"], "data": d, "failed": False}
        s["meta_attempts"] = dict(list(s["meta_attempts"].items())[-30:])
    if kind == "grab_failed":
        a = s["meta_attempts"].get(d.get("attempt_id"))
        if not a or a["failed"] or a["seq"] >= e["seq"] or any(a["data"].get(k) != d.get(k) for k in ["object_id", "run_id", "baseline_id", "hand"]):
            raise ValueError("failure_requires_distinct_matching_attempt")
        a["failed"] = True
    if s["step"] == 1 and s["status"] == "running" and d.get("object_id") == p["practice_id"]:
        oid = d["object_id"]
        if kind == "grab_success" and d.get("source") == "sdk_selection" and d.get("tracked") is True and matches_baseline(s, d):
            if oid not in s["practice_grabbed"]:
                s["practice_grabbed"].append(oid)
        if kind == "released" and oid in s["practice_grabbed"] and matches_baseline(s, d) and d.get("held") is False:
            if oid not in s["practice_released"]:
                s["practice_released"].append(oid)


def technical_failure(s, d):
    distance = d.get("distance_m")
    return (identity_matches(s, d) and d.get("object_id") == s["protocol"]["target_id"]
            and all(d.get(k) is True for k in ["tracked", "collider_enabled", "near_target", "input_received", "expected_grabbable", "restore_allowed"])
            and d.get("held") is False and d.get("baseline_match") is False
            and d.get("enabled_hand_grab_count") == 0 and d.get("enabled_companion_grab_count") == 0
            and type(distance) in (float, int) and math.isfinite(distance) and 0 <= distance <= s["protocol"]["near_distance_m"])


def meta_evidence(s, evs, now, pinned_ids=None):
    """Return only server-verified, paired Meta attempt/failure evidence.

    The records come from the bounded failure buffer rather than the rolling telemetry
    window, so a real Gemini call cannot lose the three failures while it is running.
    """
    qualifying = []
    pinned_ids = pinned_ids or set()
    for e in evs:
        a = s["meta_attempts"].get(e["data"].get("attempt_id"))
        try:
            age = now - datetime.fromisoformat(e["occurred_at"].replace("Z", "+00:00")).timestamp()
        except (ValueError, TypeError):
            continue
        if (-5 <= age <= 90 or e["event_id"] in pinned_ids) and a and a.get("failed") and technical_failure(s, a["data"]) and technical_failure(s, e["data"]):
            qualifying.append(e)
    return qualifying


def eligible_meta(s, evs, ids, now, pinned_ids=None):
    target = s["protocol"]["target_id"]
    baseline = s["objects"].get(target)
    if (not baseline or baseline.get("hand_grab_count", 0) <= 0
            or baseline.get("enabled_hand_grab_count") != baseline.get("hand_grab_count")):
        return False, "no_registered_healthy_baseline"
    qualifying = meta_evidence(s, evs, now, pinned_ids)
    if len({e["data"]["attempt_id"] for e in qualifying}) < s["protocol"]["failure_threshold"] or not set(ids).issubset({e["event_id"] for e in qualifying}):
        return False, "insufficient_disabled_hand_grab_evidence"
    last_bad = max(e["seq"] for e in qualifying)
    for e in s["recent"]:
        if e["seq"] > last_bad and e["data"].get("object_id") == target:
            if e["kind"] == "grab_success" or matches_baseline(s, e["data"]) or not identity_matches(s, e["data"]):
                return False, "evidence_superseded"
            if e["kind"] == "telemetry" and e["data"].get("tracked") is not True:
                return False, "tracking_unavailable_after_failures"
    return True, "verified_disabled_hand_grab_paths"


def meta_retest(s, d):
    return (matches_baseline(s, d) and d.get("source") == "sdk_selection"
            and d.get("restore_command_id") == s["restore_command_id"]
            and d.get("retest_command_id") == s["retest_command_id"]
            and bool(s["retest_command_id"]))


def meta_completion(s, kind, d):
    p = s["protocol"]
    if kind == "equipment_ready":
        return (s["consent"] is not None and len(s["objects"]) == 3
                and d.get("left_tracked") is True and d.get("right_tracked") is True)
    if kind == "practice_completed":
        return p["practice_id"] in s["practice_released"]
    if kind == "target_found":
        return identity_matches(s, d) and d.get("tracked") is True and d.get("near_target") is True
    if kind == "grab_success":
        return matches_baseline(s, d) and d.get("source") == "sdk_selection" and d.get("tracked") is True
    if kind == "placed":
        return (matches_baseline(s, d) and d.get("held") is False
                and d.get("inside_drop_zone") is True and d.get("source") == "drop_zone"
                and type(d.get("settled_seconds")) in (int, float) and 0.5 <= d["settled_seconds"] <= 10)
    return True
