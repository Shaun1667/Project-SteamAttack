# 요청: GroundClamp에서 컨트롤러 없는 Animator 상태 조회 방지

| 항목 | 내용 |
|---|---|
| 요청일 | 2026-10-14 |
| 요청자 | YPH |
| 받는 사람 | NGH |
| 우선순위 | 🟡 보통 |
| 희망 기한 | 다음 플레이어 통합 확인 전 |
| 관련 기획서 | 없음. 기존 Hero 자세 전달 구성의 경고 방지 |
| 상태 | 완료 |

## 1. 한 줄 요약

GroundClamp가 컨트롤러 없이 자세를 전달받는 Hero Animator의 상태를 조회해 매 프레임 경고가 발생하므로, 상태 조회 전 유효성을 확인해 주세요.

## 2. 대상 파일

| 경로 | 종류 (스크립트/씬/프리팹/설정/기타) | 수정 방식 (추가/변경/삭제) |
|---|---|---|
| `Assets/Scripts/NGH/Player/GroundClamp.cs` | 스크립트 | InLocomotion의 상태 조회 전 검사 추가 |

## 3. 왜 필요한가

개인 `YPH_HeroPlayerTest`의 `Hero_Bound`는 Animator의 Humanoid 본 정보를 사용하되, 실제 자세는 `HeroPoseRetarget`으로 전달받습니다. 따라서 해당 Animator에는 Runtime Animator Controller가 없습니다. 현재 GroundClamp는 Animator 존재 여부만 검사한 뒤 `GetCurrentAnimatorStateInfo(0)`를 호출합니다. 일반 Play 120프레임에서 동일 경고가 119건 발생했습니다. 캐릭터 교체를 막지는 않지만 콘솔이 반복 경고로 채워집니다.

## 4. 현재 상태 → 원하는 상태

- **현재:** Hero의 GroundClamp 활성, Animator 활성, Controller null. `Animator is not playing an AnimatorController` 반복 발생.
- **원하는 모습:** 컨트롤러가 없거나 실행할 수 없는 Animator는 상태를 조회하지 않고, 기존 기본 바닥 보정은 계속 수행.

## 5. 구체적인 변경 방법

1. `InLocomotion()`에서 Animator가 활성 상태이며 초기화됐고 컨트롤러를 가지고 있을 때만 상태를 조회합니다.
2. 그렇지 않으면 기존 검사 실패 경로처럼 false를 반환합니다. 본 수집·높이 계산·정상 컨트롤러의 이동 상태 판정은 유지합니다.

```csharp
if (!_animator || !_animator.isActiveAndEnabled ||
    !_animator.runtimeAnimatorController || !_animator.isInitialized)
    return false;
```

컨트롤러 없는 Hero에 별도의 이동 상태 공급자를 추가하는 기획은 이 요청에 포함하지 않습니다.

## 6. 영향 범위

- 호출부: 같은 파일 `LateUpdate()`의 `InLocomotion()` 호출.
- 확인 대상: Controller가 있는 일반 NGH 캐릭터와 Controller 없이 리타게팅하는 YPH Hero.
- 씬·프리팹 직접 수정 요청은 없습니다. NGH 코드 동시 편집 여부는 처리 전 확인이 필요합니다.

## 7. 확인 방법

1. `Assets/Scenes/YPH/YPH_HeroPlayerTest.unity`를 열고 일반 Play에서 대기합니다. 제어기나 Animator를 끄는 별도 검사 없이 경고가 사라져야 합니다.
2. 걷기·공격·구르기에서 기존 기본 바닥 보정이 유지되는지 봅니다.
3. Controller가 있는 기존 플레이어의 Locomotion·StrafeLeft·StrafeRight 높이 보정이 유지되는지 확인합니다.

## 8. 안 하면 생기는 문제 / 대안

- **안 하면:** 매 프레임 경고가 쌓여 다른 오류를 찾기 어려워집니다.
- **대안:** 경고를 기록하고 캐릭터 교체 결과를 별도 검증합니다. GroundClamp나 Hero Animator를 끄면 바닥 보정·본 사용에 영향을 주므로 임의로 끄지 않았습니다.

## 9. 첨부

- 재현 위치: `YPH_HeroPlayerIntegration/NGH_Player/Hero_Bound`.
- 일반 Play: 120프레임, 오류 0건, 해당 경고 119건. 원본 애니메이션의 `chg_Model`은 Controller가 있고 GroundClamp는 비활성, Hero 쪽 GroundClamp만 활성입니다.
- 호출 위치: `GroundClamp.InLocomotion()`의 `GetCurrentAnimatorStateInfo(0)`, `LateUpdate()`.
- 로컬 근거: 저장소 루트 `Docs/Archive/2026-10-14_Player웨이트2교체/UnityCommands/normal_play_result.txt`. 개인 기록이므로 팀에서는 위 절차로 재현합니다.
- 2026-10-15 P1 추가 재현: 새 `Assets/Scenes/YPH/YPH_EnemyTest.unity`에서도 `Hero_Bound`의 활성 GroundClamp와 Controller null 구성을 확인했습니다. Domain Reload·Scene Reload를 모두 끈 재진입에서 120프레임 관찰 동안 오류 0건, 같은 경고 121건(진입 프레임 포함)이 발생했습니다. 받아치기 입력·약점 검사는 통과했지만 P1 콘솔 경고 0건 조건은 충족하지 못했습니다. 원본 프리팹·NGH 코드는 변경하지 않았습니다.
- P1 로컬 근거: 저장소 루트 `Docs/Archive/2026-10-15_받아치기공통규약/UnityCommands/YPH_P1ReentryFinal.json`. 공유 설명은 [받아치기 공통 규약](../../Features/받아치기공통규약.md) 12~13번을 참고합니다.

---

## 처리 기록 (받는 사람이 작성)

| 날짜 | 처리자 | 결과 | 비고 |
|---|---|---|---|
| 2026-10-16 | NGH | 완료 (e516966) | 5번 방법 그대로 `InLocomotion()`에 검사 추가. `YPH_HeroPlayerIntegration`의 `Hero_Bound`(Controller null)를 띄워 120프레임 동안 해당 경고 0건 확인. 요청서의 `YPH_HeroPlayerTest` 씬은 YPH Player FBX가 저장소에 없어 Missing Prefab 상태라 그 씬으로는 확인하지 못함 |
