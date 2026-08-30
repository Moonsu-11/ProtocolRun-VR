# Verification record · 0.5.0 RC6

Checked in the coding runtime on 2026-08-30:

- 53 Python tests pass using SQLite and explicit fixture model outputs, covering both XRI and Meta HTTP/state contracts, compact model input, bounded retention of three exact server-verified failures across a 130-second retry delay, expiry after the 240-second diagnosis lifecycle, a continuously paused participant state between agent retries, and ADK's final-response behavior for `skip_summarization` Tool responses.
- A real local Uvicorn/FastAPI process was started. Public health/English console and authenticated runtime/protocol endpoints passed the read-only smoke check. The reported store was SQLite and `cloud_run` was false.
- The Meta v205 `IHand` calls used by the client (`GetIndexFingerIsPinching`, `GetJointPose(HandIndexTip)`, `GetRootPose`) were checked against Meta's current v205 reference. Four original ProtocolRun script GUIDs remain identical in the standalone SDK and merged Unity project. This does not establish Unity compilation or SDK execution.
- The supplied Unity project structure is included with Unity 6000.3.16f1, Meta XR All-in-One 205.0.0 and OpenXR 1.16.1. A/B retain their existing Meta HandGrab + Grab setup; C retains no grab interactable. The ProtocolRun scene metadata assigns A/B/C roles without adding grab components.
- Five dashboard tests, the production build, dashboard-scoped TypeScript and standalone console JavaScript syntax checks passed.
- The deployment script passes Bash syntax validation. This is not an IAM/resource deployment test.
- The user ran the RC5 `VERIFY_GEMINI_TOOL_WINDOWS.bat` probe with private credentials and obtained one clean real Google ADK `propose_recovery` call in 7.76 seconds, with no GeneratorExit/OpenTelemetry cleanup error.
- In the following real Quest run, the exported immutable audit showed valid CUBE_B `grab_failed` events at sequences 88, 93 and 97. The first two production-shaped diagnosis calls timed out at exactly 60 seconds. The third call began roughly 129 seconds after the last failure, after the old 90-second evidence window had expired, and therefore failed closed to manual review. RC6 removes noisy telemetry/pose/text fields from the Gemini prompt and pins those exact verified IDs for a bounded 240-second diagnosis lifecycle; it does not increase the 60-second model-call limit.

Final dashboard production build and rendered-output checks are performed during release packaging; see the delivered release note for their outcome.

Not verified: Unity Editor compilation for RC6, Meta lifecycle/hand input behavior after RC6, HUD ergonomics, a live RC6 production-shaped Gemini probe, actual Quest playback through RC6 recovery, Google IAM/billing/model quotas, Firestore transactions against Google, Docker execution, Cloud Run backend deployment and end-to-end recovery on a headset. This coding environment has no Google credentials, gcloud, Docker or Unity executable. These remain release blockers, not completed work.

No test-generated telemetry or agent fixtures are embedded in the production console.
