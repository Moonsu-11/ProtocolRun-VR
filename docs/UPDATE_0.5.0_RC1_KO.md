# ProtocolRun-VR 0.5.0 RC1 업데이트

이번 업데이트는 참가자가 `CUBE_B`를 정상 상태로 먼저 잡는 절차를 제거한다.

## 참가자에게 보이는 동작

1. START를 누르면 A/B/C의 역할과 A/B의 정상 컴포넌트 기준값이 먼저 등록된다.
2. 같은 START 처리 안에서 B의 기존 `HandGrabInteractable` 및 동반 `GrabInteractable` 경로가 자동으로 비활성화된다.
3. 참가자는 A만 한 번 잡고 놓아 연습한다.
4. 참가자가 B를 처음 잡으려 할 때부터 B는 잡히지 않는다.
5. 서로 다른 핀치 실패 3회가 수집되면 Gemini가 등록 기준값과 현재 비활성 상태를 비교해 복구를 제안한다.
6. 결정론적 Firewall이 승인하면 Unity가 B의 기존 컴포넌트 enabled 상태만 복원한다.
7. 복원 후 같은 B를 새로 잡아야 Verification Passed가 된다.

C는 여전히 의도적으로 잡을 수 없는 물체이며 복구 대상이 아니다.

## 적용

1. Unity Play와 `RUN_LOCAL_WINDOWS.bat`를 종료한다.
2. 업데이트 ZIP의 내용을 GitHub 저장소 루트에 덮어쓴다.
3. 서버를 다시 실행한다.
4. Unity 컴파일 오류가 0개인지 확인한다.
5. `Tools > ProtocolRun VR > Run Recovery Gate Checks`를 실행한다.
6. 서버 콘솔에서 새 `meta-hands-v1` 세션을 만들고 새 Connection JSON을 Unity에 저장한다.

기존 세션은 새 프로토콜과 호환되지 않으므로 재사용하지 않는다. B의 Grab 컴포넌트를 Inspector에서 직접 끄지 않는다. 정상 기준값을 먼저 캡처한 뒤 START 시점에 런타임이 자동으로 비활성화한다.
