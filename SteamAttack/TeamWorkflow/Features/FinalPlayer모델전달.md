# 기능: FinalPlayer 모델 전달과 NGH_Player 연결

| 항목 | 내용 |
|---|---|
| 한 줄 설명 | ClothTest의 FinalPlayer3 외형과 치마·배래 설정을 기존 NGH 플레이어에 연결한다 |
| 담당자 | YPH · 플레이어 프리팹 소유자 NGH |
| 사용한 AI | Codex |
| 완료일 | 2026-10-27 |
| 브랜치 / 커밋 | `YPH` / 이번 교체는 미커밋 |
| 관련 기획서 | 없음 (사용자가 요청한 모델 교체) · 기준 결정: 없음 |
| 상태 | 로컬 적용·테스트 완료. 다른 PC와 빌드는 미검증 |
| 대상 플랫폼 | PC |

## 1. 이 기능은 무엇인가

`NGH_Player.prefab`의 몸·옷·장신구를 FinalPlayer3 자산으로 연결했다. 치마와 배래는 MagicaCloth2로 움직이고, 패키지에 들어 있는 피부·조끼 간격 보정도 함께 실행한다. 기존 이동·전투·카메라·무기 기능과 Animator Controller는 유지한다.

이 문서의 이전 294MB FBX 수동 전달 안내는 10월 23일 모델에 관한 기록이다. 현재 외형 경로는 `Assets/Arts/Models/YPH/FinalPlayer3/`이다. 기존 `FinalPlayerMagica/` 자산은 검·검집 등의 참조가 남아 있으므로 삭제하지 않는다.

## 2. 5분 안에 써 보기

1. Unity 프로젝트 `SteamAttack/`을 연다. MagicaCloth2가 먼저 설치되어 있어야 한다.
2. `Assets/Scenes/YPH/YPH_MainPlayerTest.unity`를 연다.
3. Hierarchy의 `NGH_Player/Hero_Bound`에서 `MC2_MeshSkirt`, `MC2_MeshSleeves`를 확인한다.
4. Play를 누르고 아래 조작으로 이동·공격·사격을 확인한다.

다른 PC에 이 변경을 전달할 때는 프리팹 변경, 새 모델·재질·텍스처, 보정 코드와 각각의 `.meta`를 함께 전달한다. 원본 unitypackage를 Import하는 것만으로 기존 NGH_Player 프리팹 교체까지 자동 수행되지는 않는다.

## 3. 테스트 조작법

### 3-1. 게임 조작 키

| 입력 | 동작 | 조건 |
|---|---|---|
| WASD / Shift | 걷기 / 달리기 | 조작 안내창이 닫혀 있어야 한다 |
| Space | 구르기 | 행동 중에는 기존 플레이어 규칙을 따른다 |
| F | 발도·납도 | 검 장착 상태 |
| 좌클릭 / 우클릭 | 검 약공격 / 강공격 | 검 장착 상태 |
| Tab | 무기 교체 | 총 장착 후 우클릭으로 조준한다 |
| 우클릭 유지 + 좌클릭 | 총 조준 + 발사 | 증기가 있고 재장전이 끝난 상태 |

입력 방식은 기존 `NGH.PlayerController`와 `NGH.CombatBridge`의 Input System을 그대로 사용한다.

### 3-2. 테스트 전용 키

새 키·런타임 테스트 컴포넌트는 추가하지 않았다. 로컬 검사 명령은 임시 입력 장치를 만들고, 종료 시 제거한 뒤 저장된 씬을 다시 연다.

### 3-3. 테스트 시나리오

| 순서 | 확인 내용 |
|---|---|
| 대기·걷기·달리기 | 메시가 보이고 발 위치와 의상 시뮬레이션이 유지된다 |
| 구르기·발도·공격·받아치기 | 기존 Animator와 무기 연결이 유지된다 |
| 총 교체·조준·발사 | `Shot`이 재생되고 실제 발사 이벤트가 발생한다 |
| Play 종료 | 테스트 입력·적 비활성화·임시 상태가 저장되지 않는다 |

## 4. 어떻게 만들어졌나

### 4-1. 파일

| 경로 | 변경 | 역할 |
|---|---|---|
| `Assets/Prefabs/NGH/NGH_Player.prefab` | 수정 | 실제 게임 플레이어의 모델·Avatar·의상 설정 |
| `Assets/Scenes/YPH/YPH_MainPlayerTest.unity` | 수정 | MagicaCloth의 씬 위치별 초기화 기록 갱신 |
| `Assets/Prefabs/YPH/HeroPlayer/Models/FinalPlayer3.prefab` | 신규 | 가져온 모델 원본 프리팹 |
| `Assets/Arts/Models/YPH/FinalPlayer3/` | 신규 | FBX, 보정 메시, 재질, 텍스처 |
| `Assets/Scripts/YPH/FinalPlayer3Cloth/` | 신규 | 패키지의 의상 보정 코드 6개 |

### 4-2. 관계와 처리 순서

기존 Animator가 본을 움직인다. MagicaCloth2가 치마·배래를 계산하고, 패키지의 `SkirtBodySurfaceContact`, `SkirtVestLayerContact`, `NGHSleeveSkirtSeparation`, `AttachedSleeveSkinContact`가 보정한다. `RearSkirtSurfacePatch`, `SleeveSeparationGuard`는 이 코드들이 사용하는 보조 클래스다.

### 4-3. 기존 연결 보존

기존 `Hero_Bound`, Animator, 본 66개를 유지했다. 원래 객체 ID 427개를 모두 보존하고 게임 컴포넌트 103개의 직렬화 설정이 바뀌지 않았음을 검사했다. 검·총·스팀백·무기 소켓과 카메라가 참조하는 기존 본도 그대로다.

기존 모델은 패키지보다 Armature가 `(0, 0.5783447, -0.007424336)`만큼 올라가 있다. 기존 본 위치를 보존하고 새 모델 직속 자식에 이 좌표 차이를 적용해 발 위치를 유지했다. 패키지 루트의 ClothTest 씬 좌표는 가져오지 않았다.

### 4-4. 중복 방지

기존 `YPH.SkirtBodySurfaceContact`는 비활성화했다. `YPH.FinalPlayer3Cloth` 버전이 보정을 맡는다. 기존 장식용 `Sword`, `SwordHouse`의 비활성 상태와 게임용 검·검집은 유지했다. MagicaCloth2 본체와 게임 애니메이션은 추가 Import하지 않았다.

## 5. 왜 이렇게 만들었나

본을 새로 만들면 외부 컴포넌트가 참조하던 손·머리·무기 연결을 다시 지정해야 한다. 두 모델의 본 구조가 같아 기존 객체를 보존한 채 메시와 의상 데이터를 옮겼다. 현재 Move/Fixed 영역과 물리 수치를 임의로 다시 조정하지 않았다.

## 6. 인스펙터 값 설명

| 항목 | 현재 값·의미 |
|---|---|
| Animator Controller | `Assets/Animations/NGH/NGH_HeroAnimator.controller` 유지 |
| Avatar | `FinalPlayer3/Model/FinalPlayer.fbx`의 Humanoid Avatar |
| MagicaSettings | 150Hz, 프레임당 최대 5회, OnAwake |
| 치마 | 충돌체 13개, 패키지 설정과 Move/Fixed 영역 유지 |
| 배래 | 충돌체 8개, 패키지 설정과 Move/Fixed 영역 유지 |
| 기존 치마 피부 보정 | `YPH.SkirtBodySurfaceContact.enabled = false` |

## 7. 내 코드에서 쓰는 법

### 7-1. 공개 API

기존 플레이어 API는 바꾸지 않았다. 의상 보정은 각 컴포넌트가 MagicaCloth2의 시뮬레이션 이벤트에 연결한다.

### 7-2. 사용 예시

추가 코드는 필요 없다. 기존 `NGH_Player.prefab`을 사용한다.

### 7-3. 연결 시 주의

의상 Renderer만 따로 복사하면 충돌체·본·보정 메시 참조가 빠진다. 같은 본을 재사용하는 이번 적용 명령처럼 모든 직렬화 참조를 대응 객체에 연결하거나, 모델 전체와 게임 참조를 함께 옮겨야 한다.

## 8. 확장·복구 방법

이전 상태 복구 자료는 로컬 `Docs/Archive/2026-10-27_FinalPlayer3교체/Backups/`의 `NGH_Player.before.prefab`과 `YPH_MainPlayerTest.before.unity`다. 복구 전 이후에 사용자가 추가한 변경이 있는지 먼저 비교한다. 새 모델 자산을 먼저 삭제하면 현재 프리팹의 참조가 끊긴다.

## 9. 협업 가이드

플레이어 프리팹 소유자는 NGH이고, 이번 수정은 사용자의 해당 프리팹 모델 교체 요청에 따라 수행했다. 모델과 보정 코드는 YPH 경로에 둔다. 본 이름·객체 ID·게임 컴포넌트 필드·무기 소켓을 임의로 바꾸지 않는다. 커밋·push는 수행하지 않았다.

## 10. 의존성과 준비물

Unity `6000.6.0f1`, URP, 기존 MagicaCloth2 `2.18.3`, Burst·Collections·Mathematics를 사용한다. MagicaCloth2는 `Assets/Scripts/YPH/ThirdParty/MagicaCloth2/`의 기존 설치를 사용한다. 원본 패키지에는 유료 플러그인 본체·클립·Animator Controller가 없다.

원본 패키지는 `C:/Project/Unity6.6_ClothTest/Exports/FinalPlayer3/FinalPlayer3_NoAnimations.unitypackage`이며 SHA-256은 `60f5cb455a2511edec6e9c925ade143b07e44136b9eb9e82beea146835595f45`다. FBX의 중복 내장 텍스처는 제거된 상태이며 외부 텍스처를 함께 사용한다.

## 11. 성능 기록

별도 성능 측정은 하지 않았다. 캡처·진단이 포함된 Editor 테스트의 프레임 수를 실제 게임 FPS로 해석하지 않는다.

## 12. 검증 결과

| 항목 | 결과 |
|---|---|
| Import·명령 컴파일·본·Avatar·참조 | 통과. 누락 스크립트·끊어진 직렬화 참조 없음 |
| 치마·배래 설정과 페인트 | 프리팹·현재 씬에서 패키지와 일치 |
| 기존 객체·게임 연결 | 객체 ID 427개, 본 66개, 게임 컴포넌트 설정 103개 보존 |
| 실제 입력 Play | 829프레임. 걷기 1.95m, 달리기 1.60m, 구르기·발도·약공격·받아치기·총 발사 1회 실행 |
| 의상 | 두 MagicaCloth 유효, 두 Renderer의 시뮬레이션 메시 활성, 유한한 정점 좌표 확인 |
| 테스트 중 로그 | 오류 0, 경고 0 |
| 렌더링 | 대기·달리기·총 장착 캡처. 일부 구도는 테스트 오브젝트에 가려 정밀한 손·총구 외형 검수에 한계가 있음 |

첫 검사는 우클릭 조준을 넣지 않아 발사 수 0으로 검사 조건에 실패했다. 조준 입력을 추가한 두 번째 검사에서 통과했다. 게임 코드를 수정해서 통과시킨 것은 아니다. 로컬 결과는 `Docs/Archive/2026-10-27_FinalPlayer3교체/UnityCommands/play-result.txt`에 있다.

## 13. 알려진 문제와 확인하지 않은 범위

- 패키지에 남아 있던 빠른 팔 비틀림 등의 일부 배래 관통 한계는 이번 모델 교체로 해결된 것이 아니다.
- ClothTest의 ArmContinuity 테스트 애니메이션은 포함하지 않았고 SteamAttack 원본 애니메이션을 유지했다.
- 모든 공격·전환의 관통, 적 공격을 실제 받아치는 판정, Main·BossArena 씬 전환, 다른 PC Import와 Player 빌드는 확인하지 않았다.
- 기존 FBX·재질·검·검집 참조가 남아 있으므로 이전 자산 폴더를 일괄 삭제하지 않는다.

## 14. 용어

GUID는 Unity 자산의 고유 번호이고, 객체 ID는 프리팹 내부 연결에 쓰이는 번호다. Avatar는 사람 본 구조와 애니메이션을 연결하는 자산이다. Move/Fixed는 천에서 움직일 영역과 고정할 영역이다.

## 15. 변경 이력

| 날짜 | 작성·수정 | 내용 | 커밋 |
|---|---|---|---|
| 2026-10-23 | YPH(Claude) | 기존 FinalPlayer FBX 전달 문서 작성, 이후 Git LFS 안내 추가 | 기존 기록 참고 |
| 2026-10-27 | YPH(Codex) | FinalPlayer3 적용 경로·본 보존·의상·Play 검증·복구 절차로 갱신 | 미커밋 |
