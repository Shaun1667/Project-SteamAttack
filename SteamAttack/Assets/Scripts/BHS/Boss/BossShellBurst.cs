using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

// 꺼지지 않는 보스 본체에 붙이고, 실제 껍질 메시 조각만 등록합니다.
[DisallowMultipleComponent]
public sealed class BossShellBurst : MonoBehaviour
{
    [SerializeField] private Transform burstCenter;
    [Tooltip("Shell1, Shell2 같은 실제 조각만 지정. 부모 그룹과 자식을 함께 넣지 마세요.")]
    [SerializeField] private Transform[] pieces;
    [SerializeField, Min(0f)] private float moveDistance = 2f;
    [SerializeField, Min(0f)] private float moveDuration = 0.3f;
    [Tooltip("이동 후 낙하를 이어가는 시간. Move Duration과 합친 시간이 코어 노출 시점입니다.")]
    [SerializeField, Min(0f)] private float holdDuration = 0.15f;
    [Tooltip("날아가는 궤적의 위쪽 휘어짐 높이(월드 단위)")]
    [SerializeField, Min(0f)] private float arcHeight = 0.6f;
    [Tooltip("마지막에 시작 높이보다 내려가는 거리(월드 단위)")]
    [SerializeField, Min(0f)] private float dropDistance = 0.45f;
    [Tooltip("날아가는 동안 조각이 자신의 중심을 기준으로 회전하는 각도")]
    [SerializeField, Min(0f)] private float tumbleAngle = 110f;
    [Tooltip("조각별 거리, 높이, 회전량의 차이")]
    [SerializeField, Range(0f, 0.5f)] private float pieceVariation = 0.22f;
    [SerializeField] private UnityEvent onBurstFinished = new UnityEvent();

    private sealed class PieceState
    {
        public Transform target;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Vector3 startCenter;
        public Quaternion startRotation;
        public Vector3 pivotToCenter;
        public Vector3 travel;
        public Vector3 spinAxis;
        public float arc;
        public float drop;
        public float spin;
    }

    private readonly List<PieceState> states = new List<PieceState>();
    private bool cached;
    private bool burst;
    private bool flying;
    private float flightProgress;
    private Coroutine routine;
    public event System.Action BurstFinished;

    private void Awake() => CacheOriginalPose();

    private void CacheOriginalPose()
    {
        if (cached) return;
        cached = true;
        if (pieces == null) return;
        var unique = new HashSet<Transform>();
        foreach (Transform piece in pieces)
        {
            if (piece == null || !unique.Add(piece)) continue;
            // 부모와 자식을 동시에 이동시키면 두 번 이동하므로 설정 오류를 차단합니다.
            bool overlaps = piece == transform || transform.IsChildOf(piece);
            foreach (Transform other in pieces)
                if (other != null && other != piece && piece.IsChildOf(other)) overlaps = true;
            if (overlaps)
            {
                Debug.LogError("껍질 목록에는 서로 겹치지 않는 실제 조각만 지정하세요: " + piece.name, this);
                continue;
            }
            states.Add(new PieceState
            {
                target = piece,
                position = piece.localPosition,
                rotation = piece.localRotation,
                scale = piece.localScale
            });
        }
    }

    public void BurstShell()
    {
        if (burst || !isActiveAndEnabled) return;
        CacheOriginalPose();
        if (states.Count == 0)
        {
            Debug.LogError("껍질 조각을 Pieces에 연결하세요.", this);
            return;
        }
        burst = true;
        Vector3 center = burstCenter != null ? burstCenter.position : transform.position;
        for (int i = 0; i < states.Count; i++)
        {
            PieceState state = states[i];
            if (state.target == null) continue;
            Renderer meshRenderer = state.target.GetComponent<Renderer>();
            Vector3 pieceCenter = meshRenderer != null ? meshRenderer.bounds.center : state.target.position;
            Vector3 direction = Vector3.ProjectOnPlane(pieceCenter - center, Vector3.up);
            if (direction.sqrMagnitude < 0.000001f)
                direction = Quaternion.AngleAxis(i * 137.508f, Vector3.up) * Vector3.forward;
            direction.Normalize();
            // 게임 전체의 난수 상태에 영향을 주지 않는 조각별 편차입니다.
            float spread = Mathf.Sin((i + 1) * 2.399963f);
            float lift = Mathf.Sin((i + 1) * 1.713f + 0.7f);
            direction = Quaternion.AngleAxis(spread * pieceVariation * 25f, Vector3.up) * direction;
            state.startCenter = pieceCenter;
            state.startRotation = state.target.rotation;
            state.pivotToCenter = Quaternion.Inverse(state.startRotation) * (pieceCenter - state.target.position);
            state.travel = direction * moveDistance * (1f + spread * pieceVariation);
            state.arc = arcHeight * (1f + lift * pieceVariation);
            state.drop = dropDistance * (1f - lift * pieceVariation * 0.5f);
            state.spinAxis = (Vector3.Cross(Vector3.up, direction) + direction * spread * 0.35f).normalized;
            state.spin = tumbleAngle * (1f + lift * pieceVariation) * ((i % 2 == 0) ? 1f : -1f);
        }
        flightProgress = 0f;
        flying = true;
        routine = StartCoroutine(BurstRoutine());
    }

    private IEnumerator BurstRoutine()
    {
        // 기존 코어 노출 시점은 유지하고 정지 구간에서도 낙하/회전을 이어갑니다.
        float duration = Mathf.Max(0f, moveDuration) + Mathf.Max(0f, holdDuration);
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            flightProgress = Mathf.Clamp01(elapsed / duration);
            yield return null;
        }
        ApplyPose(1f);
        flying = false;
        foreach (PieceState state in states)
            if (state.target != null) state.target.gameObject.SetActive(false);
        routine = null;
        BurstFinished?.Invoke();
        onBurstFinished.Invoke();
    }

    private void LateUpdate()
    {
        // 부모의 애니메이션/이동 이후 월드 궤적을 적용합니다.
        if (flying) ApplyPose(flightProgress);
    }

    private void ApplyPose(float t)
    {
        float travel = 1f - (1f - t) * (1f - t);
        foreach (PieceState state in states)
        {
            if (state.target == null) continue;
            float height = 4f * state.arc * t * (1f - t) - state.drop * t * t;
            Vector3 center = state.startCenter + state.travel * travel + Vector3.up * height;
            Quaternion rotation = Quaternion.AngleAxis(state.spin * t, state.spinAxis) * state.startRotation;
            // 공통 피벗 대신 각 조각의 메시 중심을 축으로 회전합니다.
            state.target.rotation = rotation;
            state.target.position = center - rotation * state.pivotToCenter;
        }
    }

    // 2페이즈 진입 시 호출: 연출 중이어도 취소하고 원래 조립 상태로 즉시 복원합니다.
    public void RestoreShell()
    {
        CacheOriginalPose();
        if (routine != null) StopCoroutine(routine);
        routine = null;
        flying = false;
        foreach (PieceState state in states)
        {
            if (state.target == null) continue;
            state.target.localPosition = state.position;
            state.target.localRotation = state.rotation;
            state.target.localScale = state.scale;
            state.target.gameObject.SetActive(true);
        }
        burst = false;
    }

    private void OnDisable()
    {
        if (routine != null) StopCoroutine(routine);
        routine = null;
        flying = false;
    }
}
