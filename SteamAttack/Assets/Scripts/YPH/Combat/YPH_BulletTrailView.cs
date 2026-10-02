using UnityEngine;

/// <summary>총 프리팹 루트에서 발사 끝점까지 이동하는 궤적을 표시합니다. 같은 총과 자식 TrailRenderer를 연결합니다.</summary>
public class YPH_BulletTrailView : MonoBehaviour
{
    [SerializeField, Tooltip("발사 성공 이벤트를 받을 총입니다. 피해 판정은 이미 끝났으며 이 컴포넌트는 표시만 합니다.")]
    private YPH_SteamGun _gun;
    [SerializeField, Tooltip("재사용할 궤적입니다. TrailRenderer의 기록된 점은 월드 좌표라 총을 움직여도 따라오지 않습니다.")]
    private TrailRenderer _trail;
    [SerializeField, Min(0f), Tooltip("궤적 끝이 초당 이동할 거리(m)입니다. 높이면 빨라지고 0이면 전체 선을 즉시 표시합니다. 피해 시점에는 영향이 없습니다.")]
    private float _speed = 250f;
    private Vector3 _position;
    private Vector3 _end;
    private bool _flying;

    /// <summary>필수 참조가 빠진 궤적을 조용히 생략하지 않고 설정 오류를 알립니다.</summary>
    private void Awake()
    {
        if (_gun != null && _trail != null)
        {
            return;
        }
        Debug.LogError("YPH_BulletTrailView: 총과 TrailRenderer를 연결하세요.", this);
        enabled = false;
    }

    /// <summary>총이 켜질 때 발사 이벤트를 구독합니다.</summary>
    private void OnEnable()
    {
        if (_gun != null)
        {
            _gun.OnFired += HandleFired;
        }
    }

    /// <summary>장비 전환 중 이전 궤적이 다시 보이지 않도록 지웁니다.</summary>
    private void OnDisable()
    {
        if (_gun != null)
        {
            _gun.OnFired -= HandleFired;
        }
        _flying = false;
        if (_trail == null)
        {
            return;
        }
        _trail.emitting = false;
        _trail.Clear();
    }

    /// <summary>이전 발사의 선을 지우고 이번 총구와 끝점을 저장합니다.</summary>
    private void HandleFired(Vector3 muzzle, Vector3 end, bool hitEnemy)
    {
        if (_trail == null)
        {
            return;
        }
        _trail.emitting = false;
        _trail.Clear();
        _position = muzzle;
        _end = end;
        _trail.transform.position = muzzle;
        _trail.AddPosition(muzzle);
        _flying = true;
        if (_speed <= 0f)
        {
            FinishTrail();
        }
        else
        {
            _trail.emitting = true;
        }
    }

    /// <summary>총의 부모가 움직여도 저장한 월드 위치에서 계속 비행합니다.</summary>
    private void LateUpdate()
    {
        if (!_flying || _trail == null)
        {
            return;
        }
        if (_speed <= 0f)
        {
            FinishTrail();
            return;
        }
        _position = Vector3.MoveTowards(_position, _end, _speed * Time.deltaTime);
        _trail.transform.position = _position;
        if (_position == _end)
        {
            FinishTrail();
        }
    }

    /// <summary>도착점을 반드시 기록한 뒤 방출을 멈추고 기존 꼬리는 지정된 시간 동안 사라지게 둡니다.</summary>
    private void FinishTrail()
    {
        _trail.transform.position = _end;
        _trail.AddPosition(_end);
        _trail.emitting = false;
        _flying = false;
    }
}
