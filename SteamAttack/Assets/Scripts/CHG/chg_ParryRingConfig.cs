using UnityEngine;

/// <summary>
/// 패링 동심원 UI 설정 (기획: 게임 제작일지/패링)
/// 에셋 위치: Assets/Prefabs/CHG/Resources/chg_ParryRingConfig.asset — 여기 값을 바꾸면 바로 반영된다.
/// 에셋이 없거나 그림이 비어 있으면 코드로 만든 임시 고리를 쓴다.
/// </summary>
[CreateAssetMenu(fileName = "chg_ParryRingConfig", menuName = "CHG/Parry Ring Config")]
public class chg_ParryRingConfig : ScriptableObject
{
    [Header("그림")]
    [Tooltip("바깥 원: 기본 먹선 고리 (검정)")]
    public Texture2D blackRing;
    [Tooltip("안쪽 원: 아이보리 먹선 + 옅은 그림자 (비우면 검은 고리 사용)")]
    public Texture2D whiteRing;
    [Tooltip("타이밍 순간·성공 때 쓰는 붉은 먹선 고리")]
    public Texture2D redRing;

    [Header("효과음 (비워 두면 소리 없음)")]
    [Tooltip("두 원이 겹치는 순간(판정 구간 시작)")]
    public AudioClip overlapSfx;
    [Tooltip("패링 성공")]
    public AudioClip successSfx;
    [Range(0f, 1f)] public float sfxVolume = 0.8f;

    [Header("모양 (1920x1080 화면 기준)")]
    [Tooltip("안쪽 원 지름(px)")]
    [Min(10f)] public float ringSize = 120f;
    [Tooltip("적 발밑에서 원이 뜰 높이(m) — 상체")]
    public float bodyHeight = 1.2f;
    [Tooltip("안쪽 원 진하기")]
    [Range(0f, 1f)] public float innerAlpha = 0.88f;

    [Header("움직임")]
    [Tooltip("바깥 원이 줄어드는 시간(초) — 모든 적 동일. 겹치는 순간보다 이만큼 앞서 나타난다")]
    [Min(0.1f)] public float shrinkTime = 0.8f;
    [Tooltip("바깥 원 시작 크기 (안쪽 원 대비, 2 = 200%)")]
    [Min(1f)] public float outerStartScale = 2f;
    [Tooltip("바깥 원 총 회전 각도 (시계 방향, 점점 빨라짐)")]
    public float outerRotation = 135f;
    [Tooltip("회전이 빨라지는 정도 (1 = 일정한 속도, 2 = 점점 빨라짐, 3 = 끝에서 확 빨라짐)")]
    [Range(1f, 5f)] public float rotationEaseIn = 3f;
}
