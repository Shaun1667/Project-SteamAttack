# 가이드: YPH 작업을 Main 씬에 적용하기 (병합 후)

| 항목 | 내용 |
|---|---|
| 한 줄 설명 | 병합본의 `Main` 씬에 아직 들어가지 않은 YPH 작업(스카이박스·조준 머리 구도·포졸 길찾기·새 궁수 등)을 Main에서 켜는 순서 |
| 담당자 | YPH |
| 사용한 AI | Claude |
| 작성일 | 2026-10-23 |
| 기준 병합본 | `origin/main` = `2f8eff2` (2026-10-23 13:19, 모든 팀 브랜치가 같은 지점) |
| 관련 기획서 | [원거리 무기](../Design/전투/원거리무기.md) · 기준 결정: D-102 |
| 상태 | 일부 완료(절차 정리만 끝남. Main에서 실제로 적용·Play 확인한 것은 없음, 14번 참고) |
| 대상 플랫폼 | PC |

이 문서는 기능 문서 양식(15칸) 대신 **적용 순서 목록**으로 썼습니다. 새 기능이 아니라 이미 만든 기능을 공용 씬에 켜는 일이라서입니다. 기능 자체의 설명은 각 기능 문서로 링크합니다.

## 1. 한 줄 결론

**프리팹으로 들어간 것은 병합만으로 Main에 이미 적용됐고, 씬 파일이 직접 저장하는 설정은 씬을 열어 한 번씩 해야 합니다.**

비유하면 이렇습니다. `Main`은 모두가 쓰는 거실이고, YPH 작업은 내 방에서 만든 물건입니다. 가구(프리팹: 포졸, 화승총, 증기 탱크)는 설계도를 거실에 놓기만 하면 설계도가 바뀔 때 같이 바뀝니다. 하지만 벽지(스카이박스), 조명 스위치 연결(카메라 연결), 바닥 지도(길찾기 데이터)는 거실 안에 직접 적혀 있어서, 설계도를 합쳐도 따라오지 않습니다.

기술적으로는 `.unity` 씬이 `RenderSettings`, 카메라 설정, 씬 오브젝트끼리의 참조, 베이크한 NavMesh 데이터를 자기 파일에 저장하기 때문입니다. 프리팹 변경은 씬 안의 인스턴스에 전파되지만 이 값들은 전파되지 않습니다.

## 2. 지금 Main 상태

병합본 `2f8eff2`의 `Main.unity`·프리팹 파일을 YAML·GUID로 대조한 결과입니다(정적 확인, Play 미실행).

### 2-1. 아직 Main에 없는 것

| # | 항목 | Main의 지금 상태 | 해야 할 일 | 고치는 곳 · 소유 | 절차 |
|---|---|---|---|---|---|
| 0 | 병합 직후 준비 | `FinalPlayer.fbx`가 LFS 포인터(134바이트 글자 파일)로 들어옴. Magica Cloth 2는 git에서 빠짐 | LFS로 FBX 받기, Magica Cloth 2 설치 | 각자 PC | 3번 |
| 1 | 스카이박스 | 하늘 재질 없음(`m_SkyboxMaterial: 0`), 전경 카메라 배경이 단색, 안개판 4개가 하늘을 가림 | 하늘 재질 지정, 카메라 배경을 Skybox로, 안개판 재질 4개 교체 | `Main.unity` · NGH | 4번 |
| 2 | 조준 머리 구도 | 거리 연결(`_orbitCamera`)은 이미 되어 있음. **머리 연결(`_head`)이 없음** | `Head` 칸에 머리 뼈 연결 | `Main.unity` · NGH | 5번 |
| 3 | 포졸 길찾기 | `YPH_Navigation`의 베이크 데이터가 NMJ 맵 v2(12:49)보다 먼저 만든 것(11:11)이고, `Use Geometry`가 YPH 기준(Physics Colliders)과 다름 | 다시 굽기 | `Main.unity` · NGH | 6번 |
| 4 | 새 궁수 | `NMJ_EnemySpawns`의 궁수 2마리가 옛 `YPH_HeuksuArcher`. 새 모델 `YPH_HeuksuArcher_Rigged`는 쓰는 곳 없음 | 두 마리를 새 프리팹으로 교체 | `NMJ_EnemySpawns.prefab` · NMJ | 7번 |
| 5 | 옛 적 1마리 | `Main` 안에 옛 `Enemy` 오브젝트 1개(위치 0,1,0)가 남아 있음 | 삭제하거나 포졸 프리팹으로 교체 (요청 진행 중) | `Main.unity` · NGH | 8번 |
| 6 | 새 총 이펙트 | 총구 섬광·명중 효과가 옛 버전 | 레퍼런스 버전으로 연결 | `NGH_Player.prefab`, `Main.unity`의 `NGH_GunFx` · NGH | 9번 |

### 2-2. 병합으로 이미 Main에 들어간 것 (할 일 없음)

| 항목 | 어떻게 들어갔나 | 확인한 근거 |
|---|---|---|
| 화승총·발사 증기·명중 회복 | `NGH_Player.prefab` 안 `SteamGun`·`SteamHitRefill` | 스크립트 GUID가 프리팹에 있음 |
| 증기 탱크·스팀백 | `NGH_Player.prefab`, `YPH_SteamBackpack` | 같음 |
| 받아치기 | `NGH_Player.prefab` 안 `ParryController` | 같음 |
| 새 플레이어 모델(`FinalPlayer`)과 치마 접촉 | `NGH_Player.prefab`이 `FinalPlayer.fbx`를 가리킴 | 프리팹에 FBX GUID 15곳. 0번 준비가 필요 |
| 조준 연출 | `Main`의 `YPH_AimEffects` 인스턴스. 줌(0.8)·집중선·거리(0.92m) 값은 프리팹 값 | 프리팹 값 직접 확인. 딤드는 꺼지고 붓 조준(집중선)이 켜진 상태 |
| 조준 카메라 거리 연결 | `_orbitCamera`가 `NGH_PlayerCamera`의 카메라 컴포넌트에 연결됨 | `Main`·`NMJ_Main`·`BossArena` 세 씬 모두 확인 |
| 변이 포졸 6마리 | `NMJ_EnemySpawns`가 `YPH_MutantSoldier`를 씀 | 인스턴스 6개 |
| 허수아비 | `Main`의 `YPH_TargetDummy` 4개 | 인스턴스 확인 |

### 2-3. 적용 대상이 아닌 것

`YPH_HeroPlayerIntegration`(옛 히어로 통합 프리팹), `YPH_SteamGrenade`(수류탄은 범위 제외), `YPH_TemporaryPlayerHUD`(NMJ HUD로 교체됨), Blender 리깅 작업(`Docs/Features`의 Archer·Tassel 리깅)은 Main에 넣지 않습니다.

## 3. 먼저 할 일 (0번): 병합 직후 각자 PC

### 3-1. FinalPlayer.fbx 받기 (Git LFS)

병합본에서 `FinalPlayer.fbx`는 **Git LFS 포인터**입니다. 파일을 열어 보면 `version https://git-lfs...`로 시작하는 짧은 글자만 있고(134바이트), 실제 모델(308,193,324바이트)은 LFS 서버에 있습니다. 이 포인터 설정(`.gitattributes`)은 `5e0985c`에서 병합 담당(`Shaun1667`)이 넣었습니다.

1. LFS가 처음이면 한 번만: `git lfs install`
2. pull 또는 병합 뒤 실제 파일을 받기: `git lfs pull`
3. 크기가 **308,193,324바이트**인지 확인한다. 134바이트면 아직 못 받은 것이다.
4. Unity를 열어 플레이어 몸이 보이는지 확인한다.

YPH PC만 해당: 로컬에 진짜 파일이 이미 있어서 pull이 "덮어쓰게 된다"며 멈출 수 있습니다. 로컬 파일의 SHA-256 해시가 포인터의 `oid`(`9d01f247…1750`)와 같다는 것을 확인했으니, 파일을 프로젝트 밖으로 옮기고 pull한 뒤 `git lfs pull`로 받으면 됩니다.

[FinalPlayer모델전달.md](FinalPlayer모델전달.md)의 "git에 올리지 말고 직접 전달"은 이 변경 전 내용입니다(13번 참고).

### 3-2. Magica Cloth 2 설치 (유료 에셋, git에 없음)

`ec14bb6`에서 병합 담당(`Shaun1667`)이 `Assets/Scripts/YPH/ThirdParty/`를 `.gitignore`에 넣어 **Magica Cloth 2가 저장소에서 빠졌습니다.** 그런데 `Scripts/YPH/Character/SkirtBodySurfaceContact.cs`가 `using MagicaCloth2;`를 쓰고, `NGH_Player.prefab`·`FinalPlayer.prefab`에도 Magica 컴포넌트가 있습니다.

**설치하지 않으면 프로젝트 전체가 컴파일 오류로 Play를 못 합니다.** (`SkirtBodySurfaceContact.cs`가 `Assembly-CSharp`에 들어 있기 때문입니다.)

1. Unity Asset Store의 내 에셋에서 Magica Cloth 2를 가져온다(구매한 계정 필요).
2. `Assets/Scripts/YPH/ThirdParty/MagicaCloth2/` 폴더에 둔다. 다른 곳에 두면 git이 새 폴더로 보고 실수로 커밋할 수 있다. Unity Project 창에서 옮기면 GUID가 유지된다.
3. 콘솔에 컴파일 오류가 없는지 확인한다.

## 4. 스카이박스를 Main에 적용 (1번)

결과: 청록·남색 붓결 구름과 별이 보이고 천천히 흐릅니다. 하늘 재질은 밝기 0.42, 채도 0.35, 회전 속도 0.15도/초입니다. 기능 설명 문서(`YPH_기능_20261021_설경스카이박스.md`)는 YPH 로컬 `Docs/Features/`에만 있고 저장소에는 없습니다.

`Main`은 NGH 소유이므로 **한 사람만** 열어 작업하고 저장·push합니다.

1. `Assets/Scenes/Common/Main.unity`를 연다.
2. **하늘 재질:** 메뉴 `Window > Rendering > Lighting` → `Environment` 탭 → **Skybox Material** 칸에 `Assets/Materials/YPH/YPH_SnowDusk_Painterly.mat`를 끌어다 놓는다.
3. **카메라 배경:** Hierarchy에서 `Main Camera (전경)`을 고른다. Inspector의 Camera에서 **Background Type**(옛 이름 Clear Flags)을 `Solid Color`에서 **`Skybox`** 로 바꾼다. (`NGH_PlayerCamera`의 카메라는 이미 Skybox입니다.)
4. **안개판 재질 4개 교체:** Hierarchy 검색창에 `안개`를 입력한다. 아래 오브젝트의 Mesh Renderer > Materials > Element 0을 바꾼다.

   | 오브젝트 | 지금 재질 | 바꿀 재질 (`Assets/Materials/YPH/`) |
   |---|---|---|
   | `인왕산 안개` | `M_FX_Mist` | `YPH_M_OverheadMist_OneSided` |
   | `북악산 산허리 안개` | `M_FX_Mist` | `YPH_M_OverheadMist_OneSided` |
   | `북악산 산허리 안개 2` | `M_FX_Mist` | `YPH_M_OverheadMist_OneSided` |
   | `현무 발치의 먹안개` | `M_FX_MistInk` | `YPH_M_InkMist_TopOnly` |

   `도성 바닥 안개`와 `연못 물안개`는 그대로 둡니다. YPH 시험 씬도 그 둘은 바꾸지 않았습니다.
5. 저장(`Ctrl+S`).
6. 같은 구조인 `NMJ_Main.unity`에도 필요하면 같은 방법으로 합니다(NMJ 소유). `BossArena_Hyangwonjeong`은 안개 설정이 달라서(`Exponential Squared`, 카메라 시야 거리 300m) 이 가이드 대상이 아닙니다. 하늘을 넣을지 따로 정합니다.

**왜 안개판을 바꾸나:** 기존 안개판은 양면이라 아래에서 올려다보면 하늘을 가립니다. YPH 재질은 위에서만 보이고 아래에서는 사라지게 만든 복사본입니다. NMJ 원본 재질은 건드리지 않습니다.

**주의**
- NMJ의 `HyeonmuWorldBuilder`를 다시 실행하면 하늘을 `null`로 초기화합니다. 적용 뒤에는 실행하지 말고, 실행했다면 2번을 다시 합니다.
- 기본 반사 소스가 하늘이라 하늘이 바뀌면 금속(총·창)의 반사색이 달라질 수 있습니다. 어색하면 Lighting 창에서 반사를 확인합니다.
- 성능은 미측정입니다(하늘 픽셀당 텍스처 4번 읽음).

## 5. 조준할 때 카메라가 머리 쪽으로 가는 구도 (2번)

**먼저 바로잡을 것:** 이전에 쓴 [조준카메라거리_병합적용.md](조준카메라거리_병합적용.md)는 "Main에는 카메라 거리 연결이 비어 있다"고 했지만 **틀렸습니다.** 병합본에서는 `Main`·`NMJ_Main`·`BossArena` 세 씬 모두 `Aim Zoom View`의 `Orbit Camera`가 `NGH_PlayerCamera`의 카메라 컴포넌트에 연결돼 있습니다. 그래서 **조준하면 카메라가 플레이어에게 다가오는 부분(0.92m)은 Main에서도 동작할 것**으로 봅니다(Play 미실행).

Main에 없는 것은 **머리 연결(`Head`)** 입니다. YPH 시험 씬에는 있어서 머리가 화면 왼쪽 아래(0.1, 0.45)에 오도록 카메라가 옆·위로도 움직이고, Main에는 없어서 거리와 시야각만 변합니다.

`Main` 소유는 NGH입니다.

1. `Assets/Scenes/Common/Main.unity`를 연다.
2. Hierarchy에서 `NGH_PlayerCamera` 아래 `YPH_AimEffects`를 고른다.
3. Inspector에서 `Aim Zoom View`의 **Head** 칸을 찾는다.
4. Hierarchy에서 **`NGH_Player`를 펼쳐** 그 안의 `mixamorig:Head`를 Head 칸에 끌어다 놓는다. 검색하면 적(포졸·궁수)의 `mixamorig:Head`도 나오니 반드시 `NGH_Player` 아래의 것을 고른다.
5. 저장한다. **Inspector의 `Overrides > Apply All`은 누르지 않는다.** 머리 뼈는 씬 안 오브젝트라 프리팹에 저장되지 않고, YPH 소유 프리팹이 의도하지 않게 바뀔 수 있다.
6. `NMJ_Main`, `BossArena_Hyangwonjeong`도 같은 방법이다(각각 NMJ, NGH 소유).

**값** (프리팹 기본값, 코드 기본값 아님): `Aim Distance` 0.92m, `Head Viewport` (0.1, 0.45), `Aim Fov Multiplier` 0.8, 조준 진입 0.3초·해제 0.2초. 기획서는 이 줌 수치를 "미정"으로 두었으니 시험값입니다.

**씬마다 연결하지 않아도 되게 하는 방법(제안):** `AimZoomView`가 `Orbit Camera`·`Head`가 비어 있을 때 스스로 찾게 하면, 병합만으로 모든 씬에 적용되고 NGH가 씬을 고칠 필요가 없습니다. 예를 들어 부모에서 `NGH.ThirdPersonCamera`를 찾고, 그 `target`의 `Animator`에서 머리 뼈를 얻는 방식입니다. 아직 만들지 않았고 머리 뼈 찾기는 검증하지 않았습니다. 코드 변경이라 계획서를 먼저 쓰고 GPT가 구현합니다.

## 6. 포졸 길찾기 다시 굽기 (3번)

**왜:** 포졸 프리팹은 `_useNavMesh`가 켜져 있어서 NavMesh가 없는 곳에서는 **멈춥니다.** `Main`의 `YPH_Navigation`은 11:11에 구웠는데(`5e0985c`), NMJ 맵 v2(`e27e367`, 12:49)와 `NMJ_LevelDressing`이 그 뒤에 들어왔습니다. 길이 달라졌다면 포졸이 엉뚱한 곳에서 멈춥니다. 또 `Main`의 Surface는 `Use Geometry`가 Render Meshes인데, YPH 시험 씬은 Physics Colliders입니다.

굽는 사람은 **한 명**이어야 합니다. 결과 파일이 이진 파일이라 두 사람이 구우면 병합이 안 됩니다.

1. `Assets/Scenes/Common/Main.unity`를 연다.
2. Hierarchy에서 `YPH_Navigation`을 고른다.
3. Inspector의 `NavMesh Surface`에서 확인한다: `Agent Type` Humanoid, `Collect Objects` All Game Objects, **`Use Geometry` Physics Colliders**.
4. **Bake**를 누른다. 결과는 `Assets/Scenes/Common/Main/NavMesh-YPH_Navigation.asset`에 덮어쓰인다.
5. 굽기 전에 움직이는 몸(플레이어·적)이 바닥에 구멍을 내지 않는지 본다. 움직이는 몸에는 `NavMeshModifier`의 `Ignore From Build`를 켜 둡니다([적AI.md](적AI.md)). `NGH_Player`, `YPH_MutantSoldier`, `YPH_HeuksuArcher_Rigged`, `Main`의 옛 `Enemy`에 `NavMeshModifier`가 있고(켜짐 여부는 `Enemy`만 확인함), **옛 `YPH_HeuksuArcher`에는 없습니다.** 그래서 7번으로 궁수를 교체한 뒤에 굽습니다.
6. 저장한다. 에이전트 크기(높이 0.6, 오르기 0.3)는 `ProjectSettings/NavMeshAreas.asset`에 이미 있습니다. [적AI.md](적AI.md)는 포졸 에이전트 높이(2m)와의 차이로 낮은 통로의 경로가 어긋날 수 있다고 적었으니, 낮은 통로가 있으면 Play에서 따로 봅니다.

## 7. 새 궁수로 교체 (4번)

옛 궁수와 새 궁수의 차이: 새 `YPH_HeuksuArcher_Rigged`는 새 모델·활·손 화살·왼쪽 허리 화살통을 쓰고 `NavMeshModifier`가 있습니다. **둘 다 `_holdPosition`이 켜져 있어 제자리에서 쏩니다.** 기획서에 궁수 이동 규정이 없는 것과 맞습니다.

이 교체는 `NMJ_EnemySpawns.prefab`(NMJ 소유)을 고쳐야 해서 NMJ에게 요청 문서가 필요합니다. 적용하는 사람의 순서입니다.

1. `Assets/Prefabs/NMJ/NMJ_EnemySpawns.prefab`을 연다(Prefab Mode).
2. 궁수는 두 곳에 있습니다. 위치와 회전을 적어 둔다.

   | 부모 | 이름 | 로컬 위치 | Y 회전 |
   |---|---|---|---|
   | `광화문` | `흑수회궁수_광화문` | (-15.5, 3.05, -54) | 135° |
   | `육조거리` | `흑수회궁수_육조` | (13, 3.1, -76) | 200° |

3. 옛 궁수 두 개를 지운다.
4. 같은 부모 아래에 `Assets/Prefabs/YPH/Enemy/YPH_HeuksuArcher_Rigged.prefab`을 넣고 이름·위치·회전을 위 값으로 맞춘다.
5. 저장한다. **Main의 NavMesh 굽기(6번)는 이 뒤에** 합니다.

방법을 하나 더 생각해 봤지만 권하지 않습니다. 옛 프리팹(`YPH_HeuksuArcher.prefab`)의 내용을 새 것으로 덮으면 파일은 YPH 것이라 NMJ를 안 거쳐도 되지만, `NMJ_EnemySpawns`의 위치 덮어쓰기가 옛 프리팹 안의 변환 번호(fileID)를 가리켜서 번호가 달라지면 궁수가 원점으로 가 버릴 수 있습니다.

## 8. 옛 `Enemy` 오브젝트 정리 (5번)

`Main`에 이름이 `Enemy`인 오브젝트가 있습니다(위치 0,1,0, `EnemyAI`·`EnemyMotor` 붙음). YPH가 `EnemyAI`를 역할별 컴포넌트로 나누기 전 설정으로 저장된 적입니다. NGH에게 보낸 [요청](../Requests/진행/NGH_요청_20261015_기존EnemyAI적교체.md)이 아직 진행 상태입니다. 삭제하거나 `Prefabs/YPH/Enemy/YPH_MutantSoldier.prefab`으로 바꾸면 됩니다. 정식 배치는 `NMJ_EnemySpawns`가 하므로 삭제가 단순합니다.

## 9. 새 총 이펙트 (6번)

이 항목은 **우선순위가 낮고 현재 구조로는 검증하지 못했습니다.** 근거는 [총이펙트.md](총이펙트.md)입니다.

- 총구 섬광: 옛 3개 레이어(`Flash`·`Steam`·`Sparks`, 재질 `YPH_M_FxFlash`·`FxSmoke`·`FxSpark`)를 씁니다. 지금은 `NGH_Player.prefab` 안의 `YPH_SteamGunProxy` 복사본에 있습니다(프리팹 연결이 끊긴 복사본이라 이펙트 프리팹을 고쳐도 따라오지 않음).
- 명중·궤적: `Main` 씬 루트의 `NGH_GunFx`가 옛 버전입니다.
- 바꾸는 방법(문서 기준): `GunMuzzleFlashView._effects`를 `YPH_SteamRifle/Muzzle/ParryFlash`의 5개로, `GunImpactView`의 두 칸을 `YPH_VFX_ImpactEnemy`·`YPH_VFX_ImpactSurface` 인스턴스로 바꿉니다.
- 받아치기 섬광은 이미 새 버전입니다.

`NGH_Player.prefab`은 D-083의 절차(YPH 폴더로 옮겨 작업하고 되돌림)로 YPH가 고칠 수 있고, `NGH_GunFx`는 `Main` 씬이라 NGH가 합니다. 총구 모양을 바꿀지는 먼저 정하는 것이 좋습니다(13번).

## 10. 적용 뒤 Play 확인 (Main)

| # | 하는 일 | 정상이라면 보이는 것 |
|---|---|---|
| 1 | Console을 비우고 Play한다 | 컴파일·Missing 오류 없음. 플레이어 몸·치마가 보임 |
| 2 | 하늘을 올려다본다. 20초쯤 지켜본다 | 청록 구름과 별. 안개판이 하늘을 가리지 않고 구름이 조금 움직임 |
| 3 | `F` → `Tab`으로 화승총, 우클릭을 누르고 있는다 | 카메라가 다가오고 머리가 화면 왼쪽 아래 쪽에 옴. 놓으면 0.2초 만에 복구 |
| 4 | 벽을 등지고 조준한다 | 카메라가 벽 안으로 파고들지 않음 |
| 5 | 육조거리로 가서 포졸에게 다가간다 | 포졸이 길을 따라 추적하고 벽에서 멈추지 않음 |
| 6 | 궁수 지붕·해태 뒤에서 | 새 모델 궁수가 제자리에서 2.5초 간격으로 쏨. 화살 8 |
| 7 | 원점(0,1,0) 근처를 본다 | 옛 `Enemy`가 없음 |

## 11. 누가 무엇을 하나

| 할 일 | 파일 | 소유 | 요청 |
|---|---|---|---|
| 0번 준비 | 각자 PC | 각자 | 필요 없음(팀에 공지) |
| 1·2·3·5번 | `Main.unity` | NGH | 요청 문서 필요(1·2·3번은 아직 없음, 5번은 진행 중) |
| 4번 | `NMJ_EnemySpawns.prefab` | NMJ | 요청 문서 필요 |
| 6번 | `NGH_Player.prefab`, `Main.unity` | NGH | 결정 후 |
| `NMJ_Main`·`BossArena` | 각 씬 | NMJ·NGH | 같은 방법 |

요청 문서 양식은 [요청문서_템플릿.md](../Templates/요청문서_템플릿.md)입니다. 씬은 한 사람만 고치므로 NGH 요청은 하나로 묶는 편이 낫습니다.

## 12. 위험과 반대 의견

- **병합본이 이미 push된 뒤입니다.** 씬 수정은 병합본 위에서 한 사람이 하고 바로 push해야 합니다. YPH 브랜치에서 `Main`을 고치면 NGH 쪽 변경(`Main`은 오늘만 6번 바뀜)과 충돌합니다.
- **3-2의 컴파일 오류가 가장 급합니다.** 유료 에셋을 가진 사람만 Play할 수 있는 상태입니다. 모두가 설치하는 것이 지금 정책이지만, 설치가 어려운 사람이 있으면 `SkirtBodySurfaceContact.cs`를 Magica가 없을 때 컴파일에서 빠지게 분리하는 방법이 있습니다(코드 변경, 계획서 필요).
- **옛 문서가 어긋납니다.** `조준카메라거리_병합적용.md`(연결이 비었다는 틀린 내용)와 `FinalPlayer모델전달.md`(LFS 이전 내용)입니다. 13번에서 정리합니다.
- **하늘을 넣으면 분위기가 달라집니다.** 하늘 색감과 흐르는 속도는 사용자의 직접 요청으로 정한 시험값이고, 기획서·결정 로그에서 근거는 확인하지 못했습니다. 공용 씬에 넣기 전에 팀이 보고 정하는 편이 안전합니다.
- **확인한 범위에서 찾지 못한 것:** 궁수 교체로 `NMJ_EnemySpawns`의 다른 연결이 깨지는지는 열어 보지 못했습니다.

## 13. 알려진 문제와 앞으로 할 일

| 구분 | 내용 | 영향 | 해결 방향 |
|---|---|---|---|
| 결정 필요 | 머리 구도(`Head`)를 Main에 쓸지 | 거리만 쓰면 5번은 할 일 없음 | 정하면 5번 진행 |
| 결정 필요 | 카메라 연결을 씬마다 할지, 코드가 찾게 할지 | 새 씬마다 연결을 빠뜨릴 수 있음 | 코드가 찾게 하는 쪽 추천, 계획서 |
| 결정 필요 | 새 총 이펙트를 Main에 쓸지 | 총구·명중이 옛 모양 | 정하면 9번 진행 |
| 수정 필요 | `조준카메라거리_병합적용.md`의 "연결이 비어 있음" | 읽는 사람이 이미 있는 연결을 또 넣으려 함 | 이 문서 기준으로 고침 |
| 수정 필요 | `FinalPlayer모델전달.md`의 "git에 올리지 않기" | LFS와 반대 지시 | 상단에 갱신 표시를 넣음 |
| 미실행 | 이 가이드의 모든 절차를 Main에서 실제로 해 보지 않음 | 메뉴 이름·값이 다를 수 있음 | 적용한 사람이 10번 시나리오로 확인하고 14번을 갱신 |

## 14. 검증 상태

| 확인 | 결과 | 방법 |
|---|---|---|
| 병합본 `2f8eff2`에 내 마지막 커밋 포함 | 확인 | `git merge-base --is-ancestor` |
| `Main` 하늘 재질·카메라 배경·안개판 재질 | 확인(없음) | `Main.unity` YAML |
| 세 씬의 `_orbitCamera` 연결 | 확인(있음), `_head`는 없음 | 씬 YAML, 연결 대상이 `NGH_PlayerCamera`의 카메라 컴포넌트 |
| 포졸·궁수 프리팹의 길찾기·제자리 설정 | 확인 | 프리팹 YAML(`_useNavMesh`, `_holdPosition`, `NavMeshModifier`) |
| FBX 로컬 파일과 LFS 포인터 일치 | 확인 | SHA-256 비교 |
| Unity에서 열기·Play·베이크 | **미실행** | 정적 확인만 함 |

## 15. 변경 이력

| 날짜 | 작성·수정 | 바뀐 내용 | 커밋 |
|---|---|---|---|
| 2026-10-23 | YPH(Claude) | 최초 작성. 병합본 `2f8eff2` 기준 | 미커밋 |
