# API contract · 0.5.0 RC6

Python FastAPI is the standalone backend. `/console/` serves its English console. Static UI and `/healthz` are public; all study data require application bearer tokens. `/api/openapi.json` requires the researcher token.

## Authentication

Researcher token: protocol creation, session creation/list/detail, exports, runtime configuration. Session token: one Unity session, its events, commands, ACKs and agent tick. Never put a researcher/Google token in Unity. All non-loopback clients require HTTPS. Tokens are not URL parameters. Browser tokens are memory-only; Unity connection JSON is outside Assets.

## Endpoints

| Method | Path | Token |
|---|---|---|
| GET | `/healthz`, `/console/` | None; no study data |
| GET | `/api/runtime`, `/api/openapi.json` | Researcher |
| GET/POST | `/api/protocols`, `/api/sessions` | Researcher |
| GET | `/api/sessions/{sid}` | Researcher |
| GET | `/api/sessions/{sid}/client` | Session |
| POST | `/api/sessions/{sid}/events` | Session |
| GET | `/api/sessions/{sid}/commands` | Session |
| POST | `/api/sessions/{sid}/commands/{cid}/ack` | Session |
| POST | `/api/sessions/{sid}/tick` | Session |
| GET | `/api/sessions/{sid}/report`, `/api/sessions/{sid}/export/{json|csv}` | Researcher |

The immutable `meta-hands-v1` protocol defines A practice, B target, C protected, 3 failed attempts, and `restore_hand_grab_baseline`. `object-study-v1` is the legacy XRI path. Do not mix adapters.

## Event envelope

`{"events":[{"event_id":"unique_hex_id","seq":1,"occurred_at":"ISO8601 UTC","kind":"consent","data":{"accepted":true,"version":"demo-consent-v1"}}]}`

- Global sequence starts at 1 per session, not per object. Max batch 25, max event 4000 JSON characters, max request 150 KB, demo session cap 30,000 events.
- Event-ID replay is accepted only for the identical original payload. Gaps/conflicts return 409; a batch commits atomically.
- A consent event must precede Meta telemetry. Register all 3 objects while healthy/released. Baseline `run_id` and `baseline_id` are immutable for this session.
- Each registration records expected-grabbable role, restore permission, counts and enabled counts of direct HandGrab/Grab components, collider availability and released state. Local SDK additionally retains exact component references and original enabled arrays.
- Pinch attempts have a unique `attempt_id`, side, index-fingertip-to-collider distance, tracking/input/near flags and current component snapshot. The tracked wrist is used only when the tip joint is temporarily unavailable. A failure must link a distinct previous matching attempt. A selection within the 0.5-second observation cancels the candidate failure.
- A-only practice uses actual `sdk_selection` grab and release observations. B's healthy component baseline is registered before its automatic startup fault; no prior participant grab of B is required. C input is `non_grabbable_attempt`, not a fault.
- Telemetry is sampled at 1 Hz; per-event observation occurs on SDK callbacks. FPS is a single-frame sample. This is not high-frequency motion capture or an input-latency benchmark.

## Recovery

1. Gemini ADK diagnosis receives a compact whitelist of server-verified decision fields and the registered target baseline. Raw telemetry, poses, rotations and participant text are excluded from the model prompt. Fault injection events are withheld; a demo flag is not technical proof.
2. Recovery agent proposes one bounded action with concrete failed-event IDs. Free text is never executed.
3. Firewall requires target-step/running status, permission, an immutable healthy B component baseline registered before the fault, 3 distinct tracked near pinch failures, active collider and disabled captured direct grab paths. Old/future-dated or superseded evidence is rejected.
4. Server commands carry target, protocol hash, baseline/run identities, expiry and command ID. Those values come from the frozen session, not model arguments.
5. `pause` ACK → `restore_hand_grab_baseline` ACK with observed baseline → `retest` ACK. SDK holds a saved ACK ledger and resends lost acknowledgements without repeating actuation. Commands expire after 60 s; retest window is 90 s.
6. Verification requires a *new* actual HandGrab selection of B with matching baseline and both restore/retest command IDs after retest ACK. Companion/controller selection, wrong target, old events and restore-only ACK cannot pass.
7. Failed/unknown/expired paths enter manual review. No automatic task easing, target changes, raw-log edits or C restoration.

Unity app restarts are deliberately not resumed: create a new session to prevent stale physical baselines and commands from crossing Play runs. Disconnect/reconnect within one run preserves ordered pending events; original local JSONL and server raw events remain available. SDK is a trusted authenticated measurement client, not remotely attested hardware.

## Persistence and execution

SQLite locally; Firestore transactions on Cloud Run. State snapshots retain recent events/audits while append-only collections retain the full record. The API provides no deletion/edit endpoint for raw events; a project administrator can still change the database. This is not cryptographically tamper-proof storage.

Unity periodically calls `/tick`; agent work runs on the server while the researcher dashboard is closed. Keep Unity running until the final report is returned. This release does not have an independent Cloud Tasks/Pub/Sub worker. Its last-seen indication distinguishes an offline headset from a running session.

The server gives each forced ADK diagnosis Tool decision up to 60 seconds and the post-session report up to 120 seconds. It does not extend a slow model call. Once the third failure passes every deterministic check, the exact three event IDs are pinned for that diagnosis lifecycle for at most 240 seconds so safe retries cannot lose already-verified evidence to the general 90-second freshness window. Every retry still rechecks baseline identity, attempt pairing, technical predicates and supersession. The internal lease remains bounded at 180 seconds and Unity allows 150 seconds for the corresponding `/tick` request. From the moment a diagnosis is pending through any safe retry, the participant client receives only an `agent_busy` flag; diagnosis contents remain researcher-only and additional task input stays paused.

The server pauses protocol progression and Unity participant controls; it does not freeze the HMD or disable hand tracking. Elapsed step times include interruptions and network delay. Excluded intervals are marked, not deleted. Reports describe a single demonstration and are not research-validity statistics.
