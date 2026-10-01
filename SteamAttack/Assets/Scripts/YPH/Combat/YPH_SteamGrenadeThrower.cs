using System;
using UnityEngine;

/// <summary>증기로 수류탄을 제작하고 소지한 수류탄을 손 위치에서 던집니다.</summary>
public class YPH_SteamGrenadeThrower : MonoBehaviour
{
    [SerializeField, Tooltip("제작 비용을 낼 탱크입니다. 던질 때는 증기를 추가로 쓰지 않습니다.")]
    private YPH_SteamTank _tank;
    [SerializeField, Tooltip("던진 수류탄이 적을 맞혔을 때 알려 줄 증기 회복 입구입니다.")]
    private YPH_SteamHitRefill _hitRefill;
    [SerializeField, Tooltip("던질 때 복제할 수류탄입니다. 기폭·피해·물리는 이 프리팹에서 조절합니다.")]
    private YPH_Grenade _grenadePrefab;
    [SerializeField, Tooltip("수류탄이 출발할 손 위치입니다. 손 구가 숨겨져 있어도 Transform 위치는 쓸 수 있습니다.")]
    private Transform _handPoint;
    [SerializeField, Tooltip("앞 방향으로 던질 기준입니다. 조준 카메라를 연결합니다.")]
    private Transform _aimOrigin;
    [SerializeField, Tooltip("비행 중 충돌을 무시할 플레이어 루트입니다. 폭발 범위 피해는 그대로 받습니다.")]
    private Transform _ignoreRoot;
    [SerializeField, Min(0f), Tooltip("한 개 제작을 시작할 때 즉시 낼 증기입니다. 전액이 없으면 제작하지 않으며 0이면 무료입니다.")]
    private float _craftSteamCost = 40f;
    [SerializeField, Min(0f), Tooltip("제작 시작부터 소지 수 증가까지의 초입니다. 0이면 즉시 지급하고 진행 중 변경도 반영합니다.")]
    private float _craftTime = 1f;
    [SerializeField, Min(1), Tooltip("소지 상한입니다. 1이면 단개 방식입니다. 실행 중 낮춰도 가진 수류탄과 이미 시작한 제작은 버리지 않습니다.")]
    private int _maxHeldGrenades = 3;
    [SerializeField, Min(0f), Tooltip("던진 뒤 다시 던질 때까지의 초입니다. 0이면 입력할 때마다 던질 수 있으며 진행 중 변경도 반영합니다.")]
    private float _throwInterval = 0.5f;
    [SerializeField, Min(0f), Tooltip("조준 방향으로 주는 초기 속도(m/s)입니다. 높이면 더 멀리 날아갑니다.")]
    private float _throwSpeed = 12f;
    [SerializeField, Tooltip("위로 추가하는 속도(m/s)입니다. 높이면 더 높이 뜨고 음수면 아래로 던집니다.")]
    private float _upwardBoost = 2f;

    private int _heldCount;
    private float _craftTimer;
    private bool _isCrafting;
    private float _lastThrowTime = float.NegativeInfinity;

    /// <summary>제작이 끝나서 지금 던질 수 있는 소지 수입니다.</summary>
    public int HeldCount => _heldCount;
    /// <summary>현재 설정된 제작 상한입니다.</summary>
    public int MaxHeld => Mathf.Max(1, _maxHeldGrenades);
    /// <summary>증기는 지불했지만 아직 수류탄을 받지 못한 제작 단계인지입니다.</summary>
    public bool IsCrafting => _isCrafting;
    /// <summary>소지 수가 바뀌면 현재 수와 상한을 알립니다. 손 구 표시가 구독합니다.</summary>
    public event Action<int, int> OnHeldCountChanged;

    private void Awake()
    {
        if (_tank != null && _hitRefill != null && _grenadePrefab != null && _handPoint != null && _aimOrigin != null && _ignoreRoot != null) return;
        Debug.LogError("YPH_SteamGrenadeThrower: 탱크·회복·프리팹·손·조준·플레이어를 모두 연결하세요.", this);
        enabled = false;
    }

    private void Update()
    {
        if (!_isCrafting) return;
        _craftTimer += Time.deltaTime;
        FinishCraftIfReady();
    }

    /// <summary>상한과 진행 여부를 먼저 확인하고 비용을 전액 낸 경우에만 한 개 제작을 시작합니다.</summary>
    public bool TryCraft()
    {
        if (!isActiveAndEnabled || _isCrafting || _heldCount >= MaxHeld) return false;
        if (!_tank.TryConsume(_craftSteamCost)) return false;
        _isCrafting = true;
        _craftTimer = 0f;
        FinishCraftIfReady();
        return true;
    }

    /// <summary>소지 한 개를 손 위치에서 던집니다. 제작 중이거나 투척 간격이 지나지 않았으면 실패합니다.</summary>
    public bool TryThrow()
    {
        if (!isActiveAndEnabled || _isCrafting || _heldCount <= 0 || Time.time - _lastThrowTime < _throwInterval) return false;
        // ponytail: 투척 빈도가 낮은 시제품이라 Instantiate/Destroy를 씁니다. 동시 투척 수가 많아지면 풀링합니다.
        YPH_Grenade grenade = Instantiate(_grenadePrefab, _handPoint.position, _handPoint.rotation);
        grenade.Launch(_aimOrigin.forward * _throwSpeed + Vector3.up * _upwardBoost, _hitRefill, _ignoreRoot);
        _lastThrowTime = Time.time;
        _heldCount--;
        OnHeldCountChanged?.Invoke(_heldCount, MaxHeld);
        return true;
    }

    private void FinishCraftIfReady()
    {
        if (_craftTimer < Mathf.Max(0f, _craftTime)) return;
        _isCrafting = false;
        _heldCount++; // 제작 중 상한을 낮춰도 이미 지불한 한 개는 지급합니다.
        OnHeldCountChanged?.Invoke(_heldCount, MaxHeld);
    }
}
