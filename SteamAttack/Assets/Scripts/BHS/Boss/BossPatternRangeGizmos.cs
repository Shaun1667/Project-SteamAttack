using UnityEngine;

// 공격 선택 거리 확인용입니다. 실제 공격이나 피해 판정에는 관여하지 않습니다.
[DisallowMultipleComponent]
public class BossPatternRangeGizmos : MonoBehaviour
{
    [Header("패턴 목록을 가져올 FSM")]
    public BossFSM bodyFSM;
    public SnakeFSM snakeFSM;

    [Header("원 표시")]
    public bool showRanges = true;
    [Tooltip("오브젝트 높이와 관계없는 월드 Y 좌표입니다.")]
    public float worldY = 0.5f;

    [Tooltip("표시하는 거북이 패턴부터 뱀 패턴까지 순서대로 사용하는 색입니다. 색이 부족하면 반복합니다.")]
    public Color[] patternColors =
    {
        new Color(0.2f, 1f, 0.2f, 1f),
        new Color(1f, 0.65f, 0.1f, 1f),
        new Color(0.1f, 0.8f, 1f, 1f),
        new Color(1f, 0.25f, 0.7f, 1f),
        new Color(0.7f, 0.4f, 1f, 1f),
        new Color(1f, 1f, 0.2f, 1f)
    };

    private const int CircleSegments = 96;

    private void Reset()
    {
        bodyFSM = GetComponent<BossFSM>();
        if (bodyFSM == null) bodyFSM = GetComponentInParent<BossFSM>();

        snakeFSM = bodyFSM != null ? bodyFSM.snakeFSM : null;
        if (snakeFSM == null)
        {
            snakeFSM = bodyFSM != null
                ? bodyFSM.GetComponentInChildren<SnakeFSM>(true)
                : GetComponentInChildren<SnakeFSM>(true);
        }
    }

    private void OnDrawGizmos()
    {
        if (!isActiveAndEnabled || !showRanges) return;

        BossFSM body = bodyFSM != null ? bodyFSM : GetComponent<BossFSM>();
        SnakeFSM snake = snakeFSM;
        if (snake == null && body != null) snake = body.snakeFSM;
        if (snake == null) snake = GetComponent<SnakeFSM>();

        Color previousColor = Gizmos.color;
        Matrix4x4 previousMatrix = Gizmos.matrix;
        // 보스의 Scale이나 회전에 영향받지 않는 월드 거리로 그립니다.
        Gizmos.matrix = Matrix4x4.identity;
        int colorIndex = 0;

        if (body != null && body.patterns != null)
        {
            Vector3 center = body.rangeOrigin != null
                ? body.rangeOrigin.position : body.transform.position;

            foreach (BossAttack pattern in body.patterns)
            {
                if (pattern == null) continue;
                // 실제 실행 코드처럼 뱀이 담당하는 같은 컴포넌트는 제외합니다.
                if (body.snakeFSM != null && body.snakeFSM.ContainsPattern(pattern)) continue;
                DrawPattern(pattern, center, colorIndex++);
            }
        }

        if (snake != null && snake.rangeOrigin != null && snake.patterns != null)
        {
            Vector3 center = snake.rangeOrigin.position;
            foreach (BossAttack pattern in snake.patterns)
            {
                // SnakeFSM에서 실행하지 않는 거북이 돌진은 표시하지 않습니다.
                if (pattern == null || pattern is BossDashAttack) continue;
                DrawPattern(pattern, center, colorIndex++);
            }
        }

        Gizmos.color = previousColor;
        Gizmos.matrix = previousMatrix;
    }

    private void DrawPattern(BossAttack pattern, Vector3 center, int colorIndex)
    {
        // BossAttack.CanUse와 같은 최소·최대 거리를 사용합니다.
        float minimum = Mathf.Max(0f, pattern.minUseDistance);
        float maximum = Mathf.Max(minimum, pattern.maxUseDistance);
        center.y = worldY;

        Gizmos.color = patternColors != null && patternColors.Length > 0
            ? patternColors[colorIndex % patternColors.Length] : Color.yellow;

        // 반지름이 0이면 생략하고, 최소·최대가 같으면 원 하나만 그립니다.
        if (minimum > 0f) DrawCircle(center, minimum);
        if (maximum > 0f && maximum != minimum) DrawCircle(center, maximum);
    }

    private static void DrawCircle(Vector3 center, float radius)
    {
        Vector3 first = center + Vector3.right * radius;
        Vector3 previous = first;

        for (int i = 1; i < CircleSegments; i++)
        {
            float angle = i * (Mathf.PI * 2f / CircleSegments);
            Vector3 next = center + new Vector3(
                Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Gizmos.DrawLine(previous, first);
    }
}
