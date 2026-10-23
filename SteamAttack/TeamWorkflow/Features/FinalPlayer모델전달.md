# 기능: FinalPlayer 모델(FBX) 전달과 병합 절차

> **갱신 (2026-10-23):** 병합본(`origin/main` `2f8eff2`)에서 `FinalPlayer.fbx`는 **Git LFS 포인터**로 들어와 있습니다(`.gitattributes`, `5e0985c`). 이 문서의 "git에 올리지 못했다", "직접 전달", "`git add`하지 않는다"는 그 전 내용이니, 병합본을 받는 사람은 아래 절차 대신 `git lfs install` 후 `git lfs pull`로 받습니다. 자세한 순서는 [Main적용가이드.md](Main적용가이드.md) 3-1에 있습니다. 이 문서의 나머지 내용(경로·크기·해시·Missing 확인)은 그대로 쓸 수 있습니다.

| 항목 | 내용 |
|---|---|
| 한 줄 설명 | 용량 때문에 git에 없는 `FinalPlayer.fbx`(294MB)를 병합·pull 뒤에 직접 넣어 플레이어 모델을 살리는 방법 |
| 담당자 | YPH |
| 사용한 AI | Claude |
| 완료일 | 2026-10-23 |
| 브랜치 / 커밋 | `YPH` / `8fdf152` (NGH 플레이어 프리팹 교체), `4b1861a` (부속 에셋·`.fbx.meta`), `f823ac3` (FinalPlayer 프리팹) |
| 관련 기획서 | 없음 (모델 파일 전달 가이드) · 기준 결정: 없음 |
| 상태 | 일부 완료(FBX를 어디에 공유할지는 13번 참고) |
| 대상 플랫폼 | PC |

## 1. 이 문서는 무엇인가

`FinalPlayer.fbx`는 플레이어의 몸·옷·장신구가 들어 있는 3D 모델 파일입니다. 크기가 294MB라서 GitHub의 "파일 하나는 100MB까지" 제한을 넘어 **git에는 올리지 못했습니다.**

그런데 `NGH_Player.prefab`(NGH 플레이어)이 이 모델을 가리키도록 바뀌었습니다(`8fdf152`). 그래서 이 FBX를 따로 받아 넣지 않으면 pull·병합한 사람의 컴퓨터에서 **플레이어의 몸이 비어(Missing) 보입니다.**

비유하면 책 목차(프리팹)와 쪽 번호(`.meta`)는 공유 노트에 있는데, 본문(FBX)만 따로 전달하는 상황입니다. 쪽 번호가 같아야 목차가 본문을 찾으므로, **파일 이름과 위치를 그대로** 지켜야 합니다.

## 2. 5분 안에 넣어 보기

받는 사람(병합 담당자와 모든 팀원)이 할 일입니다.

1. **FBX 받기:** YPH에게서 `FinalPlayer.fbx`(또는 이를 묶은 zip)를 받는다. 공유 위치는 아직 정해지지 않았다(13번).
2. **받은 파일 확인:** 크기가 **308,193,324바이트**(약 294MB)인지, 해시가 아래와 같은지 확인한다. (4-3)
3. **Unity 끄기:** 열려 있으면 닫는다. (열린 채로 넣어도 되지만, 닫고 넣는 쪽이 안전하다. 8번 참고)
4. **git에서 받기:** `YPH` 브랜치를 pull·병합한다. 이때 `FinalPlayer.fbx.meta`가 같이 들어온다.
5. **FBX 넣기:** 아래 경로에 **이름을 바꾸지 않고** 복사한다.
   ```text
   SteamAttack/Assets/Arts/Models/YPH/Player/FinalPlayerMagica/FinalPlayer.fbx
   ```
   같은 폴더에 `FinalPlayer.fbx.meta`가 이미 있어야 한다. 없으면 4번을 다시 확인한다.
6. **Unity 열기:** 처음 열 때 이 FBX를 읽느라 시간이 걸린다.
7. **확인:** 3-3의 시나리오로 Missing이 없는지 본다.
8. **git에는 올리지 않기:** `git status`에 `FinalPlayer.fbx`가 "추적 안 됨(untracked)"으로 보이는 게 정상이다. **`git add`하지 않는다.** (100MB를 넘어 푸시가 거부된다.)

## 3. 테스트 조작법

### 3-1. 게임 조작 키

없음

### 3-2. 테스트 전용 키 (디버그)

없음

### 3-3. 확인 시나리오

| # | 하는 일 (순서대로) | 정상이라면 보이는 것 |
|---|---|---|
| 1 | Project 창에서 `Arts/Models/YPH/Player/FinalPlayerMagica/FinalPlayer.fbx`를 선택한다 | Inspector에 Model·Rig 탭이 나온다. Rig 탭의 Animation Type이 **Humanoid**다 |
| 2 | `Prefabs/NGH/NGH_Player.prefab`을 연다 | 몸·머리·장신구가 보이고, 메시 칸에 `None (Mesh)`나 Missing 표시가 없다 |
| 3 | `Prefabs/YPH/HeroPlayer/Models/FinalPlayer.prefab`을 연다 | 같은 모델이 보인다 |
| 4 | `Scenes/YPH/YPH_MainPlayerTest.unity`를 열고 Play를 누른다 | 플레이어가 서 있고 움직이면 애니메이션이 재생된다 |
| 5 | Console을 본다 | `missing`·`The referenced script` 경고가 없다 (Magica Cloth 2가 없으면 천 관련 경고는 나온다. 10번) |

## 4. 어떻게 만들어졌나 (구조)

### 4-1. 관련 파일

| 경로 | git 상태 | 역할 |
|---|---|---|
| `Assets/Arts/Models/YPH/Player/FinalPlayerMagica/FinalPlayer.fbx` | **없음 (직접 넣기)** | 몸·옷·장신구 메시와 뼈대. 294MB |
| `Assets/Arts/Models/YPH/Player/FinalPlayerMagica/FinalPlayer.fbx.meta` | 있음 (`4b1861a`) | 이 FBX의 고유 번호(guid)와 읽기 설정을 보관 |
| `Assets/Arts/Models/YPH/Player/FinalPlayerMagica/Materials/`, `Textures/`, `Cloth/` | 있음 | 머티리얼 17개, 텍스처 33개, 천 메시 |
| `Assets/Prefabs/YPH/HeroPlayer/Models/FinalPlayer.prefab` | 있음 (`f823ac3`) | 모델만 담은 프리팹 |
| `Assets/Prefabs/NGH/NGH_Player.prefab` | 있음 (`8fdf152`) | NGH 플레이어. 이 FBX의 메시·머티리얼을 직접 가리킴 (15곳) |

### 4-2. 왜 이름과 위치를 그대로 지켜야 하나

Unity는 파일을 이름이 아니라 **guid(고유 번호)** 로 찾습니다. 이 FBX의 guid는 아래와 같고 `FinalPlayer.fbx.meta`에 적혀 있습니다.

```text
0860b529352414c4ba7109524ccd7ae9
```

`NGH_Player.prefab`은 "guid 0860b529…의 메시"라고 적혀 있습니다. 같은 폴더에 `.meta`와 짝이 맞는 FBX가 들어오면 Unity가 이 번호로 다시 이어 줍니다. 파일 이름을 바꾸거나 `.meta`를 새로 만들게 하면 번호가 달라져 다시 Missing이 됩니다.

### 4-3. 받은 파일이 맞는지 확인하기

| 항목 | 값 |
|---|---|
| 크기 | 308,193,324 바이트 |
| SHA-256 | `9d01f247618a11cec503fe0b425c9558e1496e74e2a89c7eeec5cffb84471750` |

PowerShell에서 확인하는 방법입니다.

```powershell
Get-FileHash .\FinalPlayer.fbx -Algorithm SHA256
```

해시가 다르면 전송 중에 깨졌거나 다른 버전입니다. 다시 받습니다.

### 4-4. 병합 중 충돌이 나면

`NGH_Player.prefab`은 NGH 소유 파일이고, `8fdf152`에서 약 12,900줄이 추가되고 17,200줄이 삭제될 만큼 크게 바뀌었습니다. NGH도 이 프리팹을 고치고 있었다면 충돌이 납니다.

- 프리팹은 한 줄씩 합치기 어려운 글 파일(YAML)이라, 충돌이 나면 **한쪽 버전을 통째로 고르고 다른 쪽 변경을 Unity에서 다시 적용**하는 방식이 안전합니다.
- 어느 쪽을 고를지는 YPH와 NGH가 함께 정합니다. 이 문서가 정하지 않습니다.

## 5. 왜 이렇게 했나

| 결정 | 이유 | 버린 대안과 그 이유 |
|---|---|---|
| FBX를 git에서 뺐다 | 294MB라 GitHub 파일당 100MB 제한을 넘어 푸시가 거부된다 | 그냥 올리기: 푸시 실패. 한 번 커밋하면 기록에도 남아 `.git`이 커진다 |
| `.meta`는 git에 올렸다 | guid를 지켜야 프리팹 참조가 이어진다 | `.meta`도 빼기: 팀원마다 guid가 달라져 Missing |
| 파일을 직접 전달 | 가장 간단하다 | Git LFS: 저장소 설정이 필요해 아직 결정되지 않음(13번) |

일부러 단순하게 둔 곳과 한계: 손으로 전달하므로 **빠뜨리거나 오래된 버전을 쓸 수 있습니다.** 해시 확인(4-3)이 그걸 막는 유일한 장치입니다.

## 6. 인스펙터 값 설명

없음 (이 문서는 코드·인스펙터를 바꾸지 않는다). FBX 읽기 설정은 `.meta`에 들어 있다(Animation Type = Humanoid, Import BlendShapes 켜짐). 바꾸면 모든 팀원의 `.meta`가 달라지므로 YPH와 상의한다.

## 7. 내 코드에서 쓰는 법

### 7-1. 공개 API

없음

### 7-2. 사용 예시

없음

### 7-3. 이 기능에 연결하려면

없음

## 8. 확장하는 법

| 하고 싶은 일 | 하는 방법 (순서대로) | 고치지 않아도 되는 것 |
|---|---|---|
| 모델을 새 버전으로 바꾸기 | 1. 같은 이름 `FinalPlayer.fbx`로 덮어쓴다 2. Unity에서 다시 읽게 한다 3. 새 파일의 크기·해시를 이 문서 4-3에 고치고 모두에게 알린다 | `.meta`, `NGH_Player.prefab` (이름·위치가 같으면 연결 유지) |
| Unity를 이미 연 채로 FBX를 넣었을 때 | Unity 창으로 돌아가면 자동으로 읽는다. Console의 `.meta` 관련 경고가 있으면 3-3을 다시 확인한다 | |

## 9. 협업 가이드

- **소유자:** FBX와 `FinalPlayerMagica/` 폴더는 YPH. `NGH_Player.prefab`은 NGH.
- **수정이 필요하면:** 직접 고치지 말고 [요청문서_템플릿.md](../Templates/요청문서_템플릿.md)로 소유자에게 요청한다.
- **씬·프리팹:** 한 사람만 수정하는 파일 — `Prefabs/NGH/NGH_Player.prefab`(NGH), `Prefabs/YPH/HeroPlayer/Models/FinalPlayer.prefab`(YPH)
- **함부로 바꾸면 안 되는 것:** `FinalPlayer.fbx`의 이름·위치, `FinalPlayer.fbx.meta`의 guid
- **이 기능을 쓰고 있는 곳:** `NGH_Player.prefab`이 FBX를 직접 가리킨다. `FinalPlayer.prefab`은 아직 어디에서도 쓰이지 않는다.
- **연락할 곳:** YPH

## 10. 의존성과 준비물

| 종류 | 이름 | 설명 |
|---|---|---|
| 큰 파일 | `FinalPlayer.fbx` | git에 없음. 직접 넣어야 한다 |
| 유료 에셋 | Magica Cloth 2 | `FinalPlayer.prefab`과 `NGH_Player.prefab`이 천 물리에 쓴다. 이것도 git에 없다(`Scripts/YPH/ThirdParty/`). 없으면 몸은 보이지만 천 컴포넌트가 Missing이고 천이 흔들리지 않는다. 별도로 설치한다 |
| 패키지 | `Unity.AI.Navigation` | `NGH_Player.prefab`의 `NavMeshModifier`가 쓴다 |
| 다른 작업자의 코드·에셋 | `NGH_Player.prefab` | NGH 소유 |

## 11. 성능 기록 (PC, 목표 60FPS)

미측정

## 12. 검증 결과

| 완료 조건 | 결과 | 확인 방법 |
|---|---|---|
| FBX 크기·해시 계산 | 완료 | 2026-10-23 작성자 PC의 파일로 계산 |
| 3-3 시나리오를 다른 팀원 PC에서 실행 | **미실행** | 작성자 PC에서만 모델이 있는 상태로 확인했고, FBX 없이 pull한 환경에서 Missing이 어떻게 보이는지, 이 절차로 복구되는지는 아직 시험하지 않았다 |

- 콘솔 에러·경고: 미확인
- 코드 컨벤션 점검: 해당 없음 (코드 변경 없음)

## 13. 알려진 문제와 앞으로 할 일

| 구분 | 내용 | 영향 | 해결 방향 |
|---|---|---|---|
| 알려진 문제 | FBX 없이 pull하면 NGH 플레이어가 Missing | 병합 후 모두의 플레이어가 안 보임 | 이 문서 2번 절차 |
| 결정 필요 | FBX를 **어디에 공유할지** 정해지지 않았다 (공유 드라이브 / Git LFS / 메신저) | 받을 곳이 없다 | 팀장과 결정. 결정되면 2번 1단계에 적는다 |
| 확인 필요 | Unity가 `.meta`만 있고 FBX가 없는 상태를 어떻게 처리하는지 확인하지 못했다. 경고만 하는지 `.meta`를 지우는지 모른다 | `.meta`가 지워지면 guid가 사라질 수 있다 | 지워졌다면 `git checkout -- SteamAttack/Assets/Arts/Models/YPH/Player/FinalPlayerMagica/FinalPlayer.fbx.meta`로 복구한 뒤 FBX를 넣는다. 가능하면 FBX를 넣은 뒤 Unity를 연다 |
| 한계 | `.git`이 이미 커서 이번 텍스처 약 287MB가 더해졌다 | 푸시·clone이 느리다 | 푸시 전 용량 점검 |
| 다음에 할 일 | `.gitignore`에 `FinalPlayer.fbx`를 추가해 실수로 올리지 않게 하기 | `git add -A` 때 실수로 들어갈 수 있다 | `.gitignore`는 팀장 소관이므로 요청 문서로 부탁한다 |

## 14. 용어

| 용어 | 뜻 |
|---|---|
| FBX | 3D 모델·뼈대·애니메이션을 담는 파일 형식 |
| `.meta` | Unity가 파일마다 만드는 짝 파일. 고유 번호(guid)와 읽기 설정이 들어 있다 |
| guid | 파일의 고유 번호. 프리팹은 파일 이름이 아니라 이 번호로 파일을 찾는다 |
| Missing | 프리팹이 가리키는 파일을 못 찾은 상태 |
| Humanoid | 사람 모양 뼈대로 읽는 설정. 다른 사람의 애니메이션을 같이 쓸 수 있다 |

## 15. 변경 이력

| 날짜 | 작성·수정 | 바뀐 내용 | 커밋 |
|---|---|---|---|
| 2026-10-23 | YPH(Claude) | 최초 작성 | 미커밋 |
