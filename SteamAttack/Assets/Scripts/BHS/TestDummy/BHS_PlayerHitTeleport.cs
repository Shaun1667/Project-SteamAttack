using System.Collections.Generic;
using UnityEngine;

public class BHS_PlayerHitTeleport : MonoBehaviour
{
    [Header("순간이동 거리 목록 - 보스와의 수평 거리")]
    [Tooltip("목록의 항목 중 하나를 같은 확률로 선택합니다.")]
    [Min(0f)] public List<float> teleportDistances = new List<float> { 2f, 4f };

    private Transform boss;
    private CharacterController characterController;
    private Rigidbody body;

    private void Awake()
    {
        characterController = GetComponent<CharacterController>();
        body = GetComponent<Rigidbody>();
    }

    // 공격이 명중하면 BossFSM에서 직접 호출한다.
    public void TakeDamage(float damage)
    {
        Debug.Log($"[피격 수신] {name} / 활성 상태: {isActiveAndEnabled}", this);
        if (!isActiveAndEnabled) return;

        if (teleportDistances == null || teleportDistances.Count == 0)
        {
            Debug.LogWarning("Teleport Distances에 거리를 하나 이상 넣어 주세요.", this);
            return;
        }

        if (boss == null || !boss.gameObject.activeInHierarchy)
        {
            GameObject found = GameObject.FindGameObjectWithTag("Boss");
            boss = found != null ? found.transform : null;
        }

        if (boss == null)
        {
            Debug.LogWarning("Boss 태그를 가진 오브젝트가 없습니다.", this);
            return;
        }

        // 360도 중 랜덤 방향.
        float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
        Vector3 direction = new Vector3(
            Mathf.Sin(angle),
            0f,
            Mathf.Cos(angle)
        );

        // Inspector의 거리 목록에서 항목 하나를 같은 확률로 선택한다.
        int index = Random.Range(0, teleportDistances.Count);
        float distance = Mathf.Max(0f, teleportDistances[index]);
        Vector3 destination = boss.position + direction * distance;

        // 현재 높이를 유지해 바닥에 파묻히는 것을 피한다.
        destination.y = transform.position.y;

        bool controllerWasEnabled =
            characterController != null && characterController.enabled;

        if (controllerWasEnabled)
            characterController.enabled = false;

        if (body != null)
        {
            if (!body.isKinematic)
            {
#if UNITY_6000_0_OR_NEWER
                body.linearVelocity = Vector3.zero;
#else
                body.velocity = Vector3.zero;
#endif
                body.angularVelocity = Vector3.zero;
            }

            body.position = destination;
        }
        else
        {
            transform.position = destination;
        }

        if (controllerWasEnabled)
            characterController.enabled = true;
    }

   
}
