using UnityEngine;

/// <summary>총 프리팹 루트에서 실제 충돌 지점에 명중 불꽃을 표시합니다. 같은 총과 적·표면용 파티클 두 개를 연결합니다.</summary>
public class YPH_GunImpactView : MonoBehaviour
{
    [SerializeField, Tooltip("충돌 이벤트를 받을 총입니다. 아무것도 맞지 않은 발사에는 이벤트가 오지 않습니다.")]
    private YPH_SteamGun _gun;
    [SerializeField, Tooltip("살아 있는 적·보스에게 피해를 준 자리에서 재생할 백·황 이펙트입니다.")]
    private ParticleSystem _enemyImpact;
    [SerializeField, Tooltip("바닥·벽 등 그 밖의 충돌에서 재생할 불똥·먼지 이펙트입니다.")]
    private ParticleSystem _surfaceImpact;

    /// <summary>총과 두 종류의 명중 표시가 모두 있어야 이벤트를 받도록 연결을 검사합니다.</summary>
    private void Awake()
    {
        if (_gun != null && _enemyImpact != null && _surfaceImpact != null)
        {
            return;
        }
        Debug.LogError("YPH_GunImpactView: 총과 적·표면용 파티클을 모두 연결하세요.", this);
        enabled = false;
    }

    /// <summary>활성화된 동안만 총의 충돌 이벤트를 받습니다.</summary>
    private void OnEnable()
    {
        if (_gun != null)
        {
            _gun.OnImpact += HandleImpact;
        }
    }

    /// <summary>장비가 숨겨질 때 재사용 중인 두 이펙트를 정리합니다.</summary>
    private void OnDisable()
    {
        if (_gun != null)
        {
            _gun.OnImpact -= HandleImpact;
        }
        if (_enemyImpact != null)
        {
            _enemyImpact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        if (_surfaceImpact != null)
        {
            _surfaceImpact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    /// <summary>면 바깥쪽으로 입자가 튀도록 법선 방향으로 회전시킨 뒤 재시작합니다.</summary>
    private void HandleImpact(Vector3 point, Vector3 normal, bool hitEnemy)
    {
        ParticleSystem effect = hitEnemy ? _enemyImpact : _surfaceImpact;
        if (effect == null)
        {
            return;
        }
        effect.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        effect.transform.SetPositionAndRotation(point, Quaternion.LookRotation(normal));
        effect.Play(true);
    }
}
