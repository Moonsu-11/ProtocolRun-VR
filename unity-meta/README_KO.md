# Meta Hands SDK · 0.5.0 RC6

통합 후보 버전. [전체 시작 안내](../docs/START_HERE_KO.md)를 먼저 따른다.

Unity 6000.3.16f1 / Meta XR All-in-One 205.0.0 / OpenXR 1.16.1 / Quest 3 PCVR Link의 손 추적을 대상으로 한다. 기존 `Assets/ProtocolRunVRMeta`를 덮어쓴 뒤 `Tools → ProtocolRun VR → Install Network Study`를 실행한다. 씬 배치는 바꾸지 않는다. 네트워크 세션, HUD와 Drop Zone 검출을 추가하며 기존 잡기 컴포넌트를 자동 추가/삭제하지 않는다.

0.2.1의 HandGrabInteractable + GrabInteractable 동시 배치 지원과 4개 스크립트 GUID를 유지한다. B 장애는 두 경로를 실제 비활성화하고 원래 enabled 배열로만 복원한다. Collider/Rigidbody/물체 위치/과제 조건을 복구 동작으로 수정하지 않는다. 서버 연결 중 수동 복원 버튼은 잠긴다.

현재 포함: IHand 추적/검지 손끝 핀치 후보, SDK 실제 잡기 이벤트, 동의, 프로토콜 다운로드, HTTP 이벤트 큐/재전송, ACK 중복 실행 방지, 고정 기준값 및 C 보호, 서버 승인 복원, 재시험 토큰, 배치 안정 확인, VR 패널/난이도 설문, 로컬 JSONL.

한계: Unity Editor 컴파일과 실제 Quest 테스트는 아직 하지 못했다. 핀치+근접은 의도 추정이며 모든 손 파지를 검출하지 않는다. UI와 장애 시도는 검지 손끝 위치를 우선하고 손끝 관절이 일시적으로 없을 때 손목 위치로 폴백한다. 패널 크기/위치를 실제 기기에서 조정한다. Play 재시작은 새 세션이 필요하다. C# 코드 생성/실행, AI의 임의 설정 변경, 음성/영상, 고주파 궤적 리플레이, 임의 프로토콜 DSL은 포함하지 않는다.

기준 API:
- https://developers.meta.com/horizon/reference/interaction/v205/interface_oculus_interaction_input_i_hand/
- https://developers.meta.com/horizon/reference/interaction/v205/interface_oculus_interaction_i_interactable_view/
- https://developers.meta.com/horizon/reference/interaction/v205/class_oculus_interaction_hand_grab_hand_grab_interactable/
- https://developers.meta.com/horizon/reference/interaction/v205/class_oculus_interaction_grab_interactable/
