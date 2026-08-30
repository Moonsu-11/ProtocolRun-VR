# ProtocolRun-VR 0.5.0 RC3 업데이트

RC3는 실제 Meta 손 추적 실패 이벤트가 Gemini 응답 대기 중 연속 텔레메트리에 밀려 진단 입력과 Firewall 검사에서 사라지던 문제를 수정한다.

## 변경 사항

- `grab_failed` 이벤트를 UI용 최근 이벤트 목록과 분리된 제한형 증거 버퍼에 보존한다.
- 서버가 attempt/failure 쌍, 손 추적, 입력, 거리, Collider, 구성요소 상태, 대상 ID, 기준 상태와 시간 유효성을 먼저 검증한다.
- Gemini에는 검증된 실패 이벤트 ID와 명시적인 `recovery_candidate`를 전달한다.
- Gemini가 제안한 행동은 이전과 동일하게 Protocol Firewall을 통과해야만 Unity 명령이 된다.
- 새 회귀 테스트는 80개의 후속 텔레메트리가 들어와도 세 번의 실패 증거와 자동 복구 자격이 유지되는지 확인한다.

## 적용 및 재시험

1. Unity Play와 기존 `RUN_LOCAL_WINDOWS.bat` 창을 종료한다.
2. RC3 업데이트 ZIP의 내용을 저장소 루트에 덮어쓴다.
3. `RUN_LOCAL_WINDOWS.bat`을 다시 실행하고 대시보드 런타임이 `0.5.0-rc3`인지 확인한다.
4. 기존 `manual_review` 세션은 재사용하지 말고 `meta-hands-v1` 새 세션과 새 연결 JSON을 만든다.
5. Unity Play에서 START 후 A를 잡았다 놓고, B에서 손가락을 완전히 펴는 동작을 사이에 두어 세 번 핀치한다.
6. `Gemini diagnosis is running` 동안 Play를 끄거나 STOP/HELP를 누르지 않는다.
7. 상태가 `retest`로 바뀌면 B를 새로 잡는다. 이 새 Grab 성공만 복구 검증을 통과시킨다.
