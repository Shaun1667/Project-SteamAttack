using UnityEngine;

namespace YPH
{
    /// <summary>개발 전용 표적입니다. 피해를 받으면 깜빡이고, 쓰러진 몸체를 숨겼다가 부활시킵니다.</summary>
    public class TargetDummy : MonoBehaviour, IDamageable
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        [SerializeField, Min(1f), Tooltip("시작·부활 시 채우는 HP입니다. 실행 중 변경은 다음 부활부터 적용하며 현재 HP를 다시 채우지 않습니다.")]
        private float _maxHealth = 5f;
        [SerializeField, Min(0f), Tooltip("맞은 뒤 하얗게 표시할 초입니다. 0이면 깜빡임 없이 원래 색을 유지합니다.")]
        private float _flashDuration = 0.1f;
        [SerializeField, Min(0f), Tooltip("쓰러져서 몸체를 숨긴 뒤 부활까지의 초입니다. 0이면 다음 Update에 부활하며 진행 중 변경도 반영합니다.")]
        private float _respawnDelay = 3f;
        [SerializeField, Tooltip("숨기고 깜빡일 몸체의 루트입니다. 플레이어는 Body만 연결해 백팩이 같이 사라지지 않게 합니다. 비우면 자기 자신을 씁니다.")]
        private Transform _visualRoot;

        private Renderer[] _renderers;
        private Collider[] _colliders;
        private Color[] _baseColors;
        private MaterialPropertyBlock _block;
        private float _health;
        private float _flashElapsed;
        private float _deadElapsed;
        private bool _flashing;

        /// <summary>활성 상태이며 HP가 남아 있어야 공격할 수 있습니다.</summary>
        public bool IsAlive => isActiveAndEnabled && _health > 0f;
        /// <summary>개발용 확인 값입니다. 외부에서 HP를 직접 바꾸지 않습니다.</summary>
        public float CurrentHealth => _health;

        private void Awake()
        {
            Transform root = _visualRoot != null ? _visualRoot : transform;
            _renderers = root.GetComponentsInChildren<Renderer>(true);
            _colliders = root.GetComponentsInChildren<Collider>(true);
            _baseColors = new Color[_renderers.Length];
            _block = new MaterialPropertyBlock();
            for (int i = 0; i < _renderers.Length; i++)
            {
                Material material = _renderers[i].sharedMaterial;
                _baseColors[i] = material != null && material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white;
            }
            _health = Mathf.Max(1f, _maxHealth);
        }

        private void Update()
        {
            if (_health <= 0f)
            {
                _deadElapsed += Time.deltaTime;
                if (_deadElapsed < _respawnDelay) return;
                _health = Mathf.Max(1f, _maxHealth);
                SetBodyVisible(true);
            }
            if (!_flashing) return;
            _flashElapsed += Time.deltaTime;
            if (_flashElapsed < _flashDuration) return;
            _flashing = false;
            SetFlash(false);
        }

        /// <summary>양수인 유한 피해만 받습니다. 죽은 동안의 추가 호출은 피해와 부활 시간에 영향을 주지 않습니다.</summary>
        public void TakeDamage(float amount, Vector3 hitFrom)
        {
            if (!IsAlive || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            _health = Mathf.Max(0f, _health - amount);
            LogHealth();
            if (_health <= 0f)
            {
                _flashing = false;
                _deadElapsed = 0f;
                SetFlash(false);
                // 루트를 끄면 Update도 멈춰 부활할 수 없으므로 몸체의 렌더러·콜라이더만 끕니다.
                SetBodyVisible(false);
                return;
            }
            _flashElapsed = 0f;
            _flashing = true;
            SetFlash(_flashDuration > 0f);
        }

        private void SetBodyVisible(bool visible)
        {
            foreach (Renderer body in _renderers) body.enabled = visible;
            foreach (Collider body in _colliders) body.enabled = visible;
        }

        private void SetFlash(bool white)
        {
            for (int i = 0; i < _renderers.Length; i++)
            {
                _renderers[i].GetPropertyBlock(_block);
                _block.SetColor(BaseColorId, white ? Color.white : _baseColors[i]);
                _renderers[i].SetPropertyBlock(_block);
            }
        }

        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogHealth() { Debug.Log($"{name}: HP {_health}", this); }
    }
}
