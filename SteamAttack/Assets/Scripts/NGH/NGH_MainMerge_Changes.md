# Main 씬 병합 — 작업 내역 및 다른 파트 수정 사항

작성: 남귀훈(NGH) · 2026-10-02
대상 씬: `Scenes/Common/Main.unity` (TestPoint 기준 배치)

팀장 요청으로 각 파트 시험 씬의 기능을 Main 씬 하나로 합쳤습니다.
다른 파트 스크립트는 요청서(YPH DevRequests)와 팀장 지시에 따른 부분만 **추가/수정**했습니다. 각 담당자는 머지 시 확인해 주세요.

---

## 1. Main 씬 배치 (TestPoint 기준, 플레이어는 +Z를 봄)

| 위치(TestPoint 기준) | 내용 | 원본 씬 |
|---|---|---|
| (0, 0, 0) | `chg_Player` + `chg_PlayerCamera` (시간 역행, 등의 스팀백, 총·수류탄 연결) | NGH/TimeTest, CHG, YPH |
| 앞 4~6m | 근접 연습 더미 3개(`CHG_MeleeDummies`), 조작 안내(ESC) | CHG/chg_PrefabTest |
| (6, 0, 4) 부근 | YPH 사격 표적 4개(`YPH_TargetRange`) | YPH/YPH_CombatTest |
| (-18, 0, 12) | NGH Enemy (`NGH_EnemyZone`) | NGH/EnemyTest |
| (18, 0, 22) | BHS 보스 (`BHS_BossZone`) | BHS/BHS_BossMk1 |

- 플레이어·카메라를 뺀 나머지는 `Merge_TestArea` 아래에 묶었습니다.
- 스팀 게이지(YPH_SteamGaugeTest)는 플레이어 등에 붙은 스팀백과 같은 것이라 따로 두지 않았습니다.

### 중복 처리
- NMJ 임시 플레이어 `Capsule`, 카메라 `Main Camera (전경)`: **비활성화** (삭제 안 함)
- 각 시험 씬의 바닥·조명·카메라·임시 플레이어: 가져오지 않음
- YPH `Boss_Target`(Boss 태그 표적): 실제 보스와 태그가 겹쳐 가져오지 않음
- `YPH_CombatTestInput`: 요청서대로 Main에는 넣지 않음 (`NGH_CombatBridge`가 대체)
- CHG `chg_TestClub`: 무기 칸에서 빼고 비활성화

---

## 2. 조작 (Main 씬)

| 키 | 동작 |
|---|---|
| WASD / Shift / Space | 이동 / 달리기 / 구르기 (CHG) |
| F | 발도·납도 |
| Tab | 무기 교체: **환도 → 총 → 증기 수류탄** |
| 좌클릭 | 환도: 약공격 / 총: 발사 / 수류탄: 투척 |
| 우클릭 | 환도: 강공격 / 총: 조준(누르는 동안) |
| R | 총 장착 중 탄 주머니 충전 |
| G | 수류탄 제작 (증기 40, 장비 무관) |
| **Q** | 시간 역행 (R과 겹쳐서 R → Q로 변경, Inspector 값만 변경) |
| 휠클릭 | 락온 |

---

## 3. 다른 파트 스크립트 수정 사항

### BHS — `Scripts/BHS/Boss/BHS_BossFSM.cs` (팀장 지시)
- `DamagePlayer(float)` 수정: Player 태그 대상만 공격하고, 실제 플레이어(`chg_PlayerController`)의 `TakeHit(int, Vector3)`로 피해를 줌 (데미지는 반올림, 최소 1)
- 보스 시험 씬용 `BHS_PlayerHitTeleport` / `BHS_PlayerHealth`는 **있을 때만** 호출 (없으면 에러 없이 넘어감)

### CHG — `Scripts/CHG/chg_PlayerController.cs` (YPH 요청서 CHG전투연결 / 조준배율)
- `event Action<chg_Damageable> OnHitLanded` 추가 — 근접 공격이 살아 있던 대상을 실제로 맞힌 직후 호출 (`DoHit`의 `TakeDamage` 바로 다음)
- `event Action<chg_Weapon> ShotStarted` 추가 — 원거리 장비(isMelee = false)로 사격 동작이 시작될 때 호출 (실제 발사·투척은 구독하는 쪽이 처리)
- `public float MoveSpeedMultiplier = 1f` 추가 — 일반 이동 속도에만 곱함 (구르기·공격 이동·중력 제외)

### CHG — `Scripts/CHG/chg_ThirdPersonCamera.cs` (YPH 요청서 조준배율)
- `public float SensitivityMultiplier = 1f` 추가 — 마우스 회전량에 곱함

기본값이 모두 1이고 이벤트는 구독자가 없으면 아무 일도 하지 않으므로, 기존 CHG 씬 동작은 그대로입니다.

### NMJ — `Scripts/NMJ/Quest/QuestManager.cs`, `Scripts/NMJ/UI/QuestTrackerUI.cs` (2026-10-02, 버그 수정 요청)
- 증상: 스크립트를 고친 직후 처음 플레이하면 `QuestTrackerUI.Redraw` NullReferenceException 2건
- 원인: 이 프로젝트는 플레이 진입 시 도메인·씬 리로드를 끈 설정이라, 재컴파일 때 Unity가 `TrackedQuest`(자동 속성)와 `boundInventory`를 직렬화했다가 빈 객체로 되살림 → 데이터가 없는 퀘스트를 추적 중인 것으로 보고 UI가 접근
- `TrackedQuest`에 `[field: NonSerialized]`, `boundInventory`에 `[NonSerialized]` 추가, `BuildInitialStates()`에서 `TrackedQuest = null`
- `QuestTrackerUI.Redraw()`: `tracked.Data != null`일 때만 표시 (방어 코드)
- 같은 원인으로 재컴파일 직후 플레이에서는 수집(Collect) 퀘스트가 인벤토리와 연결되지 않던 문제도 함께 막힘

### YPH — 스크립트 수정 없음
- 씬에서만 연결: 총(`YPH_SteamGunProxy`)은 오른손 총 모델 끝에 배치(YPH 몸체 메시는 숨기고 CHG 총 모델 사용), 수류탄 손 구는 수류탄 무기 칸에, 조준 연출(`YPH_AimEffects`)은 CHG 카메라 자식으로 배치
- CHG 카메라(씬 인스턴스) **후처리 켬**, Volume Layer Mask에 Default 포함
- 총·수류탄의 조준 원점 = CHG 카메라, 무시 대상 = chg_Player, 탱크 = 플레이어 등의 스팀백

---

## 4. NGH 추가/수정

| 파일 | 내용 |
|---|---|
| `Scripts/NGH/NGH_EnemyHealth.cs` (신규) | 적 HP·피해 API (`IsAlive`, `TakeDamage(float, Vector3)`, `Damaged`/`Died`/`Revived` 이벤트). 기본 HP 5, 쓰러지면 AI 정지 → 0.15초 뒤 숨김 → 5초 뒤 스폰 지점에서 부활. 풀에서 다시 꺼낼 때도 초기화 |
| `Scripts/NGH/NGH_EnemyDamageRelay.cs` (신규, 병합용) | YPH 총·수류탄 피해(`YPH_IDamageable`) → NGH_EnemyHealth. CHG 근접은 같은 오브젝트의 `chg_Damageable`을 수신용으로 써서 깎인 만큼 옮김(락온 표시도 이걸로 동작) |
| `Scripts/NGH/NGH_CombatBridge.cs` (신규, 병합용) | CHG 입력·무기 칸 ↔ YPH 총·수류탄·조준 연결 (`YPH_CombatTestInput` 대체). 근접 명중 → 증기 회복 |
| `Scripts/NGH/NGH_EnemyAI.cs` | `StopForDeath()`, `ReviveAtHome()` 추가 |
| `Scripts/NGH/NGH_EnemyAttackHitbox.cs` | 명중한 플레이어의 `chg_PlayerController.TakeHit(데미지, Enemy 위치)` 호출 추가 (구르기·피격 무적이면 피해 없음). `Apply Damage To Player`로 끌 수 있음 |
| `Scripts/NGH/NGH_TimeRewind.cs` | 복원할 때 구르기·발도/납도·피격 중이던 기록도 공격처럼 대기 상태로 바꿈 (역행 후 자동 구르기 수정) |

- CHG 더미 3개: 근접 명중 시 증기가 회복되도록 Main 씬 인스턴스만 `Enemy` 태그로 변경
- YPH 총 효과(`BulletTrail`, `ImpactEnemy`, `ImpactSurface`)를 오른손 뼈 밑에서 씬 루트의 `NGH_GunFx`로 옮김. 이를 위해 Main 씬의 `YPH_SteamGunProxy` 프리팹 인스턴스만 풀었음(프리팹 원본은 그대로). CHG `chg_GroundClamp`가 손 뼈 밑의 효과까지 뼈로 보고 모델을 띄우던 문제 수정

---

## 5. 확인한 것 (플레이 모드)

- 보스 → 플레이어: 공격이 맞으면 CHG HP가 줄고 피격(대) 동작, 두 번째에 사망
- 총: 발사 → 표적 HP -1, 증기 +10 / 6발 중 4발로 Enemy 처치 → 5초 뒤 부활
- 조준: FOV 60 → 48, 감도 배율 0.6·이동 배율 0.5가 CHG 카메라·플레이어에 전달, 해제 시 1로 복구
- 수류탄: G 제작(증기 -40, 소지 1) → 수류탄 칸에서 손에 구 표시 → 좌클릭 투척
- 근접: 더미 명중 → 증기 +10 / Enemy 명중 → NGH HP 감소 + 증기 +10
- Enemy 공격: 명중 시 플레이어 HP 3 → 2, 피격(소) 동작 (2026-10-02 추가)
- 콘솔 에러 없음 (원래 시험 씬들은 변경 없음)

## 6. 아직 연결 안 된 것

- **플레이어 → 보스 피해**: `BHS_BossHealth`가 CHG 근접·YPH 총 피해 방식과 연결되어 있지 않음
- 수류탄 투척은 CHG의 사격(Shot) 동작을 그대로 사용
- Player/Enemy 물리 레이어 지정(YPH 요청서 전투통합순서와레이어)은 하지 않음 — 팀장 결정 필요


## 7. 버그 수정 내역 및 남은 버그 (2026-10-02)

- 해결: Enemy 공격 피해 / 시간 역행 후 자동 구르기 / 총 사용 후 모델이 뜨는 문제 / 재컴파일 후 퀘스트 UI NullReference
- 남음: 수류탄 투척 시 사격 모션 (던지기 애니메이션 필요)
- 남음: 보스에 끼임 — 보스 등껍질·다리 콜라이더가 Rigidbody 없이 움직여 플레이어에 파고듦, 보스 Character Controller가 몸체보다 작음 (BHS 확인 필요)
- 참고: 퀘스트 UI 글꼴(LiberationSans SDF)에 한글이 없어 □로 표시됨 (NMJ 확인 필요)