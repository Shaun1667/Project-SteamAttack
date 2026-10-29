using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class FourLegWalk : MonoBehaviour
{
    [Header("왼쪽 앞발 Target")]
    public Transform leftFrontTarget;

    [Header("오른쪽 앞발 Target")]
    public Transform rightFrontTarget;

    [Header("왼쪽 뒷발 Target")]
    public Transform leftRearTarget;

    [Header("오른쪽 뒷발 Target")]
    public Transform rightRearTarget;

    private Transform[] legTargets;
    public float stepSize = 1f;
    public float smoothness = 1;
    public float stepHeight = 0.1f;

    [Header("지면 탐색")]
    [Tooltip("발을 디딜 바닥 레이어만 선택하세요. 비어 있으면 Ground 레이어를 사용합니다.")]
    public LayerMask groundMask;
    [Tooltip("발판에서 제외할 자신의 최상위 오브젝트입니다. 비어 있으면 Transform Root를 사용합니다.")]
    public Transform selfCollisionRoot;

    private readonly RaycastHit[] groundHits = new RaycastHit[32];
    private float raycastRange = 1f;
    private Vector3[] defaultLegPositions;
    private Vector3[] lastLegPositions;
    private Vector3 lastBodyUp;
    private bool[] legMoving;
    private int nbLegs;

    private Vector3 velocity;
    private Vector3 lastVelocity;
    private Vector3 lastBodyPos;

    private float velocityMultiplier = 15f;

    bool TryMatchToSurfaceFromAbove(Vector3 point, float halfRange, Vector3 up, out Vector3 targetPoint)
    {
        targetPoint = point;
        Ray ray = new Ray(point + halfRange * up, -up);
        int count = Physics.RaycastNonAlloc(ray, groundHits, 2f * halfRange,
            groundMask, QueryTriggerInteraction.Ignore);
        RaycastHit[] candidates = groundHits;
        // A full buffer may omit the valid floor behind ignored colliders.
        if (count == groundHits.Length)
        {
            candidates = Physics.RaycastAll(ray, 2f * halfRange, groundMask, QueryTriggerInteraction.Ignore);
            count = candidates.Length;
        }

        float nearest = float.PositiveInfinity;
        Transform ownRoot = selfCollisionRoot != null ? selfCollisionRoot : transform.root;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = candidates[i];
            if (hit.collider == null || hit.transform.IsChildOf(ownRoot) ||
                IsPlayerSurface(hit.collider) || hit.distance >= nearest) continue;
            nearest = hit.distance;
            targetPoint = hit.point;
        }
        return nearest < float.PositiveInfinity;
    }

    static bool IsPlayerSurface(Collider collider)
    {
        if (collider.GetComponentInParent<CharacterController>() != null) return true;
        for (Transform current = collider.transform; current != null; current = current.parent)
            if (current.CompareTag("Player")) return true;
        return false;
    }

    void Reset()
    {
        groundMask = LayerMask.GetMask("Ground");
    }

    void Start()
    {
        lastBodyUp = transform.up;

        nbLegs = legTargets.Length;
        defaultLegPositions = new Vector3[nbLegs];
        lastLegPositions = new Vector3[nbLegs];
        legMoving = new bool[nbLegs];
        for (int i = 0; i < nbLegs; ++i)
        {
            defaultLegPositions[i] = legTargets[i].localPosition;
            lastLegPositions[i] = legTargets[i].position;
            legMoving[i] = false;
        }
        lastBodyPos = transform.position;
    }

    IEnumerator PerformStep(int index, Vector3 targetPoint)
    {
        Vector3 startPos = lastLegPositions[index];
        float dynamicSmoothness = Mathf.Max(1, smoothness - velocity.magnitude * 0.2f); // 속도 증가에 따라 다리 이동 빠르게

        for (int i = 1; i <= dynamicSmoothness; ++i)
        {
            float t = i / (float)(dynamicSmoothness + 1f);
            legTargets[index].position = Vector3.Lerp(startPos, targetPoint, t);
            legTargets[index].position += transform.up * Mathf.Sin(t * Mathf.PI) * stepHeight;
            yield return new WaitForFixedUpdate();
        }

        legTargets[index].position = targetPoint;
        lastLegPositions[index] = legTargets[index].position;
        legMoving[0] = false;
    }

    void FixedUpdate()
    {
        velocity = transform.position - lastBodyPos;
        velocity = (velocity + smoothness * lastVelocity) / (smoothness + 1f);

        if (velocity.magnitude < 0.000025f)
            velocity = lastVelocity;
        else
            lastVelocity = velocity;

        Vector3[] desiredPositions = new Vector3[nbLegs];
        int indexToMove = -1;
        float maxDistance = stepSize;
        for (int i = 0; i < nbLegs; ++i)
        {
            desiredPositions[i] = transform.TransformPoint(defaultLegPositions[i]);

            float distance = Vector3.ProjectOnPlane(desiredPositions[i] + velocity * velocityMultiplier - lastLegPositions[i], transform.up).magnitude;
            if (distance > maxDistance)
            {
                maxDistance = distance;
                indexToMove = i;
            }
        }
        for (int i = 0; i < nbLegs; ++i)
            if (i != indexToMove)
                legTargets[i].position = lastLegPositions[i];

        if (indexToMove != -1 && !legMoving[0])
        {
            //Vector3 targetPoint = desiredPositions[indexToMove] + Mathf.Clamp(velocity.magnitude * velocityMultiplier, 0.0f, 1.5f) * (desiredPositions[indexToMove] - legTargets[indexToMove].position) + velocity * velocityMultiplier;
            float maxStepDistance = stepSize + Mathf.Clamp(velocity.magnitude * 0.5f, 0.0f, stepSize * 2f);
            Vector3 targetPoint = desiredPositions[indexToMove] + Mathf.Clamp(velocity.magnitude * velocityMultiplier, 0.0f, maxStepDistance) *
                                  (desiredPositions[indexToMove] - legTargets[indexToMove].position).normalized;

            Vector3 groundPoint;
            if (TryMatchToSurfaceFromAbove(targetPoint, raycastRange, transform.up, out groundPoint))
            {
                legMoving[0] = true;
                StartCoroutine(PerformStep(indexToMove, groundPoint));
            }
        }

        lastBodyPos = transform.position;
    }

    private void OnDrawGizmosSelected()
    {
        for (int i = 0; i < nbLegs; ++i)
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(legTargets[i].position, 0.05f);
            Gizmos.color = Color.green;
            Gizmos.DrawWireSphere(transform.TransformPoint(defaultLegPositions[i]), stepSize);
        }
    }

    void OnEnable()
    {
        if (groundMask.value == 0) groundMask = LayerMask.GetMask("Ground");
        legTargets = new Transform[]
        {
            leftFrontTarget,
            rightFrontTarget,
            leftRearTarget,
            rightRearTarget
        };

        if (legTargets == null || legTargets.Length == 0)
            return;

        if (defaultLegPositions == null || defaultLegPositions.Length != legTargets.Length)
        {
            nbLegs = legTargets.Length;
            defaultLegPositions = new Vector3[nbLegs];
            lastLegPositions = new Vector3[nbLegs];
            legMoving = new bool[nbLegs];

            for (int i = 0; i < nbLegs; ++i)
            {
                defaultLegPositions[i] = legTargets[i].localPosition;
            }
        }

        for (int i = 0; i < nbLegs; ++i)
        {
            // 다리를 원래 위치로 복구
            legTargets[i].localPosition = defaultLegPositions[i];
            lastLegPositions[i] = legTargets[i].position;
            legMoving[i] = false;
        }

        lastBodyPos = transform.position;
        velocity = Vector3.zero;
        lastVelocity = Vector3.zero;
    }

    void OnDisable()
    {
        StopAllCoroutines(); // 움직이던 다리 멈춤
    }
}
