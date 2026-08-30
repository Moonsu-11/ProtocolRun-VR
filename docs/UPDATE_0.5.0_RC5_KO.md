# ProtocolRun-VR 0.5.0 RC5 업데이트

RC5는 RC4 실환경 연결 검사에서 실제 Google ADK Function Calling이 9.33초 만에 성공한 뒤 출력된 `GeneratorExit`, `Failed to detach context`, `Root node ... was cancelled` 정리 오류를 수정한다.

## 원인

RC4는 `propose_recovery`가 실행되는 즉시 `Runner.run_async()` 반복문을 `break`했다. 복구 제안은 이미 만들어졌지만, Google ADK 비동기 생성기가 강제로 닫히며 OpenTelemetry 컨텍스트 정리 오류가 출력됐다.

## 변경

- `propose_recovery`가 ADK `ToolContext`를 받아 `actions.skip_summarization = True`를 설정한다.
- ADK는 Tool FunctionResponse를 최종 응답으로 판단하므로 불필요한 두 번째 Gemini 호출을 하지 않는다.
- 호출 스트림을 끝까지 자연스럽게 소비하며 강제 `break`를 제거했다.
- Windows 검증 배치가 정리 오류 문구를 자동 검사해, Tool 호출 뒤 정리 오류가 있으면 `[PASS]`가 먼저 출력됐더라도 최종 실패로 처리한다.
- 복구 제안, 결정론적 Firewall, Unity 명령 승인 규칙은 변경하지 않았다.

## 합격 기준

`VERIFY_GEMINI_TOOL_WINDOWS.bat` 실행 결과에 다음 항목이 모두 필요하다.

1. `[PASS] Real Google ADK function calling completed.`
2. `Tool: propose_recovery`
3. `Action: restore_hand_grab_baseline`
4. 명령 종료 후 `Failed to detach context`, `GeneratorExit`, `Root node ... was cancelled`가 없어야 한다.

이 검사가 통과한 뒤에만 새 Unity/Quest 세션을 만든다. 검사는 실제 Unity 복원 명령을 실행하지 않는다.
