using UnityEngine;

namespace YPH
{
    /// <summary>공격 신호에 맞춰 창 표면·기운·잔상을 표시합니다. 피해 판정에는 관여하지 않습니다.</summary>
    [DisallowMultipleComponent]
    public sealed class SpearAttackVfx : MonoBehaviour
    {
        [SerializeField, Tooltip("일반·강공격의 준비와 타격 신호를 보낼 공격 부품")] private EnemyMeleeAttack _attack;
        [SerializeField, Tooltip("원래 창 위에 겹쳐 놓은 에너지 표면 렌더러")] private MeshRenderer _glow;
        [SerializeField, Tooltip("창날 주변 기운과 작은 불꽃")] private ParticleSystem[] _particles;
        [SerializeField, Tooltip("창끝의 짧은 이동 잔상")] private TrailRenderer _trail;
        [SerializeField, ColorUsage(false, true), Tooltip("일반 공격의 발광 색. HDR 밝기를 높이면 더 밝아집니다")] private Color _normalColor = new Color(.04f, .9f, 2.5f, 1f);
        [SerializeField, ColorUsage(false, true), Tooltip("강공격의 발광 색. 밝은 빨강을 사용합니다")] private Color _heavyColor = new Color(3f, .06f, .025f, 1f);
        [SerializeField, Min(.01f), Tooltip("준비 때 밝아지는 시간(초). 기본 0.08")] private float _fadeIn = .08f;
        [SerializeField, Min(.01f), Tooltip("타격 뒤 사라지는 시간(초). 기본 0.18")] private float _fadeOut = .18f;
        private static readonly int ColorId = Shader.PropertyToID("_EnergyColor");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private MaterialPropertyBlock _block;
        private EnemyAI _brain;
        private EnemyHealth _health;
        private Color _color;
        private float _intensity, _target;
        private bool _particlesPaused;

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (_attack != null) _brain = _attack.GetComponent<EnemyAI>();
            _health = GetComponent<EnemyHealth>();
        }

        private void OnEnable()
        {
            if (_block == null) _block = new MaterialPropertyBlock();
            End(true);
            if (_health != null) _health.Revived += ResetEffect;
            if (_attack == null) return;
            _attack.WeaponAttackStarted += Begin;
            _attack.WeaponAttackImpact += Impact;
            _attack.WeaponAttackEnded += End;
        }

        private void OnDisable()
        {
            if (_health != null) _health.Revived -= ResetEffect;
            if (_attack != null)
            {
                _attack.WeaponAttackStarted -= Begin;
                _attack.WeaponAttackImpact -= Impact;
                _attack.WeaponAttackEnded -= End;
            }
            End(true);
        }

        // 부활은 몸의 모든 렌더러를 다시 켜므로 복구가 끝난 뒤 잔상을 지웁니다.
        private void ResetEffect() => End(true);

        private void Begin(bool heavy)
        {
            End(true);
            _color = heavy ? _heavyColor : _normalColor;
            _target = 1f;
            // 파티클·잔상의 정점 색은 0~1이므로 색 비율을 보존하고 밝기는 재질에서 냅니다.
            Color particleColor = _color / Mathf.Max(1f, _color.maxColorComponent);
            particleColor.a = 1f;
            if (_particles != null)
                foreach (var particle in _particles)
                {
                    if (particle == null) continue;
                    var main = particle.main;
                    main.startColor = particleColor;
                    particle.Play(true);
                }
            if (_trail != null)
            {
                _trail.startColor = particleColor;
                _trail.endColor = new Color(particleColor.r, particleColor.g, particleColor.b, 0f);
                _trail.emitting = true;
            }
            Apply();
        }

        private void Impact()
        {
            _intensity = 1.65f;
            if (_particles != null)
                foreach (var particle in _particles)
                    if (particle != null) particle.Emit(8);
            Apply();
        }

        private void End(bool cancelled)
        {
            _target = 0f;
            _particlesPaused = false;
            if (_trail != null)
            {
                _trail.emitting = false;
                if (cancelled) _trail.Clear();
            }
            if (_particles != null)
                foreach (var particle in _particles)
                    if (particle != null) particle.Stop(true, cancelled ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
            if (cancelled)
            {
                _intensity = 0f;
                Apply();
            }
        }

        private void LateUpdate()
        {
            bool paused = _brain != null && _brain.IsHitStopped;
            if (paused != _particlesPaused && _target > 0 && _particles != null)
            {
                foreach (var particle in _particles)
                    if (particle != null) { if (paused) particle.Pause(true); else particle.Play(true); }
                _particlesPaused = paused;
            }
            if (paused || (_target == 0 && _intensity == 0)) return;
            float duration = _target > _intensity ? _fadeIn : _fadeOut;
            _intensity = Mathf.MoveTowards(_intensity, _target, Time.deltaTime / Mathf.Max(.01f, duration));
            Apply();
        }

        private void Apply()
        {
            if (_glow == null || _block == null) return;
            _glow.enabled = _intensity > .001f;
            _block.SetColor(ColorId, _color);
            _block.SetFloat(IntensityId, _intensity);
            _glow.SetPropertyBlock(_block);
        }
    }
}
