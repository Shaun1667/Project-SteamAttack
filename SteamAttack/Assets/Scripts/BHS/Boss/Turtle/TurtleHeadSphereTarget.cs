using System;
using System.Collections;
using UnityEngine;

[RequireComponent(typeof(SphereCollider))]
[DefaultExecutionOrder(100)]
public class TurtleHeadSphereTarget : MonoBehaviour
{
    [Header("기존 HeadTa - Chain IK도 같은 타겟 사용")]
    public Transform ikTarget;

    [Header("애니메이션 시작 위치에 배치한 빈 오브젝트")]
    public Transform animationStartPoint;

    [Header("애니메이션 시작 위치까지 이동하는 시간")]
    [Min(0.01f)] public float alignDuration = 0.3f;

    [Header("애니메이션 후 플레이어 추적으로 돌아오는 시간")]
    [Min(0.01f)] public float returnDuration = 0.4f;

    [Header("플레이어 위치에서 추가할 높이")]
    public float playerHeightOffset = 0f;

    [Header("Y축 제한 - 좌우")]
    [Range(-180f, 180f)] public float minYAngle = -100f;
    [Range(-180f, 180f)] public float maxYAngle = 100f;

    [Header("X축 제한 - 수평 0, 아래쪽 90")]
    [Range(-90f, 90f)] public float minXAngle = 0f;
    [Range(-90f, 90f)] public float maxXAngle = 90f;

    [Header("따라가는 속도 - 초당 각도")]
    [Min(1f)] public float angleSpeed = 180f;

    private Transform player;
    private SphereCollider sphere;
    private float currentYAngle;
    private float currentXAngle;
    private bool animationControl;
    private Coroutine transition;

    public bool IsAlignedForRush { get; private set; }

    private void Awake()
    {
        sphere = GetComponent<SphereCollider>();
        sphere.isTrigger = true;

        GameObject playerObject = GameObject.FindWithTag("Player");
        if (playerObject != null) player = playerObject.transform;

        if (player == null || ikTarget == null)
        {
            Debug.LogError("Player 태그와 기존 HeadTa 연결을 확인해 주세요.", this);
            enabled = false;
            return;
        }

        if (ikTarget == transform || transform.IsChildOf(ikTarget))
        {
            Debug.LogError("HeadTaRoot를 HeadTa의 자식으로 두면 안 됩니다.", this);
            enabled = false;
            return;
        }

        currentYAngle = Mathf.Clamp(0f, minYAngle, maxYAngle);
        currentXAngle = Mathf.Clamp(0f, minXAngle, maxXAngle);
    }

    private void Update()
    {
        // 준비부터 닫기 완료까지 평소의 플레이어 추적은 멈춘다.
        if (animationControl || player == null || ikTarget == null) return;
        ikTarget.position = GetTrackingPosition();
    }

    // 돌진 준비 시작: 타겟을 정렬한 뒤 Bool을 켜는 함수를 호출한다.
    public bool BeginRush(Action onAligned)
    {
        if (!isActiveAndEnabled || ikTarget == null) return false;

        if (animationStartPoint == null)
        {
            Debug.LogWarning("Animation Start Point에 기준점 오브젝트를 연결해 주세요.", this);
            return false;
        }

        if (animationStartPoint == ikTarget ||
            animationStartPoint.IsChildOf(ikTarget))
        {
            Debug.LogWarning("기준점은 HeadTa와 별개로 두고 HeadTa의 자식으로 넣지 마세요.", this);
            return false;
        }

        StopTransition();
        animationControl = true;
        IsAlignedForRush = false;
        transition = StartCoroutine(AlignThenPlay(onAligned));
        return true;
    }

    private IEnumerator AlignThenPlay(Action onAligned)
    {
        Vector3 startLocal = ikTarget.localPosition;
        float elapsed = 0f;
        float duration = Mathf.Max(0.01f, alignDuration);

        while (elapsed < duration)
        {
            if (ikTarget == null || animationStartPoint == null) yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / duration));

            Vector3 startWorld = ikTarget.parent != null
                ? ikTarget.parent.TransformPoint(startLocal)
                : startLocal;

            ikTarget.position =
                Vector3.Lerp(startWorld, animationStartPoint.position, t);

            yield return null;
        }

        if (ikTarget == null || animationStartPoint == null) yield break;
        ikTarget.position = animationStartPoint.position;

        IsAlignedForRush = true;
        transition = null;
        onAligned?.Invoke();

        // 여기부터는 RushAf/RushBe가 같은 HeadTa를 직접 움직인다.
        // 스크립트는 닫기 대기가 끝날 때까지 타겟 위치를 쓰지 않는다.
    }

    // 돌진 종료: Recovery Time의 마지막 구간을 추적 복귀에 사용한다.
    public void EndRush(float recoveryDuration)
    {
        if (!isActiveAndEnabled || ikTarget == null) return;

        StopTransition();
        animationControl = true;
        IsAlignedForRush = false;
        transition = StartCoroutine(WaitAndReturn(recoveryDuration));
    }

    private IEnumerator WaitAndReturn(float recoveryDuration)
    {
        float duration = Mathf.Max(0.01f, returnDuration);
        float wait = Mathf.Max(0f, recoveryDuration - duration);

        // 이 시간 동안 Bool은 false이고 RushBe가 재생된다.
        if (wait > 0f) yield return new WaitForSeconds(wait);
        if (ikTarget == null) yield break;

        // 실제 애니메이션 종료 위치에서 이어서 움직인다.
        Vector3 startLocal = ikTarget.localPosition;
        GetAngles(ikTarget.position, out float y, out float x);
        currentYAngle = Mathf.Clamp(y, minYAngle, maxYAngle);
        currentXAngle = Mathf.Clamp(x, minXAngle, maxXAngle);

        float elapsed = 0f;

        while (elapsed < duration)
        {
            if (ikTarget == null) yield break;

            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f,
                Mathf.Clamp01(elapsed / duration));

            // 보스가 이동/회전해도 시작 위치가 몸을 따라가게 한다.
            Vector3 startWorld = ikTarget.parent != null
                ? ikTarget.parent.TransformPoint(startLocal)
                : startLocal;

            ikTarget.position =
                Vector3.Lerp(startWorld, GetTrackingPosition(), t);

            yield return null;
        }

        animationControl = false;
        transition = null;
    }

    private Vector3 GetTrackingPosition()
    {
        if (player != null)
        {
            GetAngles(player.position + Vector3.up * playerHeightOffset,
                out float wantedY, out float wantedX);

            wantedY = Mathf.Clamp(wantedY, minYAngle, maxYAngle);
            wantedX = Mathf.Clamp(wantedX, minXAngle, maxXAngle);
            float step = angleSpeed * Time.deltaTime;

            currentYAngle = Mathf.Clamp(
                Mathf.MoveTowards(currentYAngle, wantedY, step),
                minYAngle, maxYAngle);

            currentXAngle = Mathf.Clamp(
                Mathf.MoveTowards(currentXAngle, wantedX, step),
                minXAngle, maxXAngle);
        }

        Vector3 direction = transform.rotation
            * Quaternion.Euler(currentXAngle, currentYAngle, 0f)
            * Vector3.forward;

        Vector3 scale = transform.lossyScale;
        float largestScale = Mathf.Max(Mathf.Abs(scale.x),
            Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z)));

        return transform.TransformPoint(sphere.center)
            + direction * sphere.radius * largestScale;
    }

    private void GetAngles(Vector3 worldPoint, out float y, out float x)
    {
        y = currentYAngle;
        x = currentXAngle;

        Vector3 direction = Quaternion.Inverse(transform.rotation)
            * (worldPoint - transform.TransformPoint(sphere.center));

        if (direction.sqrMagnitude < 0.000001f) return;

        float horizontal = Mathf.Sqrt(
            direction.x * direction.x + direction.z * direction.z);

        if (horizontal > 0.0001f)
            y = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;

        x = Mathf.Atan2(-direction.y, horizontal) * Mathf.Rad2Deg;
    }

    private void StopTransition()
    {
        if (transition != null) StopCoroutine(transition);
        transition = null;
    }

    private void OnDisable()
    {
        StopTransition();
        animationControl = false;
        IsAlignedForRush = false;
    }

    private void OnValidate()
    {
        minYAngle = Mathf.Clamp(minYAngle, -180f, 180f);
        maxYAngle = Mathf.Clamp(maxYAngle, minYAngle, 180f);
        minXAngle = Mathf.Clamp(minXAngle, -90f, 90f);
        maxXAngle = Mathf.Clamp(maxXAngle, minXAngle, 90f);
        alignDuration = Mathf.Max(0.01f, alignDuration);
        returnDuration = Mathf.Max(0.01f, returnDuration);
    }
}
