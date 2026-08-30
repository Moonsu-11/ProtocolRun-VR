from __future__ import annotations

import asyncio
import csv
import hashlib
import io
import json
import logging
import os
import secrets
import time
from pathlib import Path
from contextlib import asynccontextmanager

from fastapi import Depends, FastAPI, Header, HTTPException, Request, Response
from fastapi.middleware.cors import CORSMiddleware
from fastapi.responses import RedirectResponse
from fastapi.staticfiles import StaticFiles
from pydantic import Field

from .agents import ADKAgents
from .domain import (Ack, EventBatch, Protocol, StrictModel, acknowledge, apply_proposal,
                     audit, eligible, evidence, expire_commands, ingest, manual_review,
                     new_session, public_session, recovery_action, report, uid, meta_protocol)
from .store import FirestoreStore, SQLiteStore

log = logging.getLogger("protocolrun")


class SessionCreate(StrictModel):
    protocol_id: str = Field(default="object-study-v1", pattern=r"^[a-zA-Z0-9_-]{1,64}$")


def create_app(store=None, agents=None, admin_token=None):
    admin_token = admin_token or os.environ.get("PRVR_ADMIN_TOKEN", "")
    if len(admin_token) < 32 or admin_token.startswith(("replace-with", "your-")):
        raise RuntimeError("Set PRVR_ADMIN_TOKEN to a random secret of at least 32 characters; never put it in Unity.")
    if store is None:
        if os.environ.get("PRVR_STORE", "sqlite") == "firestore":
            store = FirestoreStore(os.environ["GOOGLE_CLOUD_PROJECT"], os.environ.get("FIRESTORE_DATABASE", "(default)"))
        else:
            if os.environ.get("K_SERVICE"):
                raise RuntimeError("Cloud Run requires PRVR_STORE=firestore; local SQLite is not durable there.")
            store = SQLiteStore(os.environ.get("PRVR_SQLITE_PATH", "protocolrun.sqlite3"))
    agents = agents or ADKAgents()
    app = FastAPI(title="ProtocolRun-VR", version="0.5.0-rc6", docs_url=None, redoc_url=None, openapi_url=None)
    app.add_middleware(CORSMiddleware, allow_origins=[x.strip() for x in os.environ.get("PRVR_CORS_ORIGINS", "http://localhost:5173").split(",") if x.strip()],
                       allow_methods=["GET", "POST"], allow_headers=["Authorization", "Content-Type"])

    @app.middleware("http")
    async def limits(request: Request, call_next):
        # Bounded body read also handles missing or dishonest Content-Length.
        if request.method == "POST":
            body = bytearray()
            async for chunk in request.stream():
                body.extend(chunk)
                if len(body) > 150000:
                    return Response("request_too_large", status_code=413)
            request._body = bytes(body)
        response = await call_next(request)
        response.headers["Cache-Control"] = "no-store"
        response.headers["X-Content-Type-Options"] = "nosniff"
        response.headers["Referrer-Policy"] = "no-referrer"
        response.headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'none'; frame-ancestors 'self'"
        return response

    def bearer(value):
        if not value or not value.startswith("Bearer "):
            raise HTTPException(401, "Bearer token required")
        return value[7:]

    def admin(authorization: str | None = Header(default=None)):
        if not secrets.compare_digest(bearer(authorization), admin_token):
            raise HTTPException(401, "Invalid researcher token")

    def session_auth(sid: str, authorization: str | None = Header(default=None)):
        token = bearer(authorization)
        if not sid.isalnum() or len(sid) > 64:
            raise HTTPException(404, "Session not found")
        s = store.get("sessions", sid)
        if not s or not secrets.compare_digest(s["token_hash"], hashlib.sha256(token.encode()).hexdigest()):
            raise HTTPException(401, "Invalid session credentials")
        return s

    def session_exists(sid):
        if not sid.isalnum() or len(sid) > 64:
            raise HTTPException(404, "Session not found")
        s = store.get("sessions", sid)
        if not s:
            raise HTTPException(404, "Session not found")
        return s

    def update(sid, fn, events=None):
        try:
            return store.update(sid, fn, events)
        except ValueError as e:
            raise HTTPException(409, str(e)) from e

    @app.get("/healthz")
    def health():
        return {"status": "ok", "service": "ProtocolRun-VR", "version": app.version, "agent_mode": agents.mode}

    @app.get("/api/runtime", dependencies=[Depends(admin)])
    def runtime():
        return {"version": app.version, "store": type(store).__name__, "agent_mode": agents.mode,
                "model": os.environ.get("GEMINI_MODEL", "gemini-3.5-flash"), "cloud_run": bool(os.environ.get("K_SERVICE")),
                "note": "Configuration only; live model access requires a successful diagnosis."}

    @app.get("/api/openapi.json", dependencies=[Depends(admin)])
    def openapi():
        return app.openapi()

    @app.get("/api/protocols", dependencies=[Depends(admin)])
    def protocols():
        items = store.list("protocols")
        if not any(p["id"] == "object-study-v1" for p in items):
            items.insert(0, Protocol().model_dump())
        if not any(p["id"] == "meta-hands-v1" for p in items):
            items.insert(0, meta_protocol().model_dump())
        return {"protocols": items}

    @app.post("/api/protocols", dependencies=[Depends(admin)], status_code=201)
    def create_protocol(p: Protocol):
        if p.id in ["object-study-v1", "meta-hands-v1"] or store.get("protocols", p.id):
            raise HTTPException(409, "Use a new protocol ID; existing protocols are immutable")
        store.create("protocols", p.id, p.model_dump())
        return p

    @app.get("/api/sessions", dependencies=[Depends(admin)])
    def sessions():
        return {"sessions": sorted([public_session(s, True) for s in store.list("sessions")], key=lambda s: s["created_at"], reverse=True)}

    @app.post("/api/sessions", dependencies=[Depends(admin)], status_code=201)
    def create_session(body: SessionCreate):
        p = Protocol() if body.protocol_id == "object-study-v1" else meta_protocol() if body.protocol_id == "meta-hands-v1" else store.get("protocols", body.protocol_id)
        if not p:
            raise HTTPException(404, "Unknown protocol")
        if isinstance(p, dict):
            p = Protocol.model_validate(p)
        token, sid = secrets.token_urlsafe(32), uid()
        s = new_session(p, token, sid)
        store.create("sessions", sid, s)
        return {"session": public_session(s, True), "session_token": token}

    @app.get("/api/sessions/{sid}", dependencies=[Depends(admin)])
    def get_session(sid: str):
        return public_session(session_exists(sid), True)

    @app.get("/api/sessions/{sid}/client")
    def client_session(sid: str, s=Depends(session_auth)):
        return public_session(s)

    @app.post("/api/sessions/{sid}/events")
    def events(sid: str, batch: EventBatch, s=Depends(session_auth)):
        if s["event_count"] + len(batch.events) > 30000:
            raise HTTPException(429, "Demo session event limit reached")
        now = time.time()
        def accept(state, fresh):
            for e in fresh:
                ingest(state, e, now)
        updated = update(sid, accept, [e.model_dump() for e in batch.events])
        return {"accepted_ids": [e.event_id for e in batch.events], "last_seq": updated["last_seq"], "session": public_session(updated)}

    @app.get("/api/sessions/{sid}/commands")
    def commands(sid: str, s=Depends(session_auth)):
        state = update(sid, lambda state, _: expire_commands(state, time.time()))
        return {"commands": [c for c in state["commands"] if c["status"] == "pending"], "session": public_session(state)}

    @app.post("/api/sessions/{sid}/commands/{cid}/ack")
    def ack(sid: str, cid: str, body: Ack, s=Depends(session_auth)):
        state = update(sid, lambda state, _: acknowledge(state, cid, body, time.time()))
        return {"session": public_session(state)}

    @app.post("/api/sessions/{sid}/tick")
    async def tick(sid: str, s=Depends(session_auth)):
        now, lease_id = time.time(), uid()
        def claim(state, _):
            expire_commands(state, now)
            job = "diagnosis" if state["needs_diagnosis"] and state["status"] == "running" else (
                "analysis" if state["status"] == "completed" and state["report"] is None else None)
            if not job or (state["lease"] and state["lease"]["expires_at"] > now):
                return
            if now - state.get("last_agent_at", 0) < 15 or state.get("agent_attempts", {}).get(job, 0) >= 3:
                return
            state["lease"] = {"id": lease_id, "job": job, "expires_at": now + 180}
            state["last_agent_at"] = now
            state.setdefault("agent_attempts", {})[job] = state.get("agent_attempts", {}).get(job, 0) + 1
            audit(state, "agent_started", {"job": job, "mode": agents.mode}, now)
        claimed = update(sid, claim)
        if not claimed["lease"] or claimed["lease"]["id"] != lease_id:
            return {"status": "idle_or_busy"}
        job = claimed["lease"]["job"]
        try:
            if job == "diagnosis":
                failure_events = evidence(claimed, time.time())
                failure_ids = [e["event_id"] for e in failure_events]
                candidate_ok, candidate_reason = eligible(claimed, time.time(), failure_ids) if failure_ids else (False, "no_qualifying_failure_events")
                recent_context = [e for e in claimed["recent"]
                                  if e["kind"] in ["grab_success", "help_request", "telemetry"]][-12:]
                snapshot = {"protocol": claimed["protocol"], "step": claimed["step"],
                            "registered_baselines": claimed.get("objects", {}), "normal_practice_release_objects": claimed.get("practice_released", []),
                            "recovery_candidate": {"server_verified": candidate_ok,
                                                   "reason": candidate_reason,
                                                   "required_action": recovery_action(claimed),
                                                   "failure_threshold": claimed["protocol"]["failure_threshold"],
                                                   "distinct_failed_attempt_count": len({e["data"].get("attempt_id") for e in failure_events}),
                                                   "qualifying_failure_event_ids": failure_ids},
                            "qualifying_failure_events": failure_events,
                            "recent_events": failure_events + recent_context}
                # Diagnosis is one forced Gemini tool decision. A timeout fails
                # closed and retries; it never silently restores Unity state.
                result = await asyncio.wait_for(agents.diagnose(snapshot), timeout=60)
            else:
                result = await asyncio.wait_for(agents.analyze(report(claimed)), timeout=120)
            def finish(state, _):
                if not state["lease"] or state["lease"]["id"] != lease_id:
                    return
                state["lease"] = None
                if job == "diagnosis":
                    if state["status"] == "running" and state["step"] == claimed["step"]:
                        apply_proposal(state, result, time.time())
                    else:
                        audit(state, "stale_diagnosis_discarded", {}, time.time())
                else:
                    state["report"] = result
                    audit(state, "report_generated", {"mode": agents.mode}, time.time())
            update(sid, finish)
            log.info(json.dumps({"session": sid, "job": job, "result": "completed"}))
            return {"status": "processed"}
        except Exception as exc:
            def fail(state, _):
                if not state["lease"] or state["lease"]["id"] != lease_id:
                    return
                state["lease"] = None
                audit(state, "agent_error", {"job": job, "error_type": type(exc).__name__, "mode": agents.mode}, time.time())
                if job == "diagnosis" and state["agent_attempts"][job] >= 3:
                    manual_review(state, "agent_unavailable", time.time())
            update(sid, fail)
            log.warning(json.dumps({"session": sid, "job": job, "error_type": type(exc).__name__}))
            raise HTTPException(503, "Agent unavailable; no recovery was executed. Check server logs, quota and model access.") from exc

    @app.get("/api/sessions/{sid}/report", dependencies=[Depends(admin)])
    def get_report(sid: str):
        return report(session_exists(sid))

    @app.get("/api/sessions/{sid}/export/{format}", dependencies=[Depends(admin)])
    def export(sid: str, format: str):
        state = session_exists(sid)
        records = store.events(sid)
        if format == "json":
            body = json.dumps({"report": report(state), "events": records, "audit": store.audits(sid)}, ensure_ascii=False, indent=2)
            media = "application/json"
        elif format == "csv":
            buf = io.StringIO()
            writer = csv.writer(buf)
            writer.writerow(["seq", "event_id", "occurred_at", "received_at", "kind", "step", "status", "excluded", "data_json"])
            for e in records:
                excluded = any(seg["from_seq"] <= e["seq"] and (seg["to_seq"] is None or e["seq"] <= seg["to_seq"]) for seg in state["segments"])
                # Prefix formula-like values before spreadsheets interpret them.
                def safe(x):
                    x = str(x)
                    return "'" + x if x.lstrip().startswith(("=", "+", "-", "@")) else x
                writer.writerow([safe(x) for x in [e["seq"], e["event_id"], e["occurred_at"], e["received_at"], e["kind"], e["server_step"], e["server_status"], excluded, json.dumps(e["data"], ensure_ascii=False)]])
            body, media = "\ufeff" + buf.getvalue(), "text/csv"
        else:
            raise HTTPException(404, "Use json or csv")
        return Response(body, media_type=media, headers={"Content-Disposition": f'attachment; filename="protocolrun-{sid}.{format}"'})

    @app.get("/", include_in_schema=False)
    def home():
        return RedirectResponse("/console/")

    app.mount("/console", StaticFiles(directory=Path(__file__).resolve().parent.parent / "static", html=True), name="console")
    return app
