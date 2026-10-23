using UnityEngine;

[DisallowMultipleComponent]
public class BossFSM : MonoBehaviour
{
    public enum State
    {
        Idle, SearchPlayer, LookAtPlayer, SelectAttack, Attack, Recovery
    }

    [Header("현재 상태 - 확인용")]
    [SerializeField] private State currentState;
    [SerializeField] private Transform player;

    // 포격의 중거리 체류 조건도 FSM이 실제로 추적하는 같은 플레이어를 사용한다.
    public Transform Player => player;

    [Header("플레이어 탐색 / 회전")]
    [Min(0.1f)] public float detectRange = 8f;
    [Min(0.05f)] public float searchInterval = 0.25f;
    [Min(1f)] public float turnSpeed = 180f;
    [Min(0f)] public float heightTolerance = 2f;

    [Header("거북이 패턴 목록")]
    [Tooltip("위에서부터 확인해서 현재 거리에서 사용할 수 있는 첫 패턴을 실행합니다.")]
    public BossAttack[] patterns = new BossAttack[0];

    [Header("뱀과 공유하는 공격 조율자")]
    [Tooltip("두 FSM에 같은 조율자를 연결하세요. 비우면 자신/부모에서 찾습니다. 없으면 공격을 시작하지 않습니다.")]
    public BossAttackCoordinator attackCoordinator;

    private BossAttack activeAttack;
    private BossAttackCoordinator acquiredCoordinator;
    private bool ownsAttack;
    private bool reportedMissingCoordinator;
    private float stateTimer;
    private float recoveryDuration;

    private void OnEnable()
    {
        player = null;
        activeAttack = null;
        acquiredCoordinator = null;
        ownsAttack = false;
        reportedMissingCoordinator = false;
        ChangeState(State.Idle);
    }

    private void Update()
    {
        stateTimer += Time.deltaTime;
        switch (currentState)
        {
            case State.Idle:
                if (stateTimer >= searchInterval) ChangeState(State.SearchPlayer);
                break;
            case State.SearchPlayer:
                SearchPlayer();
                break;
            case State.LookAtPlayer:
                LookAtPlayer();
                break;
            case State.SelectAttack:
                SelectAttack();
                break;
            case State.Attack:
                UpdateAttack();
                break;
            case State.Recovery:
                if (stateTimer >= recoveryDuration) ChangeState(State.SearchPlayer);
                break;
        }
    }

    private void SearchPlayer()
    {
        if (player == null || !player.gameObject.activeInHierarchy)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Player");
            player = found != null ? found.transform : null;
        }
        ChangeState(IsPlayerInRange() ? State.LookAtPlayer : State.Idle);
    }

    private bool IsPlayerInRange()
    {
        if (player == null || !player.gameObject.activeInHierarchy) return false;
        Vector3 offset = player.position - transform.position;
        if (Mathf.Abs(offset.y) > heightTolerance) return false;
        offset.y = 0f;
        return offset.sqrMagnitude <= detectRange * detectRange;
    }

    private void LookAtPlayer()
    {
        if (!IsPlayerInRange())
        {
            ChangeState(State.Idle);
            return;
        }

        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (direction.sqrMagnitude < 0.0001f) direction = Vector3.forward;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, targetRotation, turnSpeed * Time.deltaTime);
        if (Quaternion.Angle(transform.rotation, targetRotation) <= 1f)
        {
            transform.rotation = targetRotation;
            // 회전이 완료된 순간의 거리로 선택한다.
            ChangeState(State.SelectAttack);
            SelectAttack();
        }
    }

    private void SelectAttack()
    {
        if (!IsPlayerInRange())
        {
            ChangeState(State.Idle);
            return;
        }

        Vector3 offset = player.position - transform.position;
        offset.y = 0f;
        float distance = offset.magnitude;
        activeAttack = null;

        // 특정 공격 이름을 알 필요 없이 목록에 등록된 패턴을 확인한다.
        if (patterns != null)
        {
            foreach (BossAttack pattern in patterns)
            {
                if (pattern == null || pattern.IsRunning || !pattern.CanUse(distance, player)) continue;
                activeAttack = pattern;
                break;
            }
        }

        // 현재 거리에서 사용할 패턴이 없으면 대기하며 다시 탐색한다.
        if (activeAttack == null)
        {
            ChangeState(State.Idle);
            return;
        }

        // 권한을 기다릴 때도 본체는 플레이어를 향해 계속 회전합니다.
        if (!TryReserveAttack())
        {
            activeAttack = null;
            ChangeState(State.LookAtPlayer);
            return;
        }

        BossAttack attack = activeAttack;
        ownsAttack = true;
        ChangeState(State.Attack);
        try
        {
            if (attack.BeginAttack(transform, player, heightTolerance, DamagePlayer)) return;
            Debug.LogWarning("선택한 공격을 시작하지 못했습니다: " + attack.GetType().Name +
                ". 공격 컴포넌트와 설정을 확인하세요.", this);
        }
        catch (System.Exception exception)
        {
            Debug.LogException(exception, this);
        }
        if (!isActiveAndEnabled || activeAttack != attack || !ownsAttack) return;
        CancelOwnedAttack(false);
        recoveryDuration = 1f;
        ChangeState(State.Recovery);
    }

    private bool TryReserveAttack()
    {
        if (attackCoordinator == null)
            attackCoordinator = GetComponentInParent<BossAttackCoordinator>();
        if (attackCoordinator == null)
        {
            if (!reportedMissingCoordinator)
            {
                reportedMissingCoordinator = true;
                Debug.LogWarning("BossFSM: 공통 몸체에 BossAttackCoordinator를 추가하고 두 FSM에 같은 조율자를 연결하세요.", this);
            }
            return false;
        }
        if (!attackCoordinator.TryAcquire(this, activeAttack)) return false;
        acquiredCoordinator = attackCoordinator;
        return true;
    }

    // 공격이 명중하면 플레이어의 피격 함수를 직접 호출한다.
    private void DamagePlayer(float damage)
    {
        if (player == null) return;
        /*테스트용*/
        PlayerHitTeleport receiverA = player.GetComponentInParent<PlayerHitTeleport>();
        if (receiverA != null) receiverA.TakeDamage(damage);

        PlayerHealth receiver = player.GetComponentInParent<PlayerHealth>();

        if (receiver != null) receiver.TakeDamage(damage);
    }

    private void UpdateAttack()
    {
        BossAttack attack = activeAttack;
        if (attack != null)
        {
            attack.TickAttack(Time.deltaTime);
            if (!isActiveAndEnabled || currentState != State.Attack || activeAttack != attack) return;
            if (attack != null && attack.IsRunning) return;
        }
        recoveryDuration = attack != null ? Mathf.Max(0f, attack.recoveryTime) : 0f;
        ownsAttack = false;
        activeAttack = null;
        ReleaseReservation();
        ChangeState(State.Recovery);
    }

    private void ReleaseReservation(bool applyGap = true)
    {
        BossAttackCoordinator coordinator = acquiredCoordinator;
        acquiredCoordinator = null;
        if (coordinator != null) coordinator.Release(this, applyGap);
    }

    private void CancelOwnedAttack(bool applyGap = true)
    {
        BossAttack attack = activeAttack;
        bool shouldCancel = ownsAttack;
        activeAttack = null;
        ownsAttack = false;
        try
        {
            if (shouldCancel && attack != null) attack.CancelAttack();
        }
        finally
        {
            ReleaseReservation(applyGap);
        }
    }

    private void ChangeState(State next)
    {
        currentState = next;
        stateTimer = 0f;
    }

    private void OnDisable()
    {
        CancelOwnedAttack();
        ChangeState(State.Idle);
    }

    private void OnValidate()
    {
        detectRange = Mathf.Max(0.1f, detectRange);
        searchInterval = Mathf.Max(0.05f, searchInterval);
        turnSpeed = Mathf.Max(1f, turnSpeed);
        heightTolerance = Mathf.Max(0f, heightTolerance);
    }

#if UNITY_EDITOR
    [Header("패턴 발동 거리 기즈모 - 보스 선택 시 표시")]
    public bool showActivationRangeGizmos = true;
    [Tooltip("표시할 바닥의 월드 Y 좌표입니다. 거리 판정에는 영향을 주지 않습니다.")]
    public float rangeGizmoGroundY = 0f;
    [Range(0f, 0.5f)] public float rangeGizmoOpacity = 0.16f;
    public Color closeRangeColor = new Color(1f, 0.35f, 0.1f);
    public Color breathRangeColor = new Color(0.1f, 0.8f, 1f);
    public Color dashRangeColor = new Color(0.8f, 0.3f, 1f);

    private void OnDrawGizmosSelected()
    {
        if (!showActivationRangeGizmos || patterns == null) return;

        float limit = Mathf.Max(0.1f, detectRange);
        var edges = new System.Collections.Generic.List<float> { 0f, limit };
        foreach (BossAttack pattern in patterns)
        {
            if (pattern == null) continue;
            float minimum = Mathf.Max(0f, pattern.minUseDistance);
            float maximum = Mathf.Max(minimum, pattern.maxUseDistance);
            edges.Add(Mathf.Clamp(minimum, 0f, limit));
            edges.Add(Mathf.Clamp(maximum, 0f, limit));
            // 본체와 파동의 도달 거리도 CanUse 경계에 포함한다.
            if (pattern is BossAreaAttack area)
            {
                edges.Add(Mathf.Clamp(Mathf.Max(0.01f, area.radius), 0f, limit));
                if (area.enableWave)
                    edges.Add(Mathf.Clamp(Mathf.Max(0.1f, area.waveMaxRadius), 0f, limit));
            }
        }
        edges.Sort();
        for (int i = edges.Count - 1; i > 0; i--)
            if (edges[i] == edges[i - 1]) edges.RemoveAt(i);

        Matrix4x4 previousMatrix = UnityEditor.Handles.matrix;
        Color previousColor = UnityEditor.Handles.color;
        var previousZTest = UnityEditor.Handles.zTest;
        try
        {
            UnityEditor.Handles.matrix = Matrix4x4.identity;
            UnityEditor.Handles.zTest = UnityEngine.Rendering.CompareFunction.Always;
            Vector3 center = new Vector3(transform.position.x,
                rangeGizmoGroundY + 0.03f, transform.position.z);

            int start = 0;
            while (start < edges.Count - 1)
            {
                int end = start + 1;
                BossAttack selected = GetRangeGizmoPattern((edges[start] + edges[end]) * 0.5f);
                // 같은 패턴이 이어지는 구간은 한 고리로 합친다.
                while (end < edges.Count - 1 &&
                    GetRangeGizmoPattern((edges[end] + edges[end + 1]) * 0.5f) == selected)
                    end++;
                DrawActivationBand(center, edges[start], edges[end], selected);
                start = end;
            }

            UnityEditor.Handles.color = Color.white;
            UnityEditor.Handles.DrawWireDisc(center, Vector3.up, limit);
            UnityEditor.Handles.Label(center + Vector3.back * (limit + 0.3f),
                $"탐색 한계 {limit:0.##}m · 바깥에서는 패턴 선택 안 함");
        }
        finally
        {
            UnityEditor.Handles.matrix = previousMatrix;
            UnityEditor.Handles.color = previousColor;
            UnityEditor.Handles.zTest = previousZTest;
        }
    }

    private BossAttack GetRangeGizmoPattern(float distance)
    {
        // 거리 조건만 표시한다. 포격의 체류 시간 등 추가 조건은 실제 선택 때 확인한다.
        foreach (BossAttack pattern in patterns)
            if (pattern != null && pattern.CanUse(distance)) return pattern;
        return null;
    }

    private void DrawActivationBand(Vector3 center, float inner, float outer, BossAttack pattern)
    {
        Color color = Color.gray;
        string label = "사용 가능한 패턴 없음";
        if (pattern is BossAreaAttack area) { color = closeRangeColor; label = area.enableWave ? "본체 + 파동 발동" : "근거리"; }
        else if (pattern is BossBreathAttack) { color = breathRangeColor; label = "브레스"; }
        else if (pattern is BossDashAttack) { color = dashRangeColor; label = "돌진"; }
        else if (pattern is BossArtilleryAttack artillery)
        {
            color = Color.yellow;
            label = $"포격 후보 · 체류 {Mathf.Max(0f, artillery.requiredStayTime):0.##}초\n시간 미충족 시 다음 패턴 확인";
        }
        else if (pattern != null) { color = Color.yellow; label = pattern.GetType().Name; }

        UnityEditor.Handles.color = new Color(color.r, color.g, color.b, rangeGizmoOpacity);
        if (inner == 0f)
        {
            UnityEditor.Handles.DrawSolidDisc(center, Vector3.up, outer);
        }
        else
        {
            // 고리 부분만 채우므로 안쪽 원의 색을 덮지 않는다.
            const int segments = 96;
            Vector3[] quad = new Vector3[4];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float b = (i + 1) * Mathf.PI * 2f / segments;
                Vector3 from = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 to = new Vector3(Mathf.Cos(b), 0f, Mathf.Sin(b));
                quad[0] = center + from * inner;
                quad[1] = center + from * outer;
                quad[2] = center + to * outer;
                quad[3] = center + to * inner;
                UnityEditor.Handles.DrawAAConvexPolygon(quad);
            }
        }

        UnityEditor.Handles.color = color;
        if (inner > 0f) UnityEditor.Handles.DrawWireDisc(center, Vector3.up, inner);
        UnityEditor.Handles.DrawWireDisc(center, Vector3.up, outer);

        string lower = GetRangeGizmoPattern(inner) == pattern ? "≤" : "<";
        string upper = GetRangeGizmoPattern(outer) == pattern ? "≤" : "<";
        var style = new GUIStyle(UnityEditor.EditorStyles.boldLabel);
        style.normal.textColor = color;
        Vector3 labelPosition = center + Vector3.right * ((inner + outer) * 0.5f);
        UnityEditor.Handles.Label(labelPosition,
            $"{label}\n{inner:0.##} {lower} 거리 {upper} {outer:0.##}m", style);
    }
#endif

}
