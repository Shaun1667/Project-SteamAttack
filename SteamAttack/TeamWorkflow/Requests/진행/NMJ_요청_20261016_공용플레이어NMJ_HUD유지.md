# 요청: 공용 플레이어 프리팹에 NMJ가 붙인 HUD 유지

| 항목 | 내용 |
|---|---|
| 요청일 | 2026-10-16 |
| 요청자 | NMJ |
| 받는 사람 | YPH |
| 우선순위 | 🟢 여유 |
| 희망 기한 | 프리팹 수정 때마다 |
| 관련 기획서 | `Design/UI/인게임UI.md` (D-086) |
| 상태 | 진행중 |

## 1. 한 줄 요약

`YPH_HeroPlayerIntegration` 안 `NGH_Player`에 NMJ가 붙인 HUD 두 개를 프리팹을 고칠 때 유지해 주세요.

## 2. 대상 파일

| 경로 | 종류 (스크립트/씬/프리팹/설정/기타) | 수정 방식 (추가/변경/삭제) |
|---|---|---|
| `Assets/Prefabs/YPH/HeroPlayer/YPH_HeroPlayerIntegration.prefab` | 프리팹 | 유지 |

## 3. 왜 필요한가

NMJ가 `NMJ.InkHealthBar`(붓글씨 체력바)와 `NMJ.SteamCylinderGauge`(증기·무기 HUD)를 추가했고, NGH의 `chg_PlayerHUD`는 꺼 두었습니다(NMJ HUD로 대체). YPH가 받아치기 작업으로 프리팹을 고칠 때 사라지면 안 됩니다.

## 4. 현재 상태 → 원하는 상태

| 항목 | 현재 | 원하는 모습 |
|---|---|---|
| 플레이어 HUD | NMJ HUD 2개, `chg_PlayerHUD` 꺼짐 | 그대로 유지 |

## 5. 구체적인 변경 방법

프리팹을 고칠 때 두 컴포넌트와 연결 값을 지우지 않고, `chg_PlayerHUD`를 다시 켜지 않습니다.

## 6. 영향 범위

- 받아치기 작업의 프리팹 수정(D-081, D-083)

## 7. 확인 방법

1. 프리팹 수정 후에도 붓글씨 체력바·증기 HUD가 보임
2. 체력바가 하나만 보임

## 8. 안 하면 생기는 문제 / 대안

- **안 하면:** HUD가 사라지거나 두 개 겹침
- **대안:** 없음

## 9. 첨부

- 함께 정할 것: 가방의 증기 표시(유리형 가방, 2026-10-15 회의)를 넣으면 HUD 실린더와 역할을 어떻게 나눌지
- 원문: `Design/_원문/2026-10-16_NMJ_요청사항/NMJ_요청사항_모음.md` (Y-4). YPH가 요청서 양식으로 옮김(2026-10-16)

---

## 처리 기록 (받는 사람이 작성)

| 날짜 | 처리자 | 결과 | 비고 |
|---|---|---|---|
| 2026-10-16 | YPH | 진행중 | `YPH_HeroPlayerIntegration`의 NMJ HUD는 지우지 않음. **다만 지금 팀 Main(NGH 브랜치)의 플레이어는 `YPH_HeroPlayerIntegration`이 아니라 `NGH_Player`**(10/14 NGH `10a4c1a` 이후, D-081). NMJ 브랜치는 10/13 기준이라 Main이 아직 `YPH_HeroPlayerIntegration`을 씀. 그대로 병합하면 Main에 NMJ HUD가 안 보임 → 요청 `YPH_요청_20261016_NMJ_HUD공용플레이어연결` |
