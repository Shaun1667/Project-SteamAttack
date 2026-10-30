using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

// 빈 오브젝트에 추가하면 플레이어 몸체와 카메라를 자동으로 준비합니다.
[RequireComponent(typeof(CharacterController))]
public sealed class BossTestPlayer : MonoBehaviour
{
    [Header("이동")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float gravity = -20f;
    [Header("3인칭 카메라")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private float cameraDistance = 4f;
    [SerializeField] private float cameraHeight = 1.5f;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [Header("사격")]
    [SerializeField] private float damage = 25f;
    [SerializeField] private float range = 100f;
    [SerializeField, Min(0.01f)] private float shotInterval = 0.2f;
    [SerializeField] private LayerMask shootMask = ~0;

    private CharacterController controller;
    private float yaw;
    private float pitch = 15f;
    private float verticalSpeed;
    private float nextShot;
    private bool ownsCamera;

    private void Awake()
    {
        controller = GetComponent<CharacterController>();
        controller.height = 2f;
        controller.radius = 0.35f;
        controller.center = Vector3.up;
        gameObject.tag = "Player";
        yaw = transform.eulerAngles.y;

        if (GetComponentInChildren<Renderer>() == null)
        {
            GameObject body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "TestPlayerBody";
            body.transform.SetParent(transform, false);
            body.transform.localPosition = Vector3.up;
            Collider bodyCollider = body.GetComponent<Collider>();
            bodyCollider.enabled = false;
            Destroy(bodyCollider);
        }

        if (playerCamera == null) playerCamera = Camera.main;
        if (playerCamera == null)
        {
            var cameraObject = new GameObject("TestPlayerCamera");
            playerCamera = cameraObject.AddComponent<Camera>();
            cameraObject.tag = "MainCamera";
            ownsCamera = true;
        }
        LockCursor(true);
    }

    private void Update()
    {
        Vector2 move = Vector2.zero;
        Vector2 look = Vector2.zero;
        bool fire = false;
        bool click = false;
        bool escape = false;
#if ENABLE_INPUT_SYSTEM
        if (Keyboard.current != null)
        {
            move.x = (Keyboard.current.dKey.isPressed ? 1 : 0) - (Keyboard.current.aKey.isPressed ? 1 : 0);
            move.y = (Keyboard.current.wKey.isPressed ? 1 : 0) - (Keyboard.current.sKey.isPressed ? 1 : 0);
            escape = Keyboard.current.escapeKey.wasPressedThisFrame;
        }
        if (Mouse.current != null)
        {
            look = Mouse.current.delta.ReadValue();
            fire = Mouse.current.leftButton.isPressed;
            click = Mouse.current.leftButton.wasPressedThisFrame;
        }
#elif ENABLE_LEGACY_INPUT_MANAGER
        move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
        look = new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y")) * 15f;
        fire = Input.GetMouseButton(0);
        click = Input.GetMouseButtonDown(0);
        escape = Input.GetKeyDown(KeyCode.Escape);
#endif
        if (escape) LockCursor(false);
        if (Cursor.lockState != CursorLockMode.Locked)
        {
            if (click) LockCursor(true);
            return;
        }

        yaw += look.x * mouseSensitivity;
        pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, -35f, 70f);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Vector3 direction = transform.right * move.x + transform.forward * move.y;
        direction = Vector3.ClampMagnitude(direction, 1f);
        if (controller.isGrounded && verticalSpeed < 0f) verticalSpeed = -2f;
        verticalSpeed += gravity * Time.deltaTime;
        controller.Move((direction * moveSpeed + Vector3.up * verticalSpeed) * Time.deltaTime);

        UpdateCamera();
        if (fire && Time.time >= nextShot)
        {
            nextShot = Time.time + Mathf.Max(0.01f, shotInterval);
            Shoot();
        }
    }

    private void LateUpdate() => UpdateCamera();

    private void UpdateCamera()
    {
        if (playerCamera == null) return;
        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 pivot = transform.position + Vector3.up * cameraHeight;
        playerCamera.transform.SetPositionAndRotation(
            pivot - rotation * Vector3.forward * cameraDistance, rotation);
    }

    private void Shoot()
    {
        if (playerCamera == null) return;
        Ray ray = playerCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        // 자기 몸은 제외하고 가장 가까운 충돌을 선택합니다. 벽 뒤의 코어는 맞지 않습니다.
        RaycastHit[] hits = Physics.RaycastAll(ray, range, shootMask, QueryTriggerInteraction.Collide);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (RaycastHit hit in hits)
        {
            if (hit.collider.transform.IsChildOf(transform)) continue;
            Debug.DrawLine(ray.origin, hit.point, Color.red, 0.2f);
            BossCoreHealth core = hit.collider.GetComponentInParent<BossCoreHealth>();
            if (core != null && hit.collider.CompareTag("Boss"))
            {
                core.TakeDamage(damage, transform.position);
                Debug.Log($"코어 명중: {core.name} / HP {core.CurrentHp}/{core.MaxHp}", core);
            }
            return;
        }
        Debug.DrawRay(ray.origin, ray.direction * range, Color.yellow, 0.2f);
    }

    private void OnGUI()
    {
        GUI.Label(new Rect(Screen.width / 2f - 6f, Screen.height / 2f - 12f, 24f, 24f), "+");
        GUI.Label(new Rect(15f, 15f, 600f, 25f), "WASD: Move | Mouse: Look | Left Click: Fire | Esc: Cursor");
    }

    private static void LockCursor(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

    private void OnDisable() => LockCursor(false);

    private void OnDestroy()
    {
        if (ownsCamera && playerCamera != null) Destroy(playerCamera.gameObject);
    }
}
