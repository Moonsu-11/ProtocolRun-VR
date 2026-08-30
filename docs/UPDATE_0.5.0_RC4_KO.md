# ProtocolRun-VR 0.5.0 RC4 업데이트

RC4는 실제 Google ADK 진단이 연속 두 번 정확히 120초에서 타임아웃된 Quest 로그를 반영한 업데이트다.

## 원인

기존 진단은 `Diagnosis Agent → Recovery Agent → 최종 응답`의 연속 모델 호출을 한 요청 안에서 수행했다. 단순 Gemini API 호출과 Tool 사용은 정상이어도 이 전체 흐름이 120초 안에 끝나지 않으면 복구 제안과 Unity 명령이 생성되지 않았다. 호출 사이에 `agent_busy`가 잠시 해제되어 참가자에게 B 잡기 안내가 다시 보이는 문제도 있었다.

## 변경 사항

- 서버가 선별한 증거 JSON을 하나의 ADK `diagnosis_recovery` Agent에 직접 제공한다.
- Gemini는 강제 Function Calling으로 `propose_recovery`를 정확히 한 번 호출한다.
- 실제 Tool 실행 즉시 요청을 종료하여 불필요한 최종 Gemini 응답을 기다리지 않는다.
- Tool 제안은 이전과 동일하게 결정론적 Protocol Firewall이 다시 검사한다.
- 진단 호출 제한을 60초로 줄이고 실패 시 안전하게 재시도한다.
- 재시도 사이에도 `agent_busy`를 유지하여 B 잡기 안내가 잘못 다시 나타나지 않게 한다.

## 재시험

1. 현재 타임아웃 세션이 `manual_review`로 끝나면 Unity와 BAT을 종료한다.
2. RC4 ZIP을 저장소 루트에 덮어쓴다. 아직 서버는 시작하지 않는다.
3. 먼저 `VERIFY_GEMINI_TOOL_WINDOWS.bat`을 실행한다. 이 검사는 실제 Google ADK와 Gemini를 사용해 `propose_recovery` Tool을 한 번 호출하고 소요 시간을 출력하지만 Unity 명령은 실행하지 않는다.
4. `[PASS] Real Google ADK function calling completed.`가 나올 때만 서버를 시작한다. FAIL이면 Quest 재시험을 하지 않고 오류 유형을 먼저 해결한다.
5. 런타임 `0.5.0-rc4`를 확인하고 `meta-hands-v1` 새 세션과 연결 JSON을 만든다.
6. START → A 잡고 놓기 → B에서 손가락을 완전히 펴는 동작을 사이에 두고 세 번 핀치한다.
7. 진단 중에는 추가 핀치, HELP, STOP을 누르지 않는다.
8. `recovering`을 거쳐 `retest · 1 recovery attempt`가 표시된 뒤에만 B를 새로 잡는다.
