# Submission and acceptance

## Deliverables

Submit the Devpost project with the source repository URL, reproducible README, architecture diagram and a public YouTube/Vimeo demonstration no longer than 4 minutes. Demonstrate the backend actually running on Google Cloud. Provide a working hosted project/test build and clear access/testing instructions where available. The English bundled console and VR instructions support an English demo.

If the code repository is private, the official rules name `testing@devpost.com` and `cloudhackathons@google.com` as required invitees. Re-check the submission form/rules before submitting. Do not publish participant data or permanent researcher credentials; provide judging access privately with a dedicated deployment/token.

Official rules, checked 2026-08-28: https://allthingsagentichackathon.devpost.com/rules
Required stack: Gemini 3.5+ via Gemini API/Vertex AI, a Google agent framework and at least one Google Cloud infrastructure service. This implementation uses Google ADK, Gemini 3.5 Flash, Cloud Run, Firestore and Secret Manager. Source code is not evidence that deployment/model integration has succeeded.

Deadline: August 31, 2026 17:00 PDT = September 1, 2026 09:00 KST. Aim to submit August 31 in Korea; retain time for upload and access checks.

## Repository

Use the full package as one repository. `backend`, `unity-meta`, `unity-project`, `app`, `deploy`, `docs` and CI belong together. The Unity project contains the user's original Assets/Packages/ProjectSettings with updated SDK files; run its installer, save and commit the resulting scene after testing. Do not add Library, Temp, Logs, generated builds, .env files, connection.json, service-account files or runtime event journals. Meta dependencies are package references, not copied vendor SDK code. Review third-party asset/dependency licenses before making the repository public. No blanket license is imposed on the user's project by this package.

No user GitHub repository has been created or pushed by this release. Create a repository with the visibility you choose, then use its exact URL. Check `git status` and staged files for secrets before the first push. The CI workflow runs server tests, a container build and dashboard checks; it does not run Unity or billable Gemini calls.

## Four-minute recording outline

- 0:00–0:25: problem, three cube roles, explicit deliberately injected demo fault.
- 0:25–0:55: Cloud Run running backend and Firestore; avoid secrets. Show scene/protocol and participant consent.
- 0:55–1:35: normal A/B hand grabs, release, inject B fault, multiple real failed pinch attempts and participant help.
- 1:35–2:40: Gemini evidence tool/proposal, deterministic firewall, pause/restore/retest ACKs. Show actual B component restoration and successful new B grab.
- 2:40–3:25: placement, survey, report/raw export. Confirm C remains non-grabbable and verification was not inferred from a setting change.
- 3:25–4:00: architecture, failure boundaries, source/run instructions and what was verified.

Do not splice different sessions into an apparent single successful live cycle. Label any prerecorded views or test fixtures. Actual model and Unity integration remain unverified until the hardware acceptance cycle is recorded.

## Acceptance record to fill in after real tests

| Check | Result / evidence |
|---|---|
| Unity 6000.3.16f1 compilation | Pending |
| Quest 3 Link hand tracking and HUD reachability | Pending |
| A and B normal grabs; C protected | Pending |
| B fault blocks all captured direct grab paths | Pending |
| HTTPS Unity → Cloud Run event ingestion | Pending |
| Firestore transactions and server restart persistence | Pending |
| Actual Gemini ADK evidence/proposal calls | Pending |
| Remote baseline restoration; fresh B regrab verified | Pending |
| Disconnect/reconnect and lost ACK recovery on hardware | Pending |
| Placement, survey, English report and JSON/CSV | Pending |
| Docker image execution and Cloud Run deploy script | Pending |
| Repo permissions, judging instructions, video and architecture | Pending |
