using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BHS_4LegWalks : MonoBehaviour
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

    static Vector3[] MatchToSurfaceFromAbove(Vector3 point, float halfRange, Vector3 up)
    {
        Vector3[] res = new Vector3[2];
        RaycastHit hit;
        Ray ray = new Ray(point + halfRange * up, -up);

        if (Physics.Raycast(ray, out hit, 2f * halfRange))
        {
            res[0] = hit.point;
            res[1] = hit.normal;
        }
        else
        {
            res[0] = point;
        }
        return res;
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

            Vector3[] positionAndNormal = MatchToSurfaceFromAbove(targetPoint, raycastRange, transform.up);
            legMoving[0] = true;
            StartCoroutine(PerformStep(indexToMove, positionAndNormal[0]));
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
