using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>3인칭 백사이드뷰 카메라. 마우스로 회전, 락온 시 대상 쪽으로 자동 회전.</summary>
public class chg_ThirdPersonCamera : MonoBehaviour
{
    public Transform target;                 // 플레이어
    public chg_PlayerController player;
    public float pivotHeight = 0.85f;        // 어깨 높이 (캐릭터 키 약 1m 기준)
    public float distance = 2.4f;
    public float shoulderOffset = 0.25f;     // 오른쪽 어깨 너머
    public float mouseSensitivity = 0.12f;
    public float minPitch = -25f, maxPitch = 60f;
    public float lockOnTurnSpeed = 8f;
    public float collisionRadius = 0.15f;

    float _yaw, _pitch = 15f;

    // ---- 시간 역행(NGH_TimeRewind) 연동 — NGH(남귀훈) 추가
    /// <summary>현재 카메라 회전 (x = 좌우 yaw, y = 상하 pitch)</summary>
    public Vector2 ViewAngles => new Vector2(_yaw, _pitch);

    /// <summary>카메라 회전을 즉시 지정 (pitch는 Min/Max Pitch 안으로 제한)</summary>
    public void SetViewAngles(float yaw, float pitch)
    {
        _yaw = yaw;
        _pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
    }

    void Start()
    {
        // 프리팹으로 넣었을 때: 씬에서 플레이어를 자동으로 찾음
        if (!target)
        {
            var pc = FindFirstObjectByType<chg_PlayerController>();
            if (pc) { target = pc.transform; player = pc; }
        }
        if (target) _yaw = target.eulerAngles.y;
        if (!player && target) player = target.GetComponent<chg_PlayerController>();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void LateUpdate()
    {
        if (!target) return;
        var mouse = Mouse.current;
        bool guideOpen = chg_ControlsOverlay.IsOpen;   // ESC 안내가 열려 있으면 카메라 조작 멈춤

        if (!guideOpen && mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
        }

        Vector3 pivot = target.position + Vector3.up * pivotHeight;
        var lockT = player ? player.LockTarget : null;

        if (lockT)
        {
            // 플레이어 뒤에서 대상을 바라보도록
            Vector3 to = lockT.AimPoint - pivot;
            float wantYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float wantPitch = Mathf.Clamp(-Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg + 12f, minPitch, maxPitch);
            float k = 1f - Mathf.Exp(-lockOnTurnSpeed * Time.deltaTime);
            _yaw = Mathf.LerpAngle(_yaw, wantYaw, k);
            _pitch = Mathf.Lerp(_pitch, wantPitch, k);
        }
        else if (!guideOpen && mouse != null && Cursor.lockState == CursorLockMode.Locked)
        {
            Vector2 d = mouse.delta.ReadValue() * mouseSensitivity;
            _yaw += d.x;
            _pitch = Mathf.Clamp(_pitch - d.y, minPitch, maxPitch);
        }

        Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
        Vector3 shoulder = pivot + rot * Vector3.right * shoulderOffset;
        Vector3 wantPos = shoulder - rot * Vector3.forward * distance;

        // 벽에 카메라가 파묻히지 않게
        Vector3 dir = wantPos - shoulder;
        if (Physics.SphereCast(shoulder, collisionRadius, dir.normalized, out var hit, dir.magnitude,
                               ~0, QueryTriggerInteraction.Ignore)
            && !hit.transform.IsChildOf(target))
            wantPos = shoulder + dir.normalized * Mathf.Max(0.2f, hit.distance);

        transform.SetPositionAndRotation(wantPos, rot);
    }
}
