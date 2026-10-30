# itch.io 페이지 테마: 玄武 · 역류의 어명

타이틀 화면(밤하늘, 상아색 붓글씨 `玄武`, 붉은 낙관 `命`, 놋쇠색 부제)과 같은 톤으로 맞춘 itch.io 페이지 설정이다. 이미지는 VARCO 3D(GenerateImage)로 만들었다.

미리보기: [preview.html](preview.html)을 브라우저(크롬 등)로 열면 itch 레이아웃에 테마를 입힌 모습을 볼 수 있다. 본문은 `<template id="desc">` 안에 복사돼 있으니 description.html을 고치면 거기도 같이 고친다. 한눈에 보는 합성본은 [preview_mock.png](preview_mock.png).

## 1. 올릴 파일 (`upload/`)

| 파일 | 넣는 곳 | 규격 |
| --- | --- | --- |
| `cover_630x500.jpg` (또는 `cover_1260x1000.jpg`) | Edit game > Cover image | 630×500 권장, 2배 해상도도 됨 |
| `banner_960.png` | Edit theme > Banner | 960×369, 가장자리 투명 PNG |
| `background_1920.jpg` | Edit theme > Background image | 1920×1080 |
| `art_world_b.jpg`, `art_combat_b.jpg` | 본문(Description) 안 이미지 | 1200×675 |

원본은 `raw/`에 있다. 같은 프롬프트로 2장씩 뽑아 하나씩 골랐다(배너 A, 배경 B, 커버 A, 전투 B, 세계 B).

## 2. Edit theme 설정값

| 항목 | 값 | 이유 |
| --- | --- | --- |
| Layout | 본문 왼쪽 + 스크린샷 오른쪽 사이드 | 본문 삽화가 넓게 보임 |
| Banner | `banner_960.png`, 가운데 정렬 | 타이틀 로고 그대로 |
| Background image | `background_1920.jpg` | 먹 산수 + 경복궁, 아래가 어둡게 빠짐 |
| Background repeat / position | No repeat · Top center | 아래쪽은 BG 색으로 이어짐 |
| BG color | `#0c0f15` | 배경 이미지 맨 아래 색과 같음 |
| BG2 (본문 칸) | `#151b22` | 밤하늘 남색. 투명도 조절이 보이면 90% 정도 |
| Text | `#e8e2d4` | 한지 상아색 (타이틀 `玄武` 글자색) |
| Link | `#d9a441` | 놋쇠색 (타이틀 부제색) |
| Button / Button text | `#a3241c` / `#f4ecdc` | 낙관 붉은색 |
| Border | `#2b333d` | |
| Header font | Nanum Myeongjo (목록에 없으면 Noto Serif) | 명조로 붓글씨 로고와 어울림 |
| Body font | Nanum Gothic 또는 Noto Sans, 크기 기본 | 한글 가독성 |

## 3. 본문

[description.html](description.html)을 Description 편집기의 HTML 보기에 붙여 넣는다. `[IMG: 파일명]` 자리는 편집기 이미지 버튼으로 `upload/`의 같은 파일을 올려 바꾼다. itch는 본문의 style 속성과 외부 주소 이미지를 지우므로, 색·글꼴은 위 테마 설정으로만 바꾼다.

## 4. 스크린샷 칸

본문 삽화는 AI 일러스트라 실제 게임 화면이 아니다. Screenshots 칸에는 실제 플레이 캡처(광화문 전투, 먹 붓 공격 이펙트, 시간 역행 잔상, 현무전)를 3~5장 넣는다.

## 5. 확인 필요

- 연도 표기: 타이틀은 "1887년 3월", 세계관 문서는 "1887년 정월". 본문은 타이틀을 따랐다
- 강공격 입력: 기획은 우클릭, 현재 구현은 좌+우 동시 클릭. 본문은 기획(우클릭)을 따랐다
- 로고 한자: 생성 이미지의 `玄` 획이 흘림체라 원래 자형과 조금 다르다. 정확한 자형이 필요하면 배너는 타이틀 화면 캡처로 바꾼다
