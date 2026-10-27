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
    [Tooltip("도착한 위치에서 정지해 있다가 꺼지기까지의 시간")]
    [SerializeField, Min(0f)] private float holdDuration = 0.15f;
    [SerializeField] private UnityEvent onBurstFinished = new UnityEvent();

    private sealed class PieceState
    {
        public Transform target;
        public Vector3 position;
        public Quaternion rotation;
        public Vector3 scale;
        public Vector3 start;
        public Vector3 end;
    }

    private readonly List<PieceState> states = new List<PieceState>();
    private bool cached;
    private bool burst;
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
        foreach (PieceState state in states)
        {
            if (state.target == null) continue;
            // 모델들이 같은 피벗을 공유해도 각 메시의 실제 중심 방향으로 퍼집니다.
            Renderer meshRenderer = state.target.GetComponent<Renderer>();
            Vector3 pieceCenter = meshRenderer != null ? meshRenderer.bounds.center : state.target.position;
            Vector3 direction = pieceCenter - center;
            if (direction.sqrMagnitude < 0.000001f) direction = transform.up;
            Vector3 offset = direction.normalized * moveDistance;
            state.start = state.target.localPosition;
            state.end = state.start + (state.target.parent != null
                ? state.target.parent.InverseTransformVector(offset) : offset);
        }
        routine = StartCoroutine(BurstRoutine());
    }

    private IEnumerator BurstRoutine()
    {
        float elapsed = 0f;
        while (elapsed < moveDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / moveDuration);
            // 처음 빠르게 튀어나가고 끝에서 감속합니다.
            ApplyPosition(1f - Mathf.Pow(1f - t, 3f));
            yield return null;
        }
        ApplyPosition(1f);
        if (holdDuration > 0f) yield return new WaitForSeconds(holdDuration);
        foreach (PieceState state in states)
            if (state.target != null) state.target.gameObject.SetActive(false);
        routine = null;
        BurstFinished?.Invoke();
        onBurstFinished.Invoke();
    }

    private void ApplyPosition(float t)
    {
        foreach (PieceState state in states)
            if (state.target != null) state.target.localPosition = Vector3.Lerp(state.start, state.end, t);
    }

    // 2페이즈 진입 시 호출: 연출 중이어도 취소하고 원래 조립 상태로 즉시 복원합니다.
    public void RestoreShell()
    {
        CacheOriginalPose();
        if (routine != null) StopCoroutine(routine);
        routine = null;
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
    }
}
