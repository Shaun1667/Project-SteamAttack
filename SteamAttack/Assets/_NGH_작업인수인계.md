# NGH 작업 인수인계 (2026-10-30, 2026-11-01 갱신)

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

## 4. 최근 작업

### 4-0. 2026-11-01 작업 (커밋 `1a6806f` ~ `75fe918`, 이 PC에서 진행)

데모 11/3, 빌드 잠금 11/2 기준으로 진행. **원격 NGH보다 커밋 5개(이 문서 커밋 포함) 앞서 있고 푸시 안 함** (베이크 커밋이 약 1GB라 푸시가 오래 걸림).

**정리·조작**
| 커밋 | 내용 |
|---|---|
| `1a6806f` | Main·NGH_EnemyTest에 남은 옛 EnemyAI 적 삭제 |
| `dea59cf` | `chg_` 공유 이름 정리 (락온 표식·칼·원본 모델) |
| `44c4f17` | 화승총 조준 중에는 Shift를 눌러도 걷기 유지 |
| `a1fe881` | TeamWorkflow 열린 요청 7건 처리 기록 |

**화면 연출** (`NGH_ScreenFX.shader`에 전역 값 추가)
| 커밋 | 스크립트 / 에셋 | 내용 |
|---|---|---|
| `981a7bd` | `Player/ParryFlash.cs` (`_NGH_ParryFlash`) | 받아치기 성공 시 종이↔먹 화면 반전 + 충격파 |
| `5ce3793` | `Player/LowHpScreen.cs` (`_NGH_LowHpWash`, `_NGH_LowHpPulse`) | HP 낮을 때 색 바램 + 가장자리 먹빛 맥박 |
| `2c5ab3f` | Main `NGH_LampGlow` | 가로등에 발광 등갓을 더해 렌즈 플레어가 보이게 함 (플레어 0.5 유지) |
| `b054360` | `NGH_InkCloud.shader`, `M_InkCloud.mat`, Main `NGH_MountainClouds` (판 33장) | 먼 산 중턱에 수묵 구름 판 (카메라를 보는 판, 무늬가 천천히 흐름). **MeshBaker로 굽지 말 것** |

**밝기 조정** (`b7c2a40`, `3da47f8`) — 사용자가 "전체가 너무 어둡다" → 한 번 푸르스름하게 갔다가 "푸르스름·흐릿함 빼고 밝기만"으로 확정. 볼륨 `VP_NGH_JoseonSteampunk.asset`:
| 항목 | 전 | 후 |
|---|---|---|
| 노출 | +0.15 | +0.85 |
| 대비 | 12 | 10 |
| 채도 | -8 | -4 |
| 화이트 밸런스 | -2 | +5 |
| SplitToning | 그늘 남색 / 밝은 곳 놋쇠 | 중립(회색 0.5) |
| ShadowsMidtonesHighlights | 조정됨 | 중립 |
| 비네트 | 먹색 0.3 | (0.08,0.08,0.08) 0.15 |
| 블룸 | 따뜻한 색 | 흰색, 세기 0.85, 문턱 1 |
| LiftGammaGain | — | 넣었다가 뺌 (저장 안 된 빈 항목은 `3da47f8`에서 지움) |
| 렌즈 플레어 | 0.5 | 0.5 |

**길 안내** (`e18dd68`) — `Player/PathGuide.cs` (플레이어 프리팹), `NGH_PathGuide.shader`, `M_PathGuide.mat`
- NavMesh로 플레이어 → `NGH_Portal_ToBossArena_Trigger`까지 길을 찾아 바닥에 **속이 찬 연한 노란 세모** 10개 (색 (1,0.93,0.6,0.95), 크기 0.9, 간격 1.6m, 앞으로 흐름)
- 길은 근정전 **건물 안 가운데**를 가로지름. 세모 높이 = 레이로 찾은 바닥과 NavMesh 중 높은 쪽

**근정전 문** (`67437bc`)
- 근정전 앞뒤 가운데 문에 문턱 NavMeshLink (`NGH_GJJ_DoorLinks`, `Unity.AI.Navigation`)
- `World/ProximityDoor.cs` + `AutoDoor_Back/Front`를 만들었으나 NMJ의 `NmjHingedDoor`(문 8짝, 4.5m 안에서 안쪽으로 열림)와 겹쳐서 **AutoDoor 두 개는 꺼 둠**. 문 여닫기는 NMJ 스크립트가 함

**충돌체 최적화** (`78f775c`)
- 안전한 메쉬 콜라이더만 박스로 교체 (1차·2차). 박스는 `NGH_ColliderProxies` 아래 `Proxy_<원래 이름>`으로 모음, 원래 메쉬 콜라이더는 끔
- 근정전 앞 계단 양옆 틈(빠지면 못 나옴) 막기: `NGH_GJJ_StairFix` (SideWall·GapFill 동·서)
- NavMesh는 사용자가 다시 구움 (`YPH_Navigation`의 NavMeshSurface)

**MeshBaker 베이크** (`2fb5edc`, `12e8fa1`)
- 사용자가 넣은 툴 `Assets/MeshBaker-master` (MeshBaker_URP, `InsaneOne.DevTools.MeshBakerCore.Bake`)
- 정적 렌더러 606개에 Static 지정 (문처럼 움직이는 것 제외, 위치는 옮기지 않음)
- 80m 구역 × 셰이더 종류로 묶어 **71개**로 구움 → Main `NGH_MeshBaked`, 결과 파일 `Assets/Baked/Main/` (467개, 약 956MB). "용량은 줄이지 말고" 요청대로 아틀라스 크기를 줄이지 않음
- 굽지 못한 것: 정점 15만 넘는 묶음 7개, 4096 아틀라스에 안 들어가는 묶음 7개, 재질 하나뿐인 묶음, UV가 반복되는 메쉬, 재질 여러 개인 메쉬, 게임 스크립트가 붙은 렌더러(409개)
- 구운 원본 1409개: 렌더러 끔 → **오브젝트 1402개 비활성화**. 충돌체가 달린 7개(궁문_홍예문짝, 내부_기둥_1~4, 어좌, 건청궁_rail)는 충돌을 살리려고 켜 둠 (렌더러만 꺼짐)
- LFS를 쓰지 않고 **일반 커밋** (사용자가 다른 PC 두 대에서 pull해야 해서). 저장소가 이미 8.5GB라 다시 구울 때마다 약 1GB씩 쌓이는 점 주의
- NavMesh 파일이 `NavMesh-YPH_Navigation 2.asset`로 새로 생김 (씬은 2번을 씀). 예전 1번 파일은 그대로 둠

**계단 수정** (`75fe918`)
- 근정전 앞 계단: 사용자가 만든 바닥 박스 `월대_박석_아래`(높이 0.88m)가 계단 통로를 덮어 아래에 0.88m 턱이 생김 → 못 올라가고 내려올 때 어색함
- 박스를 좌·우·뒤 3조각으로 나눠 **계단 통로(x ±1.75, z 9.99~13.4)만 비움**. 높이는 그대로
- 플레이어와 같은 CharacterController(높이 1.16, 반지름 0.2, 턱 0.2)로 계단 폭 5곳에서 오르내리기 통과 확인

**팀원에게 알릴 것 (사용자가 직접)**
- NMJ: 근정전 문(AutoDoor 끔), 메쉬 콜라이더 → 박스 교체, 구운 원본 비활성화(NMJ 오브젝트 포함)
- YPH: NavMesh 다시 굽기(2번 파일), `NMJ_LevelDressing`·`NMJ_BackgroundMountains` 등 베이크 대상 포함

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
- ※ 11/1에 밝기 조정으로 값이 바뀜 → 4-0 표 참고
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

## 5. 커밋 안 된 것
- 없음 (11/1 기준 작업 폴더 깨끗함). 예전 5번 항목(세미리얼 먹 재질·Main 변경)은 `d496940`에서 커밋됨
- 다른 팀원 파일(NMJ_SteamRifle.mat, YPH WaterDepthSnapshot 재질 3개, EditorBuildSettings 등)은 줄바꿈(LF/CRLF)만 달랐고 내용 변경 없음

## 6. 다음에 하려던 것 (사용자 선택 대기)

후처리·연출 다음 후보 (추천 순, 1·2번은 11/1에 완료):
1. ~~패링 성공 연출~~ → `ParryFlash` 완료
2. ~~HP 낮을 때 화면~~ → `LowHpScreen` 완료
3. **보스 단계 전환 연출** — 다리 파괴·코어 노출 때 흔들림 + 충격파 + 잠깐 슬로모션 (`BossCoreHealth.Destroyed`, `BossCoreController` 이벤트 이용)
4. **사망 연출** — 먹 번짐(`_NGH_InkWipe`)으로 흑백 굳기
- 기타: 먹선·한지 다시 켜기(얼룩 원인 찾기), 빛줄기, 데칼(보스 균열·먹 자국), LUT, 회상 판화 효과, 눈보라 강도 변화

그 밖에 남은 일:
- **푸시**: NGH 브랜치 커밋 5개(베이크 약 1GB 포함)
- 계단 수정·베이크 결과는 에디터 시험만 함 → **플레이로 근정전 계단 오르내리기, 포털까지 길 안내 확인** 권장
- 길 안내 경로 검사에서 NavMesh 경로가 `PathPartial`로 나옴 (포털 근처까지는 이어짐). 실제로 포털까지 걸어가 보기
- 예전 NavMesh 파일(1번) 지울지 결정
- 열 아지랑이·충격파·먹 번짐·모션 블러·보스 초점 흐림은 **플레이 모드에서 직접 확인 못 함**
- 사운드 적용 요청(Audios/NMJ)은 새 파일을 못 찾아 보류 중

## 7. 작업 팁 (Unity MCP)
- 스크립트 수정 후 도메인 리로드 동안 "Unity not detected" → 잠시 뒤 재시도
- 테스트 렌더가 옛 화면이면 GPU Resident Drawer를 껐다 켜기 (`gpuResidentDrawerMode` Disabled → 원래 값)
- 셰이더를 다시 불러온 직후 렌더하면 회색 화면이 찍힘 → 한 번 더 찍으면 정상
- 씬 간 오브젝트 복사는 `Unsupported.CopyGameObjectsToPasteboard / PasteGameObjectsFromPasteboard` (프리팹 연결·덮어쓰기 유지)
- 테스트 렌더는 플레이어 카메라를 복사한 임시 카메라 + `renderPostProcessing = true`로. 블룸·플레어를 보려면 HDR RenderTexture에 그린 뒤 sRGB로 옮겨 저장
- `Unity_RunCommand`: 클래스는 `internal class CommandScript : IRunCommand`, `System.Reflection`·`HashSet`·`Regex`는 못 씀
- 걷기 시험: 플레이어와 같은 설정의 임시 CharacterController로 `Move()`를 반복하면 에디터에서도 오르내리기를 확인할 수 있음
- 씬 오브젝트를 버전 사이에서 짝지을 때는 `GlobalObjectId`가 정확함 (이름은 겹침)
- `git commit`은 훅 때문에 오래 걸려서 백그라운드로 돌림. `.git/index.lock`이 남으면 git 프로세스가 없는지 확인하고 지움 (GitHub Desktop이 남기기도 함)
- 이 PC에는 Python 3.12가 있음 (`TeamWorkflow/Tools/check.py`)
