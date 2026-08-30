# ProtocolRun-VR 0.5.0 RC6 — 실제 연결 시작

이 버전은 **실기기 검증 전 통합 후보**다. 자동 테스트 통과를 실제 Gemini/Quest/GCP 검증으로 표현하지 않는다.

## 무엇을 받았나

- `backend/`: 독립 Python 3.12 / FastAPI 서버, 영문 운영 화면, ADK 에이전트, 규칙 엔진, SQLite/Firestore 저장소, Dockerfile.
- `unity-meta/Assets/ProtocolRunVRMeta/`: 현재 Meta 손 추적용 SDK. 기존 같은 폴더에 덮어쓴다.
- `unity-project/`: 보내준 Unity 프로젝트에 최신 SDK 소스만 반영한 사본. 씬 배치는 변경하지 않았다. 설치 메뉴를 한 번 실행해야 한다.
- `deploy/`: 실제 GCP 배포/검증 스크립트.
- `app/`: 기존 한국어 React 대시보드. 서버 내장 콘솔을 쓰면 별도로 실행할 필요 없다.
- `unity/`: 이전 XRI 컨트롤러 경로. **지금 Meta 프로젝트에는 넣지 않는다.**

Spring으로 다시 만들 필요 없다. FastAPI 서버 자체가 제출 가능한 독립 서버 프로젝트다. 서버 소스와 실행법을 GitHub에 올리고, 실제 Cloud Run 배포와 작동 영상을 제출한다.

## 1. Unity 적용 — 지금 먼저 할 일

기존 프로젝트를 백업하고 Play를 정지한다.

1. `unity-meta/Assets/ProtocolRunVRMeta`를 현재 프로젝트의 `Assets/ProtocolRunVRMeta`에 덮어쓴다. 컴포넌트를 삭제했다가 붙이지 않는다. 기존 4개 스크립트 GUID를 유지했다.
2. 컴파일을 기다린다. Unity **6000.3.16f1**, Meta XR All-in-One **205.0.0**, OpenXR **1.16.1** 기준이다.
3. 큐브 설정을 확인한다.

| 큐브 | Expected Grabbable | Allow Baseline Restore | Allow Demo Controls |
|---|---|---|---|
| CUBE_A | 켬 | 끔 | 끔 |
| CUBE_B | 켬 | 켬 | 켬 |
| CUBE_C | 끔 | 끔 | 끔 |

4. `Tools → ProtocolRun VR → Install Network Study`를 실행하고 씬을 저장한다.
5. 생성된 `ProtocolRunSession`의 Head가 HMD 카메라인지 확인한다. Left/Right Hand Source는 기본적으로 자동 탐색한다. 중복으로 탐색되어 오류가 나면 실제 Interaction SDK의 좌우 `Hand` 컴포넌트를 명시적으로 넣는다. `OVRHand`를 무조건 넣는 것이 아니다. 필드는 **IHand 구현체**를 받는다.
6. `DropZONE/ProtocolRunDropVolume`의 BoxCollider가 원하는 배치 영역인지 확인한다. 원래 DropZONE 물리 콜라이더는 변경하지 않는다. 새 검출 영역은 큐브 중심이 포함되고, 물체를 놓은 뒤 0.5초간 속도가 낮아야 완료로 처리한다.
7. `Tools → ProtocolRun VR → Run Recovery Gate Checks`도 실행한다. 이 검사는 실제 손 추적 테스트를 대신하지 않는다.

컴파일 오류가 나면 Console의 **첫 번째 오류 전문**을 보내준다. 아직 이 실행 환경에는 Unity Editor가 없어 컴파일·Quest 런타임 검증을 하지 못했다.

## 2. 서버 실행

### 우선 로컬 PC에서 실행

Python 3.12가 설치된 Windows에서는 프로젝트 루트의 `RUN_LOCAL_WINDOWS.bat`를 더블클릭한다. 가상환경 생성, 고정 버전 설치, 서버 시작, 브라우저 열기를 한 번에 처리한다. macOS/Linux에서는 `./RUN_LOCAL_MAC_LINUX.sh`를 실행한다.

수동으로 실행하려면 Windows PowerShell:

```powershell
cd backend
py -3.12 -m venv .venv
.\.venv\Scripts\python.exe -m pip install -r requirements-dev.txt
.\.venv\Scripts\python.exe run_local.py
```

Linux/macOS:

```bash
cd backend
python3.12 -m venv .venv
.venv/bin/pip install -r requirements-dev.txt
.venv/bin/python run_local.py
```

`http://127.0.0.1:8080/console/`에서 서버 내장 콘솔을 연다. `backend/.env.local`의 연구자 토큰을 **본인 화면에서만** 복사해 입력한다. 토큰은 터미널에 자동 출력하지 않는다.

로컬 저장·세션 연결은 Google 로그인 없이 확인할 수 있다. **실제 Gemini 진단에는 Google 인증이 필요하다.** 로그인 없이 로컬에서 시험하려면 Google AI Studio에서 만든 키를 `backend/.env.local`의 `GOOGLE_API_KEY`에 본인 PC에서만 넣는다. 키를 채팅·GitHub·영상에 노출하지 않는다. Vertex AI를 쓰려면 `GOOGLE_GENAI_USE_VERTEXAI=TRUE`, 실제 `GOOGLE_CLOUD_PROJECT`, `GOOGLE_CLOUD_LOCATION=global`을 설정하고 본인 환경에서 ADC 인증한다. 인증이 없으면 자동 복구를 흉내 내지 않고 오류를 기록한다.

### 제출용 Cloud Run 배포

본인 계정으로 인증된 Google Cloud Shell에 전체 소스 ZIP을 업로드·해제한 뒤, 프로젝트 루트에서:

```bash
bash deploy/deploy_gcp.sh ACTUAL_PROJECT_ID
```

실제 GCP **프로젝트 ID**가 필요하다. 프로젝트 표시 이름만으로 추정하지 않는다. 실행 시 비용과 권한 변경 내용을 확인하고 `DEPLOY`를 입력한다.

스크립트는 Cloud Run, Firestore, Vertex AI 권한과 Secret Manager를 구성한다. 기존 PatchBlast 서비스는 삭제하지 않는다. 실제 결제/조직 정책/모델 권한에 따라 실패할 수 있으며 아직 실행 검증하지 않았다.

성공 시 출력되는 `Standalone console: https://…run.app/console/`을 사용한다. ChatGPT/Sites 로그인은 필요 없다. 연구 데이터 보호를 위해 **연구자 토큰 입력은 남아 있다.** 토큰은 `backend/.env.connection`에 비공개로 저장된다. 서버 실행은 Cloud Run의 서비스 계정으로 모델에 접근하므로 서비스 계정 JSON 키를 만들지 않는다.

기존 한국어 대시보드도 사용하려면 정확한 주소를 두 번째 인자로 준다:

```bash
bash deploy/deploy_gcp.sh ACTUAL_PROJECT_ID https://protocolrun-vr.kipper0682.chatgpt.site
```

인스턴스 상한은 비용 상한이 아니다. 결제 알림도 설정한다. 이 패키지에 GCP 로그인 세션/키는 포함되지 않는다.

## 3. Unity와 연결

1. 서버 콘솔에서 연결 후 `meta-hands-v1` 선택 → `Create session`.
2. `Copy private connection JSON` 클릭.
3. Unity에서 `Tools → ProtocolRun VR → Configure Connection`에 붙여넣고 저장한다.
4. Quest Link를 켜고 Play. 손이 보이고 추적되어야 한다.
5. VR 패널의 `AGREE / START` 가까이 손을 가져가 검지·엄지를 핀치한다. 최초 동의 전에는 손/행동 데이터를 서버로 보내지 않는다. 음성·영상은 수집하지 않는다.
6. 대시보드에 동의, 3개 물체 등록, 장비 확인 이벤트가 나타나야 한다.

동일한 Play 실행 중 네트워크가 끊기면 이벤트가 로컬 큐에 저장되고 재연결 시 순서대로 전송된다. **Play를 종료했다가 다시 시작할 때는 새 서버 세션을 만든다.** 이전 물리 상태와 복구 명령을 새 실행에 재사용하지 않는다. 이전 저널은 보존한다.

## 4. 최종 데모 사이클

1. 동의 → 양손 추적 확인.
2. START 직후 SDK가 A/B의 정상 컴포넌트 기준값을 등록한 다음 B의 HandGrab/Grab 경로를 자동으로 비활성화한다. 참가자가 B를 정상 상태로 잡는 단계는 없다.
3. 연습 단계에서는 **A만 한 번 잡고 놓는다.** B는 연습에 사용하지 않는다.
4. 목표 접근 단계에서 B를 처음 잡으려 한다. 별도의 Inspector 장애 주입 버튼은 누르지 않는다. 자동/수동 복원 버튼도 누르지 않는다.
5. B 가까이 손을 가져간 뒤 검지·엄지 핀치 → 손 펴기를 3회 반복한다. 각 시도는 0.5초 이상 관찰한다. 검지 손끝과 물체 표면 사이의 거리 기준은 기본 0.18m다. 손끝 관절을 일시적으로 읽지 못하면 손목 위치로 폴백한다. 이것은 핀치+근접으로 추정한 시도이며, 모든 형태의 파지 의도를 검출하는 것은 아니다.
6. 필요하면 참가자가 `HELP: cannot grab`을 누른다. 도움 요청 문구만으로는 복구가 승인되지 않는다.
7. 실제 Gemini가 증거 조회/복구 제안 도구를 호출한다. 서버 규칙 엔진은 정상 기준값, 서로 다른 시도, 추적, 콜라이더, 비활성 잡기 경로를 검사한다.
   `Gemini diagnosis is running`이 보이면 핀치를 멈추고 모든 물체를 놓은 채 양손 추적과 Unity Play를 유지한다. RC6는 원시 텔레메트리·위치·회전·참가자 문장을 모델 입력에서 제외하고 서버가 검증한 최소 증거만 보낸다. 호출 제한은 여전히 최대 60초이며 늘리지 않았다. 세 번째 실패에서 검증된 정확한 3개 이벤트 ID는 같은 진단 주기 동안 최대 240초만 고정되어 안전한 재시도에서 사라지지 않는다. 이때 STOP을 누르면 세션은 의도적으로 `manual_review`로 종료된다.
8. 서버 일시정지 → 원래 HandGrab/Grab enabled 상태 복원 → ACK → 재시험 명령 ACK. HMD나 손 추적 자체는 정지하지 않는다. 모든 물체를 놓아야 명령이 실행된다.
9. 참가자가 **같은 B를 새로 잡는다.** 이때만 서버 검증이 통과한다. 복원 ACK만으로 성공 표시하지 않는다.
10. B를 Drop Zone에 놓고 안정될 때까지 기다린다.
11. 난이도 1~7 선택 → SUBMIT. **보고서가 생성될 때까지 Play를 유지한다.**
12. JSON/CSV를 내려받아 원본, 감사 기록, 격리 구간, 실제 Gemini 요약을 확인한다.

C의 핀치는 비대상 물체 입력으로만 남으며, C에 잡기 컴포넌트를 추가하지 않는다.

## 5. 반드시 실제로 확인할 항목

- Unity 컴파일 오류 0개; 패널이 읽히고 손이 닿는 위치인지 확인. 패널 위치는 `ProtocolRunHUD`에서 조정 가능하다.
- A는 처음부터 정상, B는 첫 시도부터 잡히지 않음, C는 의도적으로 잡히지 않음.
- 장애 후 B만 실제로 잡히지 않음; Collider/Rigidbody/배치 값 불변.
- 서버 수신, Gemini 도구 호출, 승인된 B 복원, 새 재잡기 증거를 같은 세션에서 확인.
- 재시험 전에는 Verified가 켜지지 않음.
- 네트워크 재연결 때 중복 이벤트/복원 중복 실행 없음.
- Google 인증 실패/만료 명령/잘못된 기준값에서는 자동 실행이 차단됨.
- 클라우드 Firestore 저장, 서버 재시작 후 세션 유지 확인.
- 토큰/키 없이 저장소 및 영상 준비.

## 다음에 사용자에게 필요한 정보

우선 **실제 GCP 프로젝트 ID**를 알려주면 된다. 비밀번호/API 키/서비스 계정 JSON은 대화로 보내지 않는다. Unity 패키지 적용 후 Console 오류 여부와 정상 잡기 결과도 필요하다.
