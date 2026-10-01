using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>개발 전용. 병합 후 CHG 입력 체계로 교체하면서 삭제할 총·수류탄 시험 입력입니다.</summary>
public class YPH_CombatTestInput : MonoBehaviour
{
    private enum Equipped { Gun, Grenade }
    [SerializeField, Tooltip("좌클릭 발사와 R 충전 요청을 받을 총입니다. 수류탄 장착 중에는 이 총 오브젝트만 꺼집니다.")]
    private YPH_SteamGun _gun;
    [SerializeField, Tooltip("G 제작과 수류탄 좌클릭 요청을 받을 컴포넌트입니다. 장비 전환으로 끄지 않아 제작이 계속됩니다.")]
    private YPH_SteamGrenadeThrower _thrower;
    [SerializeField, Tooltip("Tab 전환 때 손 구 표시를 갱신할 컴포넌트입니다.")]
    private YPH_GrenadeHandView _grenadeHandView;
    [SerializeField, Tooltip("좌우 방향키로 돌릴 루트입니다. 플레이어와 카메라가 함께 이 아래에 있어야 합니다.")]
    private Transform _aimRig;
    [SerializeField, Tooltip("위아래 방향키로 회전시킬 카메라입니다. 총과 손 위치도 이 카메라 아래에 둡니다.")]
    private Transform _aimCamera;
    [SerializeField, Min(0f), Tooltip("방향키를 누르는 동안 1초에 회전하는 각도입니다. 높이면 빠르게 조준하며 0이면 회전하지 않습니다.")]
    private float _lookSpeed = 90f;
    [SerializeField, Range(0f, 89f), Tooltip("카메라가 위아래로 돌아갈 최대 각도입니다. 낮추면 조준 가능한 세로 범위가 좁아집니다.")]
    private float _pitchLimit = 60f;
    private Equipped _equipped;
    private float _pitch;

    private void Awake()
    {
        if (_gun == null || _thrower == null || _grenadeHandView == null || _aimRig == null || _aimCamera == null)
        {
            Debug.LogError("YPH_CombatTestInput: 총·투척·손 표시·조준 루트·카메라를 연결하세요.", this);
            enabled = false;
            return;
        }
        _pitch = Mathf.DeltaAngle(0f, _aimCamera.localEulerAngles.x);
    }

    private void Start()
    {
        ApplyEquipment(); // 모든 컴포넌트의 Awake가 끝난 뒤 처음 장비를 표시합니다.
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null) return;
        RotateAim(keyboard);
        if (keyboard.tabKey.wasPressedThisFrame)
        {
            _equipped = _equipped == Equipped.Gun ? Equipped.Grenade : Equipped.Gun;
            ApplyEquipment();
        }
        if (keyboard.gKey.wasPressedThisFrame) LogCraft(_thrower.TryCraft());
        if (keyboard.rKey.wasPressedThisFrame && _equipped == Equipped.Gun) LogCharge(_gun.TryChargePouch());
        if (!mouse.leftButton.wasPressedThisFrame) return;
        if (_equipped == Equipped.Gun) LogFire(_gun.TryFire());
        else LogThrow(_thrower.TryThrow());
        // 우클릭은 3단계 조준에 남겨 둡니다. 입력은 이 파일만 알고 무기는 메서드 요청만 받습니다.
    }

    private void ApplyEquipment()
    {
        _gun.gameObject.SetActive(_equipped == Equipped.Gun);
        _grenadeHandView.SetEquipped(_equipped == Equipped.Grenade);
    }

    private void RotateAim(Keyboard keyboard)
    {
        float step = _lookSpeed * Time.deltaTime;
        float yaw = (keyboard.rightArrowKey.isPressed ? 1f : 0f) - (keyboard.leftArrowKey.isPressed ? 1f : 0f);
        float pitch = (keyboard.downArrowKey.isPressed ? 1f : 0f) - (keyboard.upArrowKey.isPressed ? 1f : 0f);
        _aimRig.Rotate(Vector3.up, yaw * step, Space.World);
        _pitch = Mathf.Clamp(_pitch + pitch * step, -_pitchLimit, _pitchLimit);
        _aimCamera.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    // 인수를 받는 로그 함수로 분리합니다. 빌드에서 로그가 빠져도 호출 인수인 실제 공격은 반드시 실행해야 합니다.
    private void LogFire(bool fired) { Log($"발사 {fired}, 약실 {(_gun.ChamberLoaded ? 1 : 0)}, 주머니 {_gun.PouchRounds}/{_gun.PouchCapacity}, 상태 {_gun.CurrentState}"); }
    private void LogCharge(bool started) { Log($"주머니 충전 {started}, 상태 {_gun.CurrentState}"); }
    private void LogCraft(bool started) { Log($"수류탄 제작 {started}, 소지 {_thrower.HeldCount}/{_thrower.MaxHeld}"); }
    private void LogThrow(bool thrown) { Log($"수류탄 투척 {thrown}, 소지 {_thrower.HeldCount}/{_thrower.MaxHeld}"); }

    [System.Diagnostics.Conditional("UNITY_EDITOR")]
    private void Log(string message) { Debug.Log(message, this); }
}
