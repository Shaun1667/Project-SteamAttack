# 요청: NMJ HUD를 지금 공용 플레이어(`NGH_Player`)에 연결하고 받아치기 신호를 새 코드로

| 항목 | 내용 |
|---|---|
| 요청일 | 2026-10-16 |
| 요청자 | YPH |
| 받는 사람 | NMJ |
| 우선순위 | 🟡 보통 |
| 희망 기한 | 2026-10-18 |
| 관련 기획서 | `Design/UI/인게임UI.md` (D-086), `Design/전투/패링.md` (D-065, D-082), `Design/00_프로젝트.md` (D-081, D-083) |
| 상태 | 요청됨 |

## 1. 한 줄 요약

NMJ HUD(붓글씨 체력바, 증기 실린더)는 지금 `YPH_HeroPlayerIntegration`에 붙어 있는데, 팀 Main의 플레이어는 이제 `NGH_Player`입니다. NGH 브랜치를 먼저 병합한 뒤 HUD를 `NGH_Player`에 붙이고, 받아치기 성공 신호를 `YPH.ParryController`에서 받아 주세요.

## 2. 대상 파일

| 경로 | 종류 (스크립트/씬/프리팹/설정/기타) | 수정 방식 (추가/변경/삭제) |
|---|---|---|
| NMJ 브랜치 (NGH 브랜치 병합) | 기타 | 병합 |
| `Assets/Prefabs/NGH/NGH_Player.prefab` | 프리팹 | NMJ HUD 2개 추가, 연결 |
| `Assets/Scripts/NMJ/UI/SteamCylinderGauge.cs` | 스크립트 | 받아치기 신호 구독 변경 |

## 3. 왜 필요한가

- NMJ 브랜치는 2026-10-13(`8b22fde`) 기준입니다. 그 뒤 NGH가 플레이어를 `NGH_Player` 프리팹으로 바꿨고(10/14 `10a4c1a`), Main·BossArena도 `NGH_Player`를 씁니다. 받아치기 작업도 이 프리팹에서 합니다(D-081, D-083)
- NMJ 브랜치의 Main은 아직 `YPH_HeroPlayerIntegration`을 씁니다. 그래서 그대로 병합하면 Main 충돌이 나고, NGH 쪽 Main을 고르면 **NMJ HUD가 화면에 나오지 않습니다**
- `SteamCylinderGauge`는 `NGH.PlayerParry.ParryAttempted`(정적 이벤트)를 듣습니다. 이 옛 코드는 NGH가 지웁니다(요청 `받아치기공통규약적용`)

## 4. 현재 상태 → 원하는 상태

| 항목 | 현재 (NMJ 브랜치) | 원하는 모습 |
|---|---|---|
| HUD가 붙은 플레이어 | `YPH_HeroPlayerIntegration` | `NGH_Player` |
| Main의 플레이어 | `YPH_HeroPlayerIntegration` | `NGH_Player` (NGH 브랜치 그대로) |
| 받아치기 신호 | `NGH.PlayerParry.ParryAttempted += HandleParry(PlayerParry, bool)` | `YPH.ParryController.ParryAttempted += HandleParry(bool)` |
| `chg_PlayerHUD` | 꺼짐 | `NGH_Player` 쪽에서도 꺼짐(D-086) |

## 5. 구체적인 변경 방법

1. NMJ 브랜치에 `origin/NGH`를 병합합니다. Main·BossArena 충돌은 **NGH 쪽을 고르고**, NMJ가 씬에 넣은 것(보스 체력바 등)만 다시 넣습니다
2. `NGH_Player`는 지금 YPH가 G1(화승총 모션) 작업 중입니다(D-083). **10/17 오전 반환 알림 뒤**에, NGH와 순서를 맞춰 HUD 두 개를 붙여 주세요
   - 반환 전에 NMJ 스크립트가 NGH 브랜치에 병합돼 있으면 YPH가 G1 작업 중에 대신 붙일 수도 있습니다. 원하면 알려 주세요
3. `SteamCylinderGauge`의 받아치기 구독을 바꿉니다. 새 받아치기는 플레이어마다 붙은 컴포넌트의 이벤트입니다

```csharp
// SteamCylinderGauge (NMJ)
private YPH.ParryController parry;

void OnEnable()
{
    parry = GetComponentInParent<YPH.ParryController>();   // HUD가 플레이어 아래에 있을 때
    if (parry != null) parry.ParryAttempted += HandleParry;
}
void OnDisable()
{
    if (parry != null) parry.ParryAttempted -= HandleParry;
}
private void HandleParry(bool success)
{
    if (success) FlashStrongChance(strongChanceTime);
}
```

- `ParryController`는 `NGH_Player` 루트에 있습니다. HUD를 플레이어 밖에 두면 `FindAnyObjectByType<YPH.ParryController>()`로 찾습니다
- 強 강조 시간(`strongChanceTime`)은 적 경직 시간과 같게 두는 것을 추천합니다. 경직 시간은 아직 기획 미정이고 지금은 1.8초(`YPH.EnemyAI`)입니다

## 6. 영향 범위

- 이 파일을 참조하는 곳: NMJ HUD 2개만
- 같이 영향받을 수 있는 다른 작업자·기능: NGH(Main·`NGH_Player` 소유, 칼 패링 모션도 같은 신호 사용), YPH(G1 작업 중인 `NGH_Player`)
- 씬·프리팹: `NGH_Player`(반환 후), Main·BossArena(병합)

## 7. 확인 방법

1. Main Play → 붓글씨 체력바·증기 실린더가 보이고 체력바는 하나만
2. 화승총 발사(−5), 강공격(−30, NGH 연결 후), 시간 역행(−70)이 실린더 눈금과 맞음
3. 받아치기 성공 → 強 글자가 붉게 빛남, 실패 → 빛나지 않음
4. TAB으로 무기를 바꾸면 刀/銃이 바뀜

## 8. 안 하면 생기는 문제 / 대안

- **안 하면:** 병합 후 Main에 NMJ HUD가 없거나, 옛 `PlayerParry`를 지운 뒤 強 강조가 멈춤
- **대안:** HUD를 프리팹이 아니라 Main·BossArena 씬에 따로 둠. 씬마다 연결해야 해서 추천하지 않음

## 9. 첨부

- NMJ 요청 Y-2·Y-3·Y-4(`YPH_요청_20261016_받아치기성공신호.md`, `…무기칸정식이름.md`, `…공용플레이어NMJ_HUD유지.md`)의 처리 기록과 이어지는 내용
- 확인 기준: 2026-10-16 YPH 브랜치(`origin/NGH`와 같음)와 `origin/NMJ`(`25b03b4`) 비교

---

## 처리 기록 (받는 사람이 작성)

| 날짜 | 처리자 | 결과 | 비고 |
|---|---|---|---|
| | | | |
