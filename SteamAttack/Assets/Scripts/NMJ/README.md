# 인벤토리 & 메인 퀘스트 (NMJ)

설치 대상 씬: **`Assets/Scenes/NMJ/HyeonmuWorld_Main.unity`** (1887 정월, 멈춘 도성)

## 1. 인벤토리 — 하중 없음, 칸 수 제한

무게(하중) 개념은 **아예 없다**. `ItemData` 에 Weight 필드가 없고, 어디에서도 총 중량을 계산하지 않는다.
제한은 두 가지뿐이다.

| 제한 | 어디서 정하나 |
|---|---|
| 칸(슬롯) 개수 | `PlayerInventory.slotCount` (1~120) |
| 칸당 최대 수량 | `ItemData.maxStack` (1이면 스택 불가) |

넣을 자리가 없으면 `Inventory.Add()` 가 **넣지 못한 개수**를 돌려준다. 0이면 전부 들어간 것.

```csharp
int leftover = PlayerInventory.Instance.AddPartial(item, 10); // 들어가는 만큼만
bool ok     = PlayerInventory.Instance.TryAddAll(item, 10);   // 전부 들어갈 때만
```

칸은 퀘스트 보상(`QuestReward.extraInventorySlots`)이나 `PlayerInventory.ExpandSlots()` 로 늘린다.
퀘스트 아이템(`ItemType.Quest`)은 버릴 수 없다.

| 파일 | 역할 |
|---|---|
| `Items/ItemData.cs` | 아이템 원본 (ScriptableObject) |
| `Items/ItemStack.cs` | 칸 하나의 내용물 |
| `Items/ItemDatabase.cs` | itemId → ItemData 조회 |
| `Inventory/Inventory.cs` | 칸 제한 인벤토리 본체 (MonoBehaviour 아님) |
| `Inventory/PlayerInventory.cs` | 씬에 두는 플레이어 인벤토리 |
| `Inventory/ItemPickup.cs` | 필드 아이템 줍기 (Player 태그 트리거) |
| `UI/InventoryUI.cs`, `UI/InventorySlotUI.cs` | 인벤토리 창 — I 키 토글, 드래그 이동/스택 합치기, `12 / 20 칸` 표시 |

## 2. 메인 퀘스트 — 고종의 어명을 받아 현무를 토벌한다

데이터는 `Assets/GameData/NMJ/` (아이템 7종 + 퀘스트 5장 + DB 2개).
메뉴 `SteamAttack ▸ 메인 퀘스트 생성 (현무 토벌)` 로 언제든 다시 만들거나 값을 갱신할 수 있다.

| 순서 | questId | 제목 | 목표 | 보상 |
|---|---|---|---|---|
| 1 | `mq_01_royal_edict` | 어명(御命) | 건청궁 마당에서 고종 알현 | 어명 교지, 온돌 탕약 2 |
| 2 | `mq_02_gigichang_ember` | 기기창의 불씨 | 기기창 도달 · 물불 동력핵 5 · 기술자와 대화 | 탕약 3, **+4칸** |
| 3 | `mq_03_black_water` | 운종가의 흑수회 | 흑수회 광신도 8 토벌 · 제단 도달 | 탕약 5, 동력핵 3 |
| 4 | `mq_04_frozen_soldiers` | 한기에 중독된 군졸 | 변이병 12 · 혼천의 고리 조각 3 · 백동수와 대화 | 백검의 환도 |
| 5 | `mq_05_slay_hyeonmu` | 현무를 토벌하라 | 현무 처치 · 현무 비늘 확보 · 복명 | 현무 비늘 갑주, **+6칸** |

앞 단계를 완료하면 다음 단계가 자동 수락된다(`QuestManager.autoAcceptMainQuests`).
메인 퀘스트는 포기할 수 없다.

### 씬에 연결된 아이디

| 아이디 | 붙어 있는 오브젝트 |
|---|---|
| `npc_gojong` | 13_인물 / 어명의 순간 (건청궁 마당) |
| `npc_gigichang_gisulja` | 13_인물 / 기기창 기술자 (얼어붙음) |
| `npc_baekdongsu` | 13_인물 / 백검(白劍) 백동수 |
| `enemy_byeonibyeong` | 13_인물 / 변이병 (한기에 중독된 군졸) 의 자식 5개 |
| `enemy_heuksu_fanatic` | 13_인물 / 흑수회 광신도 |
| `boss_hyeonmu` | 09_현무_玄武 / 현무 (玄武) |
| `loc_gigichang` | QuestZones / Zone_기기창 (트리거 박스) |
| `loc_heuksu_altar` | QuestZones / Zone_흑수회 제단 (트리거 박스) |

### 진행 보고 방법

| 목표 종류 | 알리는 법 |
|---|---|
| Talk | `QuestGiverNpc.Interact()` 호출 (대화 시스템에서) |
| Kill | 적 사망 처리에서 `QuestKillReporter.ReportDeath()` 호출 |
| Reach | `QuestZoneTrigger` 영역에 `Player` 태그 오브젝트가 들어가면 자동 |
| Collect | **자동**. 인벤토리 보유량을 그대로 따라간다 |

직접 부를 때는 `QuestManager.Instance.ReportKill("enemy_byeonibyeong")` 처럼 쓰면 된다.

## 3. 설치 / 테스트

- `SteamAttack ▸ 현재 씬에 인벤토리·메인퀘스트 설치` — 열려 있는 씬에 GameSystems(PlayerInventory /
  QuestManager / QuestDebugTester), EventSystem, UI 캔버스를 깔고 위 표대로 오브젝트를 연결한다.
  여러 번 눌러도 이미 있는 것은 건드리지 않는다.
- 재생하면 좌상단에 디버그 키 안내가 뜬다. `QuestDebugTester.showOnScreenGuide` 로 끌 수 있다.

| 키 | 동작 |
|---|---|
| I | 인벤토리 창 |
| F1 / F2 / F3 | 고종 · 기기창 기술자 · 백동수 대화 |
| F4 / F5 | 흑수회 광신도 · 변이병 처치 1 |
| F6 / F7 | 물불 동력핵 · 혼천의 고리 조각 1개 획득 |
| F8 / F9 | 기기창 · 흑수회 제단 도달 |
| F10 | 현무 처치 + 비늘 획득 |
| F11 | 보고 가능한 퀘스트 완료 |

## 4. 아직 남은 것

- **한글 폰트**: TMP 기본 폰트(LiberationSans)에는 한글 글리프가 없어서 UI 글자가 네모로 보인다.
  한글 TTF 를 `Assets/Arts` 등에 넣고 TMP Font Asset 을 만들어 UI 텍스트에 연결해야 한다.
- **아이콘**: `ItemData.icon` 이 비어 있으면 슬롯에 등급 색 사각형이 임시로 표시된다.
- **적 배치**: 씬은 정지된 장면이라 변이병 5 · 광신도 1 만 있다. 퀘스트 목표치(12 / 8)를 채우려면
  스폰을 붙이거나 `QuestData` 의 `requiredAmount` 를 낮춰야 한다.
- **플레이어**: 아직 플레이어 컨트롤러가 없어 `ItemPickup` / `QuestZoneTrigger` 는 `Player` 태그
  오브젝트가 생겨야 실제로 동작한다. 그 전까지는 디버그 키로 검증한다.

> 입력은 Input System 패키지 전용 설정이라 `Keyboard.current` 를 쓴다.
