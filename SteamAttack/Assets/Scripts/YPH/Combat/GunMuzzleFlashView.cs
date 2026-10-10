using UnityEngine;

namespace YPH
{
    /// <summary>총 프리팹 루트에서 발사 성공을 총구 파티클 재생으로 바꿉니다. 같은 총과 Muzzle 아래 파티클들을 연결합니다.</summary>
    public class GunMuzzleFlashView : MonoBehaviour
    {
        [SerializeField, Tooltip("발사 성공 이벤트를 받을 총입니다. 실패한 발사 요청에는 이펙트가 나오지 않습니다.")]
        private SteamGun _gun;
        [SerializeField, Tooltip("매 발사마다 처음부터 재생할 총구 파티클입니다. 자식 중복 재생을 피하도록 각각 한 번씩 넣습니다.")]
        private ParticleSystem[] _effects;

        /// <summary>빈 목록·누락 입자가 있으면 일부만 재생하지 않고 연결 오류를 먼저 알립니다.</summary>
        private void Awake()
        {
            bool connected = _gun != null && _effects != null && _effects.Length > 0;
            if (connected)
            {
                foreach (ParticleSystem effect in _effects)
                {
                    if (effect == null)
                    {
                        connected = false;
                        break;
                    }
                }
            }
            if (connected)
            {
                return;
            }
            Debug.LogError("YPH_GunMuzzleFlashView: 총과 비어 있지 않은 파티클 목록의 모든 항목을 연결하세요.", this);
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

        /// <summary>무기를 숨길 때 구독과 남아 있는 총구 입자를 정리합니다.</summary>
        private void OnDisable()
        {
            if (_gun != null)
            {
                _gun.OnFired -= HandleFired;
            }
            if (_effects == null)
            {
                return;
            }
            foreach (ParticleSystem effect in _effects)
            {
                if (effect != null)
                {
                    effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }
        }

        /// <summary>이미 재생 중인 섬광도 다시 터지도록 지운 뒤 재생합니다.</summary>
        private void HandleFired(Vector3 muzzle, Vector3 end, bool hitEnemy)
        {
            if (_effects == null)
            {
                return;
            }
            foreach (ParticleSystem effect in _effects)
            {
                if (effect == null)
                {
                    continue;
                }
                effect.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                effect.Play(false);
            }
        }
    }
}
