# 요청: Main·NGH_EnemyTest의 기존 EnemyAI 적을 YPH 적 프리팹으로 교체

| 항목 | 내용 |
|---|---|
| 요청일 | 2026-10-15 |
| 요청자 | YPH |
| 받는 사람 | NGH |
| 우선순위 | 🟡 보통 |
| 희망 기한 | 2026-10-21 (기능 마감, D-091) |
| 관련 기획서 | `Design/적/일반몬스터.md` (D-045, D-052) |
| 상태 | 요청됨 |

## 1. 한 줄 요약

YPH가 `EnemyAI`(2,170줄)를 역할별 컴포넌트로 나누면서 설정값이 새 컴포넌트로 옮겨 갑니다. `Main`과 `NGH_EnemyTest`에 저장된 기존 적 1마리씩은 설정이 빠지므로, YPH 적 프리팹으로 바꾸거나 지워 주세요.

## 2. 대상 파일

| 경로 | 종류 (스크립트/씬/프리팹/설정/기타) | 수정 방식 (추가/변경/삭제) |
|---|---|---|
| `Assets/Scenes/Common/Main.unity` | 씬 | 기존 `EnemyAI` 적 1마리를 YPH 프리팹으로 교체 또는 삭제 |
| `Assets/Scenes/NGH/NGH_EnemyTest.unity` | 씬 | 같음 |

## 3. 왜 필요한가

`YPH.EnemyAI`는 CHG 원작을 넘겨받은 것으로(D-052), 한 파일에 판단·이동·근접·원거리·애니메이터·디버그가 모두 있습니다. 매 프레임 플레이어 탐색, 최종 빌드에 남는 디버그 코드 같은 문제가 있어서 YPH가 10/16~17에 역할별 컴포넌트(`EnemyAI`, `EnemyMotor`, `EnemyMeleeAttack`, `EnemyRangedAttack`, `EnemyAnimatorLink` 등)로 나눕니다. 그러면 씬에 저장된 적의 공격·이동 설정이 새 컴포넌트로 옮겨 가지 못합니다. 두 씬은 NGH·공용 소유라 YPH가 직접 고치지 않습니다.

## 4. 현재 상태 → 원하는 상태

- **현재:** 두 씬에 `EnemyAI` 한 컴포넌트에 설정이 저장된 적이 1마리씩 있음
- **원하는 모습:**
  - `Main`: YPH 적 프리팹(`Prefabs/YPH/Enemy/` 변이 포졸, E1에서 만듦)으로 바꿈. 정식 배치(일반 몬스터 배치표)는 10/19 배치 작업 때 따로 함
  - `NGH_EnemyTest`: 같은 프리팹으로 바꾸거나, 시험에 필요 없으면 삭제

## 5. 구체적인 변경 방법

1. YPH가 E0·E1을 push하면 YPH 브랜치를 NGH에 병합합니다.
2. `Main.unity`를 엽니다. 기존 적(`EnemyAI`가 붙은 오브젝트)의 위치·회전을 기억해 두고 삭제합니다. 같은 위치에 `Prefabs/YPH/Enemy/`의 변이 포졸 프리팹을 놓고 저장합니다.
3. `NGH_EnemyTest.unity`도 같은 방법으로 처리하거나, 적을 지웁니다.
4. 프리팹의 설정은 YPH가 관리하므로 씬에서 값을 바꾸지 않아도 됩니다.

## 6. 영향 범위

- 이 파일을 참조하는 곳: `EnemyAI`의 공개 API(`ActiveEnemies`, `ParryableAttackStarted`, `TryParry` 등)는 그대로 유지하므로 NGH `PlayerParry`·`ParryRingUI`는 수정 없이 컴파일됨
- 같이 영향받을 수 있는 다른 작업자·기능: 받아치기 시험. 교체 전까지 두 씬의 기존 적은 움직이지 않음
- 씬·프리팹이면: 두 씬 모두 NGH·공용 소유. 이 시점에 같은 씬을 고치는 사람이 있는지 NGH가 확인

## 7. 확인 방법

1. `Main` Play → 교체한 적이 배회·추적·공격하고, 강공격에 동심원이 뜨고 받아칠 수 있음
2. 콘솔에 Missing Script·NullReference 없음

## 8. 안 하면 생기는 문제 / 대안

- **안 하면:** E0 병합 후 두 씬의 기존 적이 멈춰 있거나 에러가 남
- **대안:** YPH가 프리팹만 넘기고, 교체는 10/19 배치 작업 때 함께 해도 됨(그 사이 두 씬의 기존 적은 쓰지 않음)

## 9. 첨부

- YPH 계획: E0 적 AI 구조 분리, E1 변이 포졸 (YPH 개인 계획서)
- 관련 결정: D-052(적 AI는 YPH), D-061(받아치기 담당)

### 2026-10-15 E0 구현 결과 (YPH)

- 판단은 `EnemyAI`, 이동은 `EnemyMotor`, 근접·킥은 `EnemyMeleeAttack`, 사격은 `EnemyRangedAttack`, 애니메이션 연결은 `EnemyAnimatorLink`로 분리했습니다. 개발용 키는 씬마다 자동 생성되는 `EnemyDebugTools`가 처리합니다.
- 설정은 새 컴포넌트의 `_camelCase` 필드로 옮겼으며 자동 이관은 하지 않습니다. `[RequireComponent]`만으로 기존 씬의 부품과 값을 복구할 수는 없습니다. **E0만 먼저 병합해 옛 적을 그대로 실행하지 말고 E1 프리팹과 함께 교체해 주세요.**
- YPH_EnemyTest만 분리 전 값을 복원했습니다. Main·NGH_EnemyTest·BossArena는 저장하지 않았습니다. E1 프리팹은 아직 이번 작업에 포함되지 않습니다.
- EnemyAI 공개 API와 IParryTarget·ParryTargets 계약은 유지했습니다. 기존 NGH 입력·동심원은 수정 없이 컴파일됐고 Ctrl 받아치기 회귀 검사를 수행했습니다.
- 부품 구성·Inspector·검증 범위: [적 AI 기능 문서](../../Features/적AI.md).

### 2026-10-17 원거리 적 추가 (YPH)

- 원거리 적 = `Prefabs/YPH/Enemy/YPH_HeuksuArcher` (흑수회 궁수, E2-a·E2-b 구현, 모델·활쏘기 모션 포함). 자리를 지키며 2.5초마다 화살을 쏘고, 받아칠 공격은 없습니다.
- 기존 씬의 원거리 `EnemyAI` 적이 있으면 변이 포졸 대신 이 프리팹으로 바꿔 주세요.
- 정식 배치는 [일반 몬스터](../../Design/적/일반몬스터.md) 배치표(육조거리 관아 지붕 위, 광화문 앞 해태 뒤)대로 10/19 배치 작업 때 합니다. 지붕 위에 놓을 때는 발밑에 충돌체가 있어야 하고, 활의 발사 위치가 지붕에 가리지 않는지 확인해 주세요.
- 기능·검증: [흑수회 궁수 기능 문서](../../Features/흑수회궁수.md).

---

## 처리 기록 (받는 사람이 작성)

| 날짜 | 처리자 | 결과 | 비고 |
|---|---|---|---|
| 2026-10-20 | YPH | 재확인: 일부 남음 | YPH 적 프리팹 배치는 NMJ가 `NMJ_EnemySpawns`로 처리(NMJ `16e4aa5`). 그러나 `Main`에 프리팹이 아닌 옛 `EnemyAI` 적 1마리가 남아 있음(`9082bf7`·NMJ `4ec7dc6` 모두). 이 적에는 `EnemyMeleeAttack`이 없어 공격하지 못함. `NGH_EnemyTest`도 같은 적 1마리. 교체 없이 **삭제**만 하면 됨 |
