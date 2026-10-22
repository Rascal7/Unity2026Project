# 🦊🐰 Project: Unity2026 Co-op Platformer

> **Photon Fusion 2 기반의 2인 협동 퍼즐 액션 플랫포머 게임**  
> 플레이어는 각각 **여우(Fox)**와 **토끼(Rabbit)**가 되어 음성 인식 스킬과 고유 협동 기믹을 활용해 스테이지를 돌파합니다.

---

## 📌 주요 특징 (Key Features)

- **2인 협동 멀티플레이 (Photon Fusion 2)**: 호스트/클라이언트 기반 실시간 동기화 및 물리 기반 협동 퍼즐
- **오프라인 한국어 음성 인식 스킬 (Vosk STT)**:
  - 마이크로 키워드를 발화하여 즉각적으로 스킬 발동
  - 여우 키워드: `"대시"`, `"킁킁"`, `"체인지"`
  - 토끼 키워드: `"당근"`, `"점프"`, `"파워업"`
  - 공통 키워드: `"여기"` (협동 핑 마커 호출)
- **실시간 인게임 음성 채팅 (Photon Voice)**: 상시 음성(Open Mic) 및 푸시투톡(Push-to-Talk) 모드 지원
- **실시간 리더보드 & 인증 (Firebase)**:
  - Firebase Authentication: 계정 생성, 로그인 및 게스트 플레이 지원
  - Firebase Realtime Database: 클리어 타임 및 구슬 조각(Shard) 보너스 기반 순위 산정

---

## 🛠 기술 스택 (Tech Stack)

| 구분 | 기술 / 라이브러리 | 버전 / 비고 |
| :--- | :--- | :--- |
| **Engine** | Unity | 6000.3.11f1 (Unity 6) |
| **Render Pipeline** | Universal RP (URP) | 2D Renderer |
| **Multiplayer** | Photon Fusion 2 | Networked Physics & Input |
| **Voice Chat** | Photon Voice 2 | 인게임 보이스 채팅 |
| **Voice Recognition** | Vosk Unity | 한국어 경량 모델 (`vosk-model-small-ko-0.22`) |
| **Backend / DB** | Firebase | Authentication & Realtime Database (REST API) |

---

## 🎮 조작법 (Controls)

### 기본 조작 (Keyboard)
| 동작 | 키 바인딩 | 비고 |
| :--- | :--- | :--- |
| **이동** | `W` / `A` / `S` / `D` 또는 `방향키` | 사다리 오르내리기 포함 |
| **점프** | `Space` | 가변 점프 / 코요테 타임 지원 |
| **스킬 1 (Ch.1)** | `Left Shift` | 여우: 대시 / 토끼: 당근 던지기 |
| **스킬 2 (Ch.2)** | `Left Ctrl` | 여우: 킁킁 / 토끼: 고점프 |
| **스킬 3 (Ch.3)** | `Left Alt` | 여우: 위치 바꾸기 / 토끼: 파워업 |
| **푸시투톡 (PTT)** | `F` (기본값) | 설정 메뉴에서 상시/PTT 변경 가능 |
| **설정 메뉴** | `ESC` | 키 재설정, 마이크/사운드 볼륨 조절 |

### 음성 명령어 (Voice Commands)
마이크를 통해 아래 단어를 말하면 해당 슬롯 스킬이 발동됩니다.
- **여우**: `"대시"` (슬롯1) / `"킁킁"` (슬롯2) / `"체인지"` (슬롯3)
- **토끼**: `"당근"` (슬롯1) / `"점프"` (슬롯2) / `"파워업"` (슬롯3)
- **핑 마커**: `"여기"` (동료 플레이어에게 위치 핑 표시)

---

## 🚀 시작 가이드 (Getting Started)

### 1. 프로젝트 에셋 다운로드
대용량 에셋 번들은 아래 드라이브 링크에서 다운로드 후 프로젝트에 임포트합니다.
- 📦 **[에셋 드라이브 다운로드 링크](https://drive.google.com/file/d/1BW_daCfHoEdorS8qd5Kje_qcwJMTD9_d/view?usp=sharing)**

### 2. Vosk 한국어 음성 모델 확인
오프라인 음성 인식을 위해 모델 폴더가 아래 경로에 정상 배치되어 있는지 확인합니다:
```
Assets/StreamingAssets/vosk-model-small-ko-0.22/
```

### 3. Firebase Realtime Database 보안 규칙 설정
랭킹 조회 및 회원가입 시 `401 Unauthorized`를 방지하기 위해 Firebase 콘솔 > Realtime Database > 규칙에 아래 설정을 적용합니다:
```json
{
  "rules": {
    "ranks": {
      ".read": true,
      ".write": true,
      ".indexOn": ["clearTime"]
    },
    "nicknames": {
      ".read": true,
      ".write": "auth != null"
    },
    "users": {
      "$uid": {
        ".read": "auth != null",
        ".write": "auth != null && auth.uid == $uid"
      }
    }
  }
}
```

---

## 📂 프로젝트 구조 (Scenes & Core Modules)

### 주요 씬 (Scenes)
- `Assets/Scenes/Lobby.unity`: 메인 로비, 로그인/게스트 플레이, 방 생성 및 참가, 랭킹 보드
- `Assets/Scenes/CharacterSelect.unity`: 캐릭터 선택 (여우 / 토끼) 및 레디
- `Assets/Scenes/savefile.unity`: 세이브 슬롯 선택
- `Assets/Scenes/TutorialStage.unity`: 기본 조작 및 협동 스킬 튜토리얼
- `Assets/Scenes/Main Stage1~3.unity`: 메인 챕터 1, 2, 3 스테이지
- `Assets/Scenes/OpeningCutscene.unity` / `EndingCutscene.unity`: 스토리 컷씬

### 핵심 스크립트 (Core Scripts)
- `PlayerMovement.cs`: 물리 기반 이동, 점프(버퍼/코요테 타임), 사다리, 챕터별 스킬 로직
- `VoiceSkillInput.cs`: Vosk 음성 인식 엔진 백그라운드 스레드 및 키워드-스킬 매핑
- `PhotonVoiceBridge.cs`: 인게임 음성 채팅 및 마이크 설정 연동
- `RankingManager.cs`: Firebase REST API 통신, 리더보드 조회/저장, 클리어 타임 판정
- `AuthManager.cs`: Firebase Authentication (회원가입, 로그인, 게스트 모드)
- `Spawner.cs` / `BasicLauncher.cs`: Fusion 세션 라이프사이클 및 입력 수집
