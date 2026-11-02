# NGH 작업 내역 정리 (2026-10-09 ~ 2026-10-11)

플레이어 전투 연출 · 시간 역행 · 씬 병합 · 스크립트 정리 작업을 한 곳에 모은 문서입니다.
세부 내용은 같은 폴더의 `TimeRewinder/NGH._TimeRewind_Changes.md`, `NGH_MainMerge2_Changes.md`에도 있습니다.

> 스크립트 이름은 마지막 작업(§9)에서 `이니셜_` 접두사를 지우고 namespace로 바꿨으므로, 이 문서는 **새 이름**으로 적습니다.
> 예) `NGH_PlayerController` → `NGH.PlayerController` (`Scripts/NGH/Player/PlayerController.cs`)

---

## 목차
1. 공격 이펙트 (베기 궤적)
2. 공격 애니메이션 보정
3. 타격 사운드
4. 카메라 쉐이크
5. 시간 역행
6. 프리팹 적용 · Main 씬 정리
7. Main 체력 · HUD
8. 보스맵 플레이어 교체 · 체력 이어가기
9. 스크립트 이름 정리 (namespace)
10. 콤보별 공격력
11. NGH 폴더 밖 수정 내역 (머지 시 확인)
12. 남은 것 · 주의 사항

---

## 1. 공격 이펙트 (베기 궤적)
**스크립트**: `NGH.AttackFx` (`Scripts/NGH/Player/AttackFx.cs`), `NGH.SlashTrail` (`Scripts/NGH/Player/SlashTrail.cs`)

- 약공격 1~5타, 강공격마다 `basicslash` 이펙트를 재생 (원형 형태·방향은 유지하고 너비만 넓힘)
- 3타(찌르기)는 베기 이펙트를 토대로 찌르기용으로 새로 구성
- **칼 궤적 맞춤**: 컴포넌트 메뉴(⋮) "칼 궤적으로 위치·방향 계산" — 애니메이션을 64단계로 샘플링해 칼끝·칼날 경로를 미리 계산(bake)
  - 이펙트가 칼이 실제로 지나간 면을 따라 그려지는 리본 메시(`SlashTrail`)로 표시, 굵은 끝이 항상 현재 칼 위치에 옴
  - 1타 각도·끝 높이 불일치 수정, 5타 이펙트 길이 늘림
- 항목별 설정: 재생 시점(Spawn At), 위치·방향·크기, 시작 각도, 회전, 꼬리 길이, 사운드 등
- 오브젝트 풀(`NGH.ObjectPool`) 사용, 플레이어를 따라 이동

## 2. 공격 애니메이션 보정
**스크립트**: `NGH.MocapComboBuilder`, `NGH.AttackClipBuilder` (`Scripts/NGH/`)

- 칼 궤적이 구불구불하던 문제 → 애니메이션 자체를 매끈하게 수정
  - **1타**: 휘두르는 구간을 한 평면 위로 정렬(PCA로 평면 계산 + IK), 240fps 키
  - **5타**: 가운데 꺾임 제거, 같은 방식으로 평면 정렬
  - **강공격**: 원호(ArcSwing)로 다시 만들고 휘두르는 구간을 촘촘한 키로
- 원본 백업: `NGH_Anim_LightAttack_Backup.anim`, `NGH_Anim_Combo5_Backup.anim`, `NGH_Anim_HeavyAttack_Backup.anim`

## 3. 타격 사운드
- `Audios/NGH/Slash/slash1 ~ slash5` → 약공격 1~5타, `slash6` → 강공격(좌·우클릭 동시)
- `AttackFx` 각 항목의 Sound / Volume / Pitch / Offset으로 조절
- 1타 소리는 0.1초 앞당김

## 4. 카메라 쉐이크
**스크립트**: `NGH.CameraShake` (`Scripts/NGH/CameraShake.cs`) — 플레이어 카메라(`NGH_PlayerCamera`)에 붙음

- 공격 판정이 적(Damageable)에 닿으면(`PlayerController.OnHitLanded`) 흔들림
- 세기: 약공격 0.5 / 마무리(5타) 0.75 / 강공격 1 (Inspector에서 조절)

---

## 5. 시간 역행
**스크립트 폴더**: `Scripts/NGH/TimeRewinder/`

### 5-1. 기본 (`NGH.TimeRewind`)
- 키 **Q** (기존 R에서 변경), **5초 전**으로 되돌림
- 증기 소모량 `Steam Cost`는 Inspector에서 조절 (기본 70)
- **되감기 이동 1초** (오버워치 트레이서 '역행' 참고)
  - 누른 시점 동작 → 5초 전 동작으로 바로 이어짐 (했던 동작을 반복 재생하지 않음)
  - **`Follow Recorded Path`**: 되돌아갈 위치로 곧장 가지 않고 **지나온 경로(먹선)를 거꾸로 따라 이동** (테스트: 경로 오차 0m, 도착 오차 0m)
  - **`Camera Look Along Path`**: 이동하는 동안 카메라가 가는 방향(남은 먹선)을 바라보고, 마지막 30%에서 5초 전 카메라 각도로 돌아감 (`Path Look Ahead`, `Travel Camera Pitch` 30°)
  - 이동 중 0.3m 떠오름 (`Travel Lift Height`)
- 연출용 API: `GetRecordedPath()`, `RewindTargetPosition`, `RewindTravelProgress`, `RewindStarted` / `RewindFinished` 이벤트

### 5-2. 되돌아가는 동안의 공중 동작 (`NGH.RewindFloatBuilder`)
- 메뉴 `Tools/NGH/Build Rewind Float Clip` → `Animations/NGH/NGH_Anim_RewindFloat.anim` (2초 반복), 컨트롤러 상태 `RewindFloat`
- `model@Falling.fbx`(Mixamo)를 그대로 쓰지 않고 **똑바로 선 채 떠 있는 자세**로 변환해 구움
- 최종 자세 (레퍼런스 이미지 기준)
  - 팔: 옆으로 벌리고 **팔꿈치 약 80° 굽혀 L자**, 아래팔은 정면 사선(바깥 약 25°)
  - 다리: 허벅지를 앞으로, 무릎 약 95~100° 굽혀 정강이를 뒤로 접음
  - 원본의 허우적거림을 근육마다 일부 얹음(`FloatSet`의 flail 값)
- 자세는 파일 위쪽 `FloatSet` 값으로 조절 후 메뉴를 다시 실행

### 5-3. 화면 연출
| 연출 | 스크립트 | 내용 |
|---|---|---|
| 기어 | `NGH.RewindGearFx` | `Varco3D/NGH/Gear1~7` 14개가 화면 사방에서 튀어나와 돌다가 빠져나감 (되감기 시간에 맞춤) |
| 황금색 필터 | `NGH.RewindScreenTint` | 역행 중 옅은 황금색 + 채도·노출·비네트 (URP 전역 Volume을 코드로 생성, 카메라 후처리는 연출 동안만 켬) |
| 시계 소리 | `NGH.TimeRewind` | `Audios/NGH/clock tick.wav` 재생, 끝나면 0.25초 페이드아웃 |
| 먹선 궤적 | `NGH.InkTrail` | 아래 5-4 |

### 5-4. 먹선 궤적 (`NGH.InkTrail`, 셰이더 `Shaders/NGH/NGH_Ink.shader`, 머티리얼 `Materials/NGH/NGH_Ink.mat`)
- 리그 오브 레전드 '에코' 잔상처럼 **몸통(척추 뼈 `Spine1`)에서 검은 먹선**이 뻗어 나와 지나온 경로를 따라 남음
- **붓 질감**: 꽉 찬 먹 + 가장자리 붓결 + 안쪽 흰 붓결 틈. 사라질 때 반투명해지지 않고 옅은 붓결부터 깎여 마른 붓 끝처럼 갈라짐 (머티리얼 `_Sharp`)
- 항상 카메라를 향하는 띠, 경로를 6cm 간격으로 다시 나눠 회전할 때 띠가 깨지지 않게
- **싱크**: 먹선 길이 = 시간 역행 기록 길이(5초) → **꼬리 끝 = 실제로 돌아가는 위치** (테스트 오차 0m), `Tail Ink`로 꼬리 진하기 조절
- 발동 시: 5초 궤도가 짙어지며 현재 → 과거로 번짐 → 플레이어가 지나간 만큼 먹선이 빨려 들어감 → 도착 위치에 먹물 덩어리가 모였다 사라짐
- 카메라 바로 앞의 먹은 지워 화면을 가리지 않음
- `Hide Player During Rewind` 기본 꺼짐 (캐릭터가 먹선을 따라 날아가는 모습이 보임)

---

## 6. 프리팹 적용 · Main 씬 정리
- `Prefabs/NGH/NGH_Player.prefab`에 `TimeRewind`, `AttackFx`, `RewindGearFx`, `RewindScreenTint`, `InkTrail` 추가
- `Prefabs/NGH/NGH_PlayerCamera.prefab`에 `CameraShake` 추가
- **Main 씬**: 예전에 따로 붙어 있던 복사본(TimeRewind·AttackFx·GearFx·ScreenTint·CameraShake)이 프리팹과 겹쳐 삭제 → 프리팹 설정 사용 (Main의 증기 소모 0 → 70으로 바뀜)

## 7. Main 체력 · HUD
- Main 플레이어 체력 3 → **100** (chg_Prototype과 같게)
- chg_Prototype의 UI를 Main에 추가: `chg_PlayerHUD`(체력·증기 바, 회복약/환도/테스트 원거리 칸), `NGH_ControlsGuide`(조작 안내)

## 8. 보스맵 플레이어 교체 · 체력 이어가기
- **BossArena_Hyangwonjeong**에 Main의 플레이어 요소를 설정째 복제: `NGH_Player`(스팀건·스팀백·전투 브리지 포함), `NGH_PlayerCamera`, `NGH_GunFx`, `chg_PlayerHUD`, `NGH_ControlsGuide`, `EventSystem`
  - 시작 위치 = `NGH_Spawn_FromMain`, 기존 NMJ 임시 카메라는 끔
  - 보스(`BHS.BossFSM`)가 태그 Player로 새 플레이어를 찾아 공격하는 것 확인
- **체력 이어가기**: `NGH.PlayerCarryOver` (`Scripts/NGH/Portal/PlayerCarryOver.cs`)
  - 포탈(`NGH.ScenePortal`)이 씬 이동 직전에 체력 저장 → 다음 씬 `PlayerController.Awake`에서 적용
  - 테스트: 보스맵 57 → Main 57, Main 41 → 보스맵 41 (포탈 없이 씬을 바로 실행하면 최대 체력)

---

## 9. 스크립트 이름 정리 (namespace) — 사용자 승인으로 전 폴더 대상
- `Scripts` 폴더의 모든 `이니셜_스크립트명`에서 접두사를 지우고 이니셜을 namespace로 사용
  - `NGH_PlayerController` → `NGH.PlayerController`, `CHG_EnemyAI` → `CHG.EnemyAI`, `YPH_SteamTank` → `YPH.SteamTank`, `BHS_BossFSM` → `BHS.BossFSM`
  - 소문자 `chg_` 도 `CHG` namespace로 통일 (예: `chg_PlayerHUD` → `CHG.PlayerHUD`)
  - **NMJ**: 기존 `SteamAttack.Items / .InventorySystem / .Quests / .UI / .PlayerControl / .EditorTools`를 하나의 `NMJ`로 통합, `NMJ_GitHelper` → `NMJ.GitHelper`
- 파일 이름도 클래스 이름과 같게 변경 (.meta 함께 이동 → 씬·프리팹 연결 유지)
- 다른 파트 타입을 쓰는 파일에는 `using NGH;` 등을 자동 추가
- 특이 사항
  - `BHS_4LegWalks`(파일 `BHS_4LegWalk.cs`) → 숫자로 시작할 수 없어 **`BHS.FourLegWalk`** (원래 클래스·파일 이름이 달라 붙일 수 없던 상태도 해결)
  - `BHS_BossAttack.cs`, `BHS_BossAreaAttack.cs` → 파일 이름만 `BossAttack.cs`, `BossAreaAttack.cs`로 맞추고 `BHS`로 감쌈
  - `ObjectPool`, `IPoolable`은 CHG·NGH 양쪽에 있음 → 다른 파트에서 쓸 땐 `CHG.ObjectPool`처럼 전체 이름 필요
  - 파일 인코딩(일부 BHS 파일은 CP949)과 줄바꿈은 원래대로 유지
- 검증: 컴파일 에러 0, 컴포넌트 스크립트 84개 모두 연결, 프리팹 전체 + 씬 6개 Missing Script 0, NMJ 데이터 에셋 14개 정상
- 백업: 작업 전 Scripts 폴더 전체 zip(`Scripts_bk_20261011.zip`, 작업 세션 폴더), git 마지막 커밋 `39ab0a9`

## 10. 콤보별 공격력
- `NGH.PlayerController`의 공격 항목(Light Attack, Light Combo 2~5, Heavy Attack)마다 **Damage** 추가
- 현재 값: 약공격 1~5타 = 1, 강공격 = 3 (기존 무기 값과 같음, 2타는 판정 2번)
- 0 이하로 두면 무기(Weapon)의 Light/Heavy Damage 사용

---

## 11. NGH 폴더 밖 수정 내역 (머지 시 확인)
| 대상 | 내용 |
|---|---|
| `Scenes/Common/Main.unity` | 플레이어·카메라의 겹친 복사본 삭제(§6), 체력 100, `chg_PlayerHUD`·`NGH_ControlsGuide` 추가(§7) |
| `Scenes/Common/BossArena_Hyangwonjeong.unity` | Main 플레이어 요소 복제, NMJ 임시 카메라 끔(§8) — 저장 당시 씬에서 `NMJ_Player`는 이미 빠져 있었음 |
| `Scripts/CHG`, `Scripts/YPH`, `Scripts/BHS`, `Scripts/NMJ` 전체 | 이름·namespace 변경(§9). 로직은 수정하지 않음 |
| `Scenes/CHG/chg_Prototype.unity` | 읽기만 함 (수정 없음) |

## 12. 남은 것 · 주의 사항
- 씬을 열어 둔 채 바꾼 값은 **Ctrl+S로 저장** 필요 (특히 `Scenes/NGH/NGH_AnimationTest.unity`)
- 문자열은 일부러 바꾸지 않음 → 로그 메시지에 `[NGH_TimeRewind]` 같은 옛 이름이 남아 있음 (에셋 경로·리소스 이름이 섞여 있어 바꾸면 깨짐)
- 씬·에셋 파일의 `m_EditorClassIdentifier`에 옛 이름이 남아 있음 → 동작 영향 없음, Unity가 다음 저장 때 갱신
- `.md` 문서들 안의 스크립트 이름은 옛 이름 그대로
- 직접 확인하지 못한 것
  - 시계 소리 페이드아웃 (백그라운드 에디터에서는 시간이 거의 흐르지 않아 끝까지 듣지 못함)
  - 콤보별 공격력의 실제 타격 테스트
- Main의 `GameSystems`(인벤토리·퀘스트)는 보스맵에 옮기지 않음, 증기 게이지는 씬 이동 시 이어지지 않음
- chg_Prototype의 패링(`CHG.PlayerParry`)은 Main에 옮기지 않음
