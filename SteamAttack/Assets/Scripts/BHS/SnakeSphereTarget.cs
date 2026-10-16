using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[DefaultExecutionOrder(100)]
public class SnakeSphereTarget : MonoBehaviour
{
    [Header("IK 타겟 연결")]
    public Transform ikTarget;

    [Header("플레이어 위치에서 추가할 높이")]
    public float playerHeightOffset = 0f;

    [Header("뱀의 기본 앞 방향 보정")]
    public float yawOffset = 0f;

    [Header("콜라이더 위아래 흔들림")]
    [Min(0f)]
    [Tooltip("기본 중심에서 위아래로 움직이는 최대 거리")]
    public float bobHeight = 0.15f;

    [Min(0f)]
    [Tooltip("1이면 1초에 한 번, 0.5면 2초에 한 번 왕복")]
    public float bobFrequency = 0.5f;

    private Transform player;
    private SphereCollider sphere;

    private Vector3 originalCenter;
    private float bobPhase;

    private void Awake()
    {
        sphere = GetComponent<SphereCollider>();
        originalCenter = sphere.center;
        sphere.isTrigger = true;

        // 시작할 때 Player 태그로 자동 검색.
        GameObject playerObject = GameObject.FindWithTag("Player");

        if (playerObject != null)
        {
            player = playerObject.transform;
        }
        else
        {
            Debug.LogWarning(
                "Player 태그를 찾지 못했습니다. 콜라이더 흔들림만 실행합니다.",
                this);
        }

        if (ikTarget == null)
        {
            Debug.LogWarning("IK Target을 연결해 주세요.", this);
        }
        else if (ikTarget == transform ||
                 !ikTarget.IsChildOf(transform))
        {
            Debug.LogError(
                "IK Target은 이 SnakePivot 아래 자식이어야 합니다.",
                this);
        }
    }

    private void Update()
    {
        // 1. 콜라이더 흔들림은 항상 먼저 실행.
        bobPhase = Mathf.Repeat(
            bobPhase + Time.deltaTime * bobFrequency * Mathf.PI * 2f,
            Mathf.PI * 2f);

        float offsetY = Mathf.Sin(bobPhase) * bobHeight;

        // Transform 위치는 건드리지 않고 콜라이더 중심만 이동.
        sphere.center = originalCenter + Vector3.up * offsetY;

        // 연결이 없어도 위의 흔들림은 계속 동작.
        if (player == null || ikTarget == null)
            return;

        if (ikTarget == transform ||
            !ikTarget.IsChildOf(transform))
            return;

        // 2. 플레이어 방향으로 뱀 전체를 Y축 회전.
        Vector3 playerPoint =
            player.position + Vector3.up * playerHeightOffset;

        Vector3 center =
            transform.TransformPoint(sphere.center);

        Vector3 horizontalDirection = playerPoint - center;
        horizontalDirection.y = 0f;

        if (horizontalDirection.sqrMagnitude > 0.000001f)
        {
            float yaw = Mathf.Atan2(
                horizontalDirection.x,
                horizontalDirection.z) * Mathf.Rad2Deg;

            transform.rotation =
                Quaternion.Euler(0f, yaw + yawOffset, 0f);
        }

        // 3. 움직인 구의 표면에 IK 타겟 배치.
        center = transform.TransformPoint(sphere.center);

        Vector3 direction = playerPoint - center;

        if (direction.sqrMagnitude < 0.000001f)
            return;

        Vector3 scale = transform.lossyScale;

        float largestScale = Mathf.Max(
            Mathf.Abs(scale.x),
            Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        float worldRadius = sphere.radius * largestScale;

        ikTarget.position =
            center + direction.normalized * worldRadius;
    }

    private void OnDisable()
    {
        // 껐을 때 콜라이더 중심 복구.
        if (sphere != null)
            sphere.center = originalCenter;

        bobPhase = 0f;
    }
}