using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NGH
{
    /// <summary>
    /// 피격 화면 연출: 맞는 순간 화면 가장자리에 붉은 비네트가 번졌다가 서서히 사라짐.
    ///  - 강공격(피격 대)이면 더 진하게, 렌즈가 살짝 일그러지고 색이 갈라짐
    ///  - URP 후처리(Vignette·Chromatic Aberration·Lens Distortion)를 코드로 만든 전역 Volume 으로 적용 (씬에 에셋 추가 없음)
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class HitScreenVignette : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;

        [Header("색")]
        [Tooltip("피격 비네트 색 (먹이 섞인 붉은색)")]
        public Color hitColor = new Color(0.55f, 0.05f, 0.04f, 1f);
        [Tooltip("한 번 맞았을 때 비네트 세기")]
        [Range(0f, 1f)] public float intensity = 0.42f;
        [Tooltip("강공격(피격 대) 때 비네트 세기")]
        [Range(0f, 1f)] public float heavyIntensity = 0.55f;
        [Tooltip("강공격 때 색 갈라짐 세기")]
        [Range(0f, 1f)] public float heavyChromatic = 0.5f;
        [Tooltip("강공격 때 렌즈 일그러짐 (음수 = 오목)")]
        [Range(-1f, 1f)] public float heavyDistortion = -0.18f;

        [Header("시간 (초, 실제 시간 기준)")]
        [Min(0f)] public float fadeIn = 0.05f;
        [Min(0f)] public float hold = 0.08f;
        [Min(0f)] public float fadeOut = 0.6f;

        Volume _volume;
        VolumeProfile _profile;
        Vignette _vignette;
        ChromaticAberration _chromatic;
        LensDistortion _distortion;
        Coroutine _pulse;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
        }

        void OnEnable()
        {
            if (player) player.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            if (player) player.Damaged -= OnDamaged;
            if (_pulse != null) { StopCoroutine(_pulse); _pulse = null; }
            SetWeight(0f);
        }

        void OnDestroy()
        {
            if (_volume) Destroy(_volume.gameObject);
            if (_profile) Destroy(_profile);
        }

        void OnDamaged(int damage, bool heavy)
        {
            Prepare(heavy);
            if (_pulse != null) StopCoroutine(_pulse);
            _pulse = StartCoroutine(Pulse());
        }

        IEnumerator Pulse()
        {
            float start = _volume.weight;
            for (float t = 0f; t < fadeIn; t += Time.unscaledDeltaTime)
            {
                SetWeight(Mathf.Lerp(start, 1f, t / fadeIn));
                yield return null;
            }
            SetWeight(1f);
            for (float t = 0f; t < hold; t += Time.unscaledDeltaTime) yield return null;
            for (float t = 0f; t < fadeOut; t += Time.unscaledDeltaTime)
            {
                SetWeight(1f - Mathf.SmoothStep(0f, 1f, t / fadeOut));
                yield return null;
            }
            SetWeight(0f);
            _pulse = null;
        }

        void SetWeight(float w)
        {
            if (!_volume) return;
            _volume.weight = w;
            _volume.enabled = w > 0f;
        }

        void Prepare(bool heavy)
        {
            if (!_volume)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "NGH_HitVignette";
                _vignette = _profile.Add<Vignette>(true);
                _chromatic = _profile.Add<ChromaticAberration>(true);
                _distortion = _profile.Add<LensDistortion>(true);
                var go = new GameObject("NGH_HitScreenVignette");
                go.transform.SetParent(transform, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 90f;   // 기본 톤보다 위, 시간 역행(100)보다 아래
                _volume.sharedProfile = _profile;
                _volume.weight = 0f;
                _volume.enabled = false;
            }
            _vignette.color.value = hitColor;
            _vignette.intensity.value = heavy ? heavyIntensity : intensity;
            _vignette.smoothness.value = 0.6f;
            _chromatic.intensity.value = heavy ? heavyChromatic : 0f;
            _distortion.intensity.value = heavy ? heavyDistortion : 0f;
        }
    }
}
