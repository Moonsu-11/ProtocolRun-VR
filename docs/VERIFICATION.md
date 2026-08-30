# Verification record · 0.4.0 RC2

Checked in the coding runtime on 2026-08-29:

- 47 Python tests pass using SQLite and explicit fixture model outputs, covering both XRI and Meta HTTP/state contracts.
- A real local Uvicorn/FastAPI process was started. Public health/English console and authenticated runtime/protocol endpoints passed the read-only smoke check. The reported store was SQLite and `cloud_run` was false.
- The Meta v205 `IHand` calls used by the client (`GetIndexFingerIsPinching`, `GetJointPose(HandIndexTip)`, `GetRootPose`) were checked against Meta's current v205 reference. Four original ProtocolRun script GUIDs remain identical in the standalone SDK and merged Unity project. This does not establish Unity compilation or SDK execution.
- The supplied Unity project structure is included with Unity 6000.3.16f1, Meta XR All-in-One 205.0.0 and OpenXR 1.16.1. A/B retain their existing Meta HandGrab + Grab setup; C retains no grab interactable. The ProtocolRun scene metadata assigns A/B/C roles without adding grab components.
- Five dashboard tests, the production build, dashboard-scoped TypeScript and standalone console JavaScript syntax checks passed.
- The deployment script passes Bash syntax validation. This is not an IAM/resource deployment test.

Final dashboard production build and rendered-output checks are performed during release packaging; see the delivered release note for their outcome.

Not verified: Unity Editor compilation, Meta lifecycle/hand input behavior, HUD ergonomics, actual Quest playback, real Gemini requests, Google IAM/billing/model quotas, Firestore transactions against Google, Docker execution, Cloud Run deployment and end-to-end recovery on a headset. The environment has no Google credentials, gcloud, Docker or Unity executable. These remain release blockers, not completed work.

No test-generated telemetry or agent fixtures are embedded in the production console.
