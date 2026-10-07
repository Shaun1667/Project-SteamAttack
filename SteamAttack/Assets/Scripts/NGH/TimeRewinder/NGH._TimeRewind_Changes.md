# 시간 역행 시스템 — 작업 내역 및 다른 파트 수정 사항 (NGH)

작성: 남귀훈(NGH) · 2026-10-02 (최종 기준)

이번 작업에 한해 YPH·CHG 폴더 접근과 수정을 허락받아 진행했습니다.
아래 "5. 다른 파트 수정 사항"은 각 담당자(채희강, 윤평화)가 머지할 때 확인해 주세요.
기존 동작은 바꾸지 않았고, 시간 역행이 쓰는 기능만 **추가**했습니다.

---

## 1. 한눈에 보기

- **R 키**를 누르면 연출이 먼저 나온 뒤, **5초 전 상태**로 한 번에 되돌아갑니다.
- 연출 중에는 **무적**이고, 기본 설정으로 조작도 막힙니다. 연출 내용은 비워 둔 코루틴에 나중에 작성하면 됩니다.
- 공격·발도/납도·구르기·피격 중에는 **사용할 수 없습니다.** (Inspector에서 항목별로 켜고 끔)
- 테스트 씬은 `Scenes/NGH/TimeTest.unity` 입니다.

---

## 2. 동작

### 기록 (0.02초 간격, 최근 5초만 보관, 오래된 기록은 폐기)

| 분류 | 내용 |
|---|---|
| 플레이어 | 위치, 바라보는 방향, 체력 |
| 증기 게이지 | 등에 붙은 `YPH_SteamTank`의 압력 |
| 애니메이션 | 레이어별 재생 중인 동작과 진행 지점, 파라미터, 재생 속도 |
| 무기·행동 | 납도/발도 상태, 현재 무기 칸, 진행 중인 행동(공격·구르기·피격 등) |
| 화면 방향 | 마우스로 돌린 카메라의 좌우·상하 각도 (`NGH_ThirdPersonCamera`) |
| 락온 | 락온 여부와 대상 |

### 발동 흐름
1. R 키 입력 (사용 제한에 걸리면 발동하지 않음)
2. 연출 코루틴 `RewindEffectRoutine()` 실행 — 지금은 비어 있음. 이 동안 무적 + 조작 잠금
3. 연출이 끝나면 위 상태를 5초 전 기록으로 모두 복원
4. 복원 후 기록을 비우고 새로 쌓기 시작

### 복원 규칙
- **락온**: 5초 전에 락온 중이었으면 같은 대상으로 다시 락온, 아니었으면 해제. 대상이 그 사이 죽었거나 사라졌으면, 또는 발도 + 근접 무기 상태가 아니면 락온 없이 복원합니다.
- **화면 방향**: 카메라 각도를 기록해 두었다가 함께 되돌립니다. (테스트: 좌우 +120°, 상하 40° 회전 후 역행 → 기록해 둔 각도로 정확히 복원)
- **공격·구르기·발도/납도·피격 도중이었던 시점**: 그 동작은 되살리지 않고 대기 상태로 복원합니다. (2026-10-02: 구르기 중이던 시점으로 돌아가면 역행 직후 이어서 구르던 버그 수정) 위치, 체력, 발도 상태 등 나머지는 그대로 복원합니다. 미리 눌러 둔 입력(콤보 예약 등)은 항상 비웁니다.
- 기록이 5초만큼 쌓이기 전에 쓰면, 쌓인 만큼만(가장 오래된 기록까지) 되돌아갑니다.

### 수정한 버그: 역행 직후 공격 모션이 나가는 문제
- 원인 1: 5초 전 기록이 공격 도중이면 그 공격 동작까지 그대로 되살아났음
- 원인 2: 그때 미리 눌러 둔 공격 입력(콤보 예약)도 같이 되살아나 역행 직후 공격이 나갔음
- 해결: 위 "복원 규칙"의 공격 도중 처리 (공격 동작 제외 + 예약 입력 항상 삭제)
- 테스트: 락온 + 강공격 + 약공격 예약이 걸린 순간으로 역행 → 직후 상태가 대기·발도·락온 유지, 이후 30프레임 동안 공격이 한 번도 나가지 않음

---

## 3. 사용 제한

체크된 동작 중에는 R을 눌러도 발동하지 않습니다. 네 항목 모두 기본으로 켜져 있습니다.

| 항목 | 막는 동작 |
|---|---|
| Block While Attacking | 약공격, 강공격, 2타, 사격 |
| Block While Draw Sheath | 발도, 납도 |
| Block While Rolling | 구르기 |
| Block While Hit | 피격 (소/대) |

- 막히면 콘솔에 "구르기 중에는 시간 역행을 사용할 수 없습니다"처럼 원인 동작이 표시되고, 화면 왼쪽 위에는 `Blocked (Roll)`처럼 표시됩니다.
- 테스트: 발도, 납도, 구르기, 피격(소/대), 공격 상태에서 모두 차단 확인. 구르기 항목을 끄면 구르기 중에도 사용 가능.

---

## 4. 새로 만든 파일과 Inspector 설정 (NGH)

| 파일 | 내용 |
|---|---|
| `Scripts/NGH/CHG_TimeRewind.cs` | 시간 역행 본체. 플레이어 루트에 붙임 |
| `Scripts/NGH/CHG_TimeRewindTester.cs` | TimeTest 씬 전용 테스트 입력 (증기 소비/회복 키) |
| `Scripts/NGH/NGH_TimeRewind_Changes.md` | 이 문서 |

### Inspector 설정 (`CHG_TimeRewind`)
| 항목 | 기본값 | 설명 |
|---|---|---|
| Player / Character Controller / Animator / Steam Tank / Player Camera | 자동 | 비워 두면 알아서 찾음 |
| Rewind Seconds | 5 | 되돌아갈 시간 = 기록 보관 시간 |
| Record Interval | 0.02 | 기록 간격(초), 0이면 매 프레임 |
| Rewind Key | R | 발동 키 |
| Lock Control During Effect | ✔ | 연출 중 조작 막기 |
| Allow While Dead | ✘ | 사망 중 사용 허용 |
| Block While Attacking / Draw Sheath / Rolling / Hit | ✔ | 3번 항목 참고 |
| Show Debug GUI | ✔ | 화면 왼쪽 위에 기록 시간·HP·증기·차단 상태 표시 |
| Log Rewind | ✔ | 역행·차단 시 콘솔에 로그 출력 |

### 연출 넣는 방법
`CHG_TimeRewind.RewindEffectRoutine(Snapshot target)` 안에 작성합니다.
`target`에 되돌아갈 시점의 위치 등이 들어 있어 연출에 활용할 수 있습니다.
이 코루틴이 도는 동안은 자동으로 무적이고, 끝나는 순간 복원됩니다.
다른 스크립트에서 반응하려면 `RewindStarted` / `RewindFinished` 이벤트를 구독하면 됩니다.

---

## 5. 다른 파트 수정 사항 (머지 시 확인 부탁)

### CHG — `Scripts/CHG/NGH_PlayerController.cs` (채희강)
- `IsInvincible` 조건에 `|| _externalInvincible.Count > 0` 추가
- 필드 추가: `_externalInvincible` (외부에서 켠 무적 목록)
- 맨 아래 "유틸" 위에 **시간 역행 연동** 영역 추가
  - `struct RewindState` — 기록할 내부 상태(체력, 행동, 발도/납도, 무기 칸, 락온 대상 등)
  - `SetExternalInvincible(object source, bool on)` — 외부 시스템이 무적을 켜고 끔
  - `CaptureRewindState()` / `RestoreRewindState(RewindState)` — 상태 기록/복원
  - `AttackDataFor(ActionState)` — 복원용 헬퍼 (private)

### CHG — `Scripts/CHG/NGH_WeaponHolder.cs` (채희강)
- `RestoreState(int index, bool drawn)` 추가 — 교체 연출을 멈추고 무기 칸·발도 표시를 즉시 맞춤

### CHG — `Scripts/CHG/NGH_ThirdPersonCamera.cs` (채희강)
- `ViewAngles` (현재 yaw/pitch 읽기), `SetViewAngles(yaw, pitch)` (즉시 지정) 추가 — 카메라 방향을 기록/복원하는 용도. 카메라가 움직이는 방식은 그대로

### YPH — `Scripts/YPH/SteamGauge/YPH_SteamTank.cs` (윤평화)
- `RestorePressure(float pressure)` 추가 — 저장해 둔 압력으로 되돌림
  - 소비가 아니므로 배출구 분출(`OnConsumed`)·실패 이벤트 없이 `OnPressureChanged`만 발생

---

## 6. TimeTest 씬 구성 (`Scenes/NGH/TimeTest.unity`)
- `chg_Player` 프리팹 배치 (태그 **Player**) + `CHG_TimeRewind` 추가
- `YPH_SteamBackpack` 프리팹을 플레이어 **Spine2 뼈**에 붙여 등 뒤에 부착 (크기 0.36배)
  - 캐릭터 모델에 이미 증기통 장식이 있어서 그 바로 뒤에 붙였습니다. 위치·크기는 씬에서 조절 가능
- `chg_PlayerCamera` 배치, 기존 `Main Camera`는 비활성화
- 공격·회복 테스트용 `chg_Dummy` 1개, `CHG_TimeRewindTester` 오브젝트
- 프리팹 원본(chg_Player, YPH_SteamBackpack)은 수정하지 않았고, 모두 씬 안에서만 구성했습니다.

### 테스트 키
| 키 | 동작 |
|---|---|
| R | 시간 역행 |
| 1 / 2 | 자신에게 데미지 1 / 2 (CHG 디버그 키) |
| 3 / 4 | 증기 20 소비 / 20 회복 (NGH 테스터) |
| F | 발도/납도 |
| 좌클릭 | 공격 (더미를 때리면 체력 회복) |

---

## 7. 참고
- 사망 상태에서는 기본적으로 사용할 수 없습니다 (`Allow While Dead`로 변경 가능).
- 적·보스 등 플레이어 외 오브젝트는 되돌리지 않습니다 (요청 범위: 무기 상태, 체력, 증기 게이지, 플레이어 위치, 화면 방향, 락온).
