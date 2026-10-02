# NGH 폰트 설정 (TextMesh Pro)

작성: 남귀훈(NGH) · 2026-10-02

## 사용법
- TMP 텍스트의 Font Asset에 `ChosunCentennial_otf SDF` 하나만 지정하면 됩니다.
- 한자와 자주 안 쓰는 한글은 아래 대체(Fallback) 폰트에서 자동으로 가져옵니다.

## 구성
| 파일 | 방식 | 내용 |
|---|---|---|
| `ChosunCentennial_otf SDF` | 정적(Static), 4096 아틀라스 1장 | 영문·숫자, 자주 쓰는 한글 2350자(KS X 1001), 자모, 문장부호. 샘플 크기 56, 여백 6 |
| `ChosunCentennial_otf SDF Fallback (Dynamic)` | 동적(Dynamic), 1024 아틀라스 여러 장 | 위에 없는 나머지 한글 (예: 똠, 꿳, 뷁). 처음 나올 때 자동 생성 |
| `NotoSerifKR SDF Hanja (Dynamic)` | 동적(Dynamic), 1024 아틀라스 여러 장 | 한자. 조선100년체에 한자가 없어 Noto Serif KR(명조 계열)로 표시 |

## 고친 문제
- **한자 안 나옴**: 원본 폰트 파일(조선100년체)에 한자가 한 글자도 없음 → 한자가 있는 Noto Serif KR을 대체 폰트로 연결
- **글자 깨짐**: 기존 폰트 에셋이 여백(Padding) 0, 샘플 크기 30으로 만들어져 SDF 범위가 1픽셀뿐이었음 → 여백 6, 샘플 크기 56으로 다시 생성 (머티리얼 Gradient Scale 1 → 7)

## 주의
- 동적 폰트는 에디터에서 플레이하다 새 글자가 나오면 에셋 파일이 바뀝니다. 빌드할 때 자동으로 비워지도록(`Clear Dynamic Data On Build`) 켜 두었습니다. 커밋할 때 이 두 동적 에셋의 변경은 되돌려도 됩니다.
- Noto Serif KR은 SIL Open Font License(무료, 상업 사용 가능)입니다. 라이선스 전문: `NotoSerifKR_OFL.txt`
- 기존 폰트 에셋 백업: `Library/NGH_FontBackup/` (Library 폴더라 공유되지 않음)
