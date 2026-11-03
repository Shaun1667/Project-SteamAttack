using System.Collections;
using UnityEngine;

namespace YPH
{
    /// <summary>한 번으로 편집한 창 소리의 가장 강한 부분을 공격 판정에 맞춥니다.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(EnemyMeleeAttack))]
    public sealed class SpearAttackAudio : MonoBehaviour
    {
        [SerializeField, Tooltip("창 소리 전용 AudioSource입니다. 비우면 재생하지 않습니다.")] private AudioSource _source;
        [SerializeField, Tooltip("한 번의 찌르기 음원입니다. 비우면 일반 공격 소리가 나지 않습니다.")] private AudioClip _stab;
        [SerializeField, Tooltip("번갈아 재생할 내려찍기 음원입니다. 비우면 강공격 소리가 나지 않습니다.")] private AudioClip[] _slams;
        [SerializeField, Min(0f), Tooltip("찌르기 음원 시작부터 가장 강한 부분까지의 시간(초). 현재 편집본은 0.105입니다.")] private float _stabLead = .105f;
        [SerializeField, Min(0f), Tooltip("내려찍기 음원 시작부터 가장 강한 부분까지의 시간(초). 현재 편집본 두 개는 0.19입니다.")] private float _slamLead = .19f;
        private EnemyMeleeAttack _attack;
        private EnemyAI _brain;
        private int _nextSlam;
        private bool _pending, _paused;

        private void Awake()
        {
            _attack = GetComponent<EnemyMeleeAttack>();
            _brain = GetComponent<EnemyAI>();
        }

        private void OnEnable()
        {
            _attack.WeaponAttackStarted += Begin;
            _attack.WeaponAttackImpact += Impact;
            _attack.WeaponAttackEnded += End;
        }

        private void OnDisable()
        {
            _attack.WeaponAttackStarted -= Begin;
            _attack.WeaponAttackImpact -= Impact;
            _attack.WeaponAttackEnded -= End;
            End(true);
        }

        private void Begin(bool heavy)
        {
            End(true);
            if (_source == null)
            {
                return;
            }
            _source.clip = heavy
                ? (_slams != null && _slams.Length > 0 ? _slams[_nextSlam++ % _slams.Length] : null)
                : _stab;
            _pending = _source.clip != null;
            if (_pending)
            {
                StartCoroutine(WaitForSwing(Mathf.Max(0f, _attack.GetWeaponWindup(heavy) - (heavy ? _slamLead : _stabLead))));
            }
        }

        private IEnumerator WaitForSwing(float delay)
        {
            // 공격 루틴과 같은 프레임부터 같은 시간 간격을 셉니다.
            float timer = 0f;
            while (timer < delay && _pending)
            {
                timer += _brain.ActiveDeltaTime;
                yield return null;
            }
            Impact();
        }

        private void Update()
        {
            if (_source == null)
            {
                return;
            }
            bool paused = Time.timeScale == 0f || _brain.IsHitStopped;
            if (paused != _paused)
            {
                if (paused) { _source.Pause(); }
                else { _source.UnPause(); }
                _paused = paused;
            }
        }

        private void Impact()
        {
            // 낮은 프레임에서 선행 시점을 건너뛰어도 판정 순간에는 한 번만 재생합니다.
            if (_pending && _source != null)
            {
                _pending = false;
                _source.Play();
            }
        }

        private void End(bool cancelled)
        {
            if (!cancelled)
            {
                return;
            }
            _pending = false;
            _paused = false;
            StopAllCoroutines();
            if (_source != null)
            {
                _source.Stop();
            }
        }
    }
}
