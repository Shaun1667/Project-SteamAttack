# Main 2차 병합 작업 내역 (NGH, 2026-10-10)

사용자 지시로 진행한 작업입니다. NGH 폴더 밖 수정은 아래 '다른 파트 수정 내역'에 모두 적었습니다.

## 1. Main 플레이어 교체 (Scenes/Common/Main.unity)
- 기존 `NGH_Player`, `NGH_PlayerCamera`를 지우고 `Scenes/NGH/NGH_AnimationTest`의 것을 오버라이드째 복제해 같은 위치에 배치.
  - 플레이어: NGH_AttackFx(칼 궤적 이펙트), NGH_RewindGearFx, NGH_RewindScreenTint, NGH_TimeRewind 포함
  - 카메라: NGH_CameraShake 포함
- Main에만 있던 병합 요소는 새 플레이어로 옮김 (오브젝트 그대로 이동, 설정 유지)
  - 오른손: `YPH_SteamGunProxy`, `YPH_GrenadeWeapon`
  - 루트: `NGH_CombatBridge` (CHG_CombatBridge, YPH_SteamHitRefill, YPH_GrenadeHand)
  - 카메라: `YPH_AimEffects`
- NGH_WeaponHolder 무기 칸은 Main 기준 3칸(환도, 총, 수류탄) 유지
- 스팀백은 AnimationTest 쪽 `YPH_SteamBackpack`을 사용. 총·수류탄·전투 브리지의 탱크 참조는 새 스팀백으로 재연결(15개, 실패 0)
- 시간 역행: 키 Q, 3초 (통합 기획서 기준)

## 2. 적 배치 (Main)
- `Merge_TestArea/CHG_SoldierZone`에 chg_Prototype의 `chg_Soldier`(도끼), `chg_Soldier2`(소총)를 오버라이드째 복제
  - 이름: chg_Soldier1, chg_Soldier2 / 위치 (2.5, 0, 190.8) 부근, 플레이어 시작 지점 쪽을 바라봄

## 3. 포탈
- 새 스크립트: `Scripts/NGH/Portal/NGH_ScenePortal.cs`, `NGH_PortalSpawnPoint.cs`
  - 플레이어가 트리거 영역에 들어가면 씬 이동, 도착 씬의 같은 ID 스폰 지점으로 플레이어를 옮김
  - 씬 시작 후 1초간은 발동 안 함
- Main: `Merge_TestArea/NGH_Portal_ToBossArena` (8.5, 0, 168.8) -> BossArena_Hyangwonjeong / 도착 `NGH_Spawn_FromBossArena` (8.5, 0, 172.3)
- BossArena: `NGH_PortalArea/NGH_Portal_ToMain` (0, 0, -16) -> Main / 도착 `NGH_Spawn_FromMain` (NMJ_PlayerSpawn_FogGate 위치)
- 재질: `Materials/NGH/NGH_M_PortalGate.mat`, `NGH_M_PortalGlow.mat`

## 4. 보스 배치 (Scenes/Common/BossArena_Hyangwonjeong.unity)
- BHS_BossMk1의 `BHS_HyunMu`를 오버라이드째 복제해 `NMJ_BossSpawn_Hyeonmu` 위치(0, 0.1, 3)에 배치, 입구 쪽을 바라봄

## 다른 파트 수정 내역 (머지 시 확인)
- `Scenes/Common/Main.unity`: 위 1~3
- `Scenes/Common/BossArena_Hyangwonjeong.unity`: 위 3~4 (NMJ 오브젝트는 수정하지 않음)
- `ProjectSettings/EditorBuildSettings.asset`: 빌드 씬 목록에 BossArena_Hyangwonjeong 추가 (포탈의 씬 이동에 필요)
- CHG·BHS·NMJ·YPH 스크립트와 프리팹 원본은 수정하지 않음

## 남은 것
- 플레이 모드에서 포탈 왕복, 병사 전투, 보스 전투는 아직 직접 확인하지 않음
- BossArena의 플레이어는 NMJ의 임시 캡슐(NMJ_Player)이라 전투 시스템이 없음. Main 플레이어를 보스맵에서도 쓰려면 별도 작업 필요
- chg_Prototype 플레이어에 있던 chg_PlayerParry, chg_PlayerHUD는 이번 범위에 넣지 않음
