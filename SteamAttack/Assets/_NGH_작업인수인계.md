# NGH 작업 인수인계 (2026-10-30)

새 작업 환경에서 이어서 하기 위한 정리입니다. NGH(브랜치 `NGH`) 담당 작업 기준입니다.

---

## 1. 게임 스타일과 느낌

- **배경:** 1887년 정월, 시간이 멈춘 조선 도성(경복궁·육조거리). 눈 내리는 겨울밤.
- **장르:** 3인칭 액션. 환도(근접)와 증기 화승총(원거리), 시간 역행, 받아치기(패링).
- **최종 보스:** 현무(거북 몸 + 뱀). 다리 4곳(Mid1~4)을 부수면 중앙 코어(Core)가 열림.
- **미술 방향:** **조선 수묵화 + 스팀펑크**
  - 먹·한지·수묵 산수(둘레 산은 수묵화 질감)
  - 놋쇠·증기·기계 장치(증기통, 증기 화승총, 물불 발전기)
  - 오방색 사용 (청 = 시간 역행 색, 적 = 피격)
- **색감 기준:** 겨울밤의 차가운 남색 그늘 + 가로등·석물에 도는 따뜻한 놋쇠빛. 채도는 살짝 낮게, 대비는 살짝 높게.
- **거리감:** 둘레 수묵 산은 항상 선명하게, 궁궐 안 먼 건물만 안개로 흐리게.
- **연출 원칙:** 평소 화면은 담백하게. 강한 효과(왜곡·서리·흑백 등)는 타격·역행·보스 같은 순간에만 짧게.

## 2. 사용자 작업 방식 (꼭 지킬 것)

- 대화는 **한국어, 쉬운 말**로.
- **커밋·푸시는 요청할 때만.**
- 다른 팀원(NMJ·YPH·BHS) 파일·오브젝트는 되도록 건드리지 않는다. 고쳐야 하면 최소한으로 하고 알린다.
- 기존 오브젝트는 지우지 말고 **끄기**(NMJ 규칙).
- 내가 바꾸지 않은 변경은 커밋에 넣지 않고 따로 알린다.
- 숫자 조절 요청("2.5배", "중간 정도", "더 강하게")은 표로 전·후 값을 보여 준다.

## 3. 팀원 담당

| 이니셜 | 담당 |
|---|---|
| NGH | 플레이어(조작·전투·피격 모션), 후처리·화면 연출, NGH_Main 맵 |
| NMJ | 맵 배치(근정전·광화문·육조거리·적 스폰), 개발자 창(`NmjDevWindow`) |
| YPH | 증기통·총·적 AI(파란 변이 포졸), 보스 아레나 맵(YPH_BossArena) |
| BHS | 현무 보스 (FSM·공격 패턴·코어 HP) |

팀 문서: `SteamAttack/TeamWorkflow/` (문서 수정 전 `AI_지침.md` 먼저 읽기, `Meetings/현황.md`는 `Tools/check.py --write`로만 만듦 — 이 PC에는 Python이 없었음).

---

## 4. 최근 작업 (최신 커밋 `0849031`, 푸시 안 함)

### 4-1. 맵·씬
- **보스방** `Scenes/Common/BossArena_Hyangwonjeong.unity`: 맵 전체를 `Scenes/YPH/YPH_BossArena.unity`의 맵으로 교체
  - YPH 맵은 `YPH_BossArena_Map` 묶음 아래로 복사(카메라 제외), 하늘·주변광도 YPH 씬 기준
  - 기존 NMJ 맵(향원정 모델·바닥·경계벽·화로빛·달빛)은 **꺼 둠**
  - `NMJ_PlayerSpawn_FogGate`, `NMJ_BossSpawn_Hyeonmu`는 개발자 창이 쓰므로 켜 둠
- **포털 교체:** 보스방·Main 둘 다 YPH 입구 포털(`04_Entrance_ExistingPortal`) 모양으로 교체
  - 보스방: `YPH_BossArena_Map/04_Entrance_ExistingPortal/NGH_Portal_ToMain_Trigger` (→ Main `FromBossArena`)
  - Main: `Merge_TestArea/YPH_Portal_ToBossArena/NGH_Portal_ToBossArena_Trigger` (→ 보스방 `FromMain`), 위치 (-0.2, 0, -113)
  - 기존 빨간 기둥 포털 `NGH_Portal_ToMain` / `NGH_Portal_ToBossArena`는 꺼 둠

### 4-2. 후처리 (모두 PC 품질 기준, `Assets/Settings/PC_Renderer.asset`)

**기본 톤** — 볼륨 `Materials/NGH/PostFX/VP_NGH_JoseonSteampunk.asset`
- 플레이어 카메라 프리팹 `Prefabs/NGH/NGH_PlayerCamera.prefab`의 자식 `NGH_PostFX_JoseonSteampunk`(전역 Volume, priority 10)에 붙음 → Main·NGH_Main·보스방 모두 적용
- 구성: Tonemapping ACES / ColorAdjustments(노출 +0.15, 대비 12, 채도 -8) / WhiteBalance(-2) / SplitToning(그늘 남색, 밝은 곳 놋쇠) / ShadowsMidtonesHighlights / Bloom(따뜻한 색) / Vignette(먹색 0.3) / FilmGrain(약) / ScreenSpaceLensFlare(0.5)
- 노출 +0.15는 YPH 임시 볼륨 `TEMP_ArenaBloom`(노출 -1.2)이 보스방을 너무 어둡게 해서 넣은 값
- URP 에셋 색 보정 모드를 HDR로 바꿈 (`PC_RPAsset`)

**렌더러 기능 (PC_Renderer)**
| 이름 | 상태 | 셰이더 / 재질 | 내용 |
|---|---|---|---|
| ScreenSpaceAmbientOcclusion | 켬 (원래 값 0.4/0.3) | — | 한때 올렸다가 얼룩 의심으로 되돌림 |
| NGH_InkPaperPost | **꺼 둠** | `Shaders/NGH/NGH_InkPaperPost.shader` / `M_InkPaperPost.mat` | 먹 외곽선 + 한지 질감. 씬 뷰에서 얼룩이 보여 꺼 둠 (한지 얼룩 노이즈는 이미 제거) |
| NGH_DistanceFog | 켬 | `NGH_DistanceFog.shader` / `M_DistanceFog.mat` | 궁궐 구역(x ±140, z -170~245, 경계 45m 페이드) 안에서만 거리 안개. 시작 12.5m, 최대 105m, 짙기 0.61 |
| NGH_ScreenFX | 켬 | `NGH_ScreenFX.shader` / `M_ScreenFX.mat` | 평소엔 아무것도 안 함. 전역 값으로 성에·흑백·충격파·먹 번짐을 그림 |

**NGH_ScreenFX 전역 값 (코드가 올림)**
| 전역 값 | 올리는 스크립트 | 언제 |
|---|---|---|
| `_NGH_Frost` | `HitScreenFrost` | 현무가 있는 씬에서 맞으면 가장자리 성에. 0.12초 끼고 1.6초 유지, 4.1초 녹음 |
| `_NGH_RedKeep` | `SlowMotionRedKeep` | timeScale < 0.6이 0.15초 이상 → 흑백 + 붉은색만 (현무 처치 슬로모션) |
| `_NGH_Shock` + `_NGH_ShockCenter` | `ScreenShockwave` | 넘어지는 피격 / 내 강공격 명중 때 물결 고리 0.45초 |
| `_NGH_InkWipe` | `InkTransition` | 포털 이동 때 먹이 덮음(0.5초) → 씬 이동 → 걷힘(0.8초) |

**플레이어 프리팹(`Prefabs/NGH/NGH_Player.prefab`)에 붙인 연출 스크립트** (`Scripts/NGH/Player/`)
| 스크립트 | 내용 |
|---|---|
| `HitScreenVignette` | 맞으면 붉은 비네트, 피격(대)이면 진하게 + 색 갈라짐·일그러짐 (코드로 만든 Volume) |
| `HitScreenFrost` | 위 표 참고 |
| `SlowMotionRedKeep` | 위 표 참고 |
| `ScreenShockwave` | 위 표 참고 |
| `ActionMotionBlur` | 구르기·강공격 동작 중에만 모션 블러 |
| `BossRevealFocus` | 현무가 40m 안에서 처음 보이면 보스 뒤 배경만 약 3.5초 흐림 (씬마다 1회) |
| `HeatHazeEmitter` | 증기 소비(`SteamTank.OnConsumed`)·사격(`SteamGun.OnFired`) 때 아지랑이 파티클 (`NGH_HeatHaze.shader`, `M_HeatHaze.mat`). 파티클 `NGH_VentHaze`(배출구 VentPoint 자식), `NGH_MuzzleHaze` |

**시간 역행** `Scripts/NGH/TimeRewinder/RewindScreenTint.cs`: 청색 필터 + 색 갈라짐 1(최대) + 렌즈 일그러짐 -0.7 (사용자가 "더 강하게"를 두 번 요청)

**씬 이동** `Scripts/NGH/Portal/InkTransition.cs` (DontDestroyOnLoad로 저절로 생김), `ScenePortal.cs`가 `InkTransition.CoverThen(...)`으로 씬을 부름

### 4-3. 전투
- `PlayerController.TakeHit(damage, from, HitStagger)` 추가. 경직이 Heavy 이상이면 데미지가 작아도 **피격(대) = 넘어짐 → 일어남**. 예전 `TakeHit(damage, from)`도 그대로 동작
- `PlayerController.Damaged(int damage, bool heavy)` 이벤트 추가 (화면 연출들이 이걸 씀. HP가 100이라 HP 감소량으로 강·약을 가르면 안 됨)
- **파란 적(YPH_MutantSoldier)** 강공격(데미지 15)·발차기: `YPH/Enemy/EnemyAttackHitbox.cs`에서 경직 단계를 넘기게 함 (YPH가 남긴 TODO 자리, 한 줄)
- **현무 돌진·내려찍기(발 구르기 본체)** 직접 명중 시 넘어짐
  - `BHS/Boss/BHS_BossAttack.cs`에 `knockdownPlayer` 체크 칸 + `IsDirectKnockdownHit`
  - `BossFSM.cs`, `SnakeFSM.cs`가 Heavy 경직을 넘김
  - `BHS_HyunMu.prefab`에서 BossDashAttack·BossAreaAttack만 켬 (충격파 피해·물기·브레스·포격은 그대로)
- **개발자 창 원펀맨 → 보스:** `Scripts/NGH/Player/BossCoreDamageRelay.cs`에서 `NMJ.NmjDevWindow.OnePunch`면 남은 HP 전부를 줌 (다리 Mid1~4, Core 모두)

### 4-4. 그 전 작업 (이전 커밋들 요약)
- 피격 모션: Stomach Hit(팔 내림, 1.3배속) / Knocked Down(뒤집기 전 자름, 뒤로 날아감 3m) / Getting Up (`Animations/NGH/Hero/`, `NGH_HeroAnimator`)
- 소매 뚫림 보정 `SleeveActionGuard` (공격·시간 역행 중)
- NGH_Main을 Main에 덮어쓰기, 건청궁 문턱·광화문 통행 수정(얼음문 끔), 보스방에서도 개발자 창 동작
- 눈송이 크기 절반 + 살짝 빛남, 담장 눈 덮기 셰이더 `NGH_SnowCap`(근정전 제외)
- Varco 3D로 만든 에셋들 (`Arts/Models/Varco3D/`, 목록 `Varco3D_추가제작_필요목록.md`)

---

## 5. 커밋 안 된 것 (내가 바꾸지 않은 것)
- `Materials/NGH/Player_SemiRealInk/*.mat` 22개 + `Scripts/NGH/Editor/PlayerSemiRealInkApplier.cs` (림 값 변경 — 세미리얼 먹 쉐이더 작업 이어서인 듯)
- `Scenes/Common/Main.unity` (오방기 깃발 검정·노랑 꺼짐, 카메라 설정 몇 개)
→ 사용자에게 커밋 여부 확인 중

## 6. 다음에 하려던 것 (사용자 선택 대기)

후처리·연출 다음 후보 (추천 순):
1. **패링 성공 연출** — 0.1초 종이색↔먹색 반전 + 충격파 (`ScreenShockwave.Play()` 재사용 가능)
2. **HP 낮을 때 화면** — 가장자리 먹빛 맥박 + 색 바램 (`HpTint`와 짝)
3. **보스 단계 전환 연출** — 다리 파괴·코어 노출 때 흔들림 + 충격파 + 잠깐 슬로모션 (`BossCoreHealth.Destroyed`, `BossCoreController` 이벤트 이용)
4. **사망 연출** — 먹 번짐(`_NGH_InkWipe`)으로 흑백 굳기
- 기타: 먹선·한지 다시 켜기(얼룩 원인 찾기), 빛줄기, 데칼(보스 균열·먹 자국), LUT, 회상 판화 효과, 눈보라 강도 변화

그 밖에 남은 일:
- 렌즈 플레어는 지금 맵에서 거의 안 보임 (가로등이 충분히 밝지 않음). 1.2로 올리면 화면이 회색으로 덮이는 문제 있었음
- 열 아지랑이·충격파·먹 번짐·모션 블러·보스 초점 흐림은 **플레이 모드에서 직접 확인 못 함** — 첫 작업으로 한번 플레이해 보길 권장
- 사운드 적용 요청(Audios/NMJ)은 새 파일을 못 찾아 보류 중

## 7. 작업 팁 (Unity MCP)
- 스크립트 수정 후 도메인 리로드 동안 "Unity not detected" → 잠시 뒤 재시도
- 테스트 렌더가 옛 화면이면 GPU Resident Drawer를 껐다 켜기 (`gpuResidentDrawerMode` Disabled → 원래 값)
- 셰이더를 다시 불러온 직후 렌더하면 회색 화면이 찍힘 → 한 번 더 찍으면 정상
- 씬 간 오브젝트 복사는 `Unsupported.CopyGameObjectsToPasteboard / PasteGameObjectsFromPasteboard` (프리팹 연결·덮어쓰기 유지)
- 테스트 렌더는 플레이어 카메라를 복사한 임시 카메라 + `renderPostProcessing = true`로
