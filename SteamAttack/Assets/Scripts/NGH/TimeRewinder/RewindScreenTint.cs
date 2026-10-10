using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NGH
{
    /// <summary>
    /// 시간 역행 화면 연출: 역행하는 동안 화면에 옅은 황금색 필터를 씌움.
    ///  - TimeRewind.RewindStarted 에 서서히 켜지고, RewindFinished 에 서서히 꺼짐
    ///  - URP 후처리(Color Adjustments 색 필터 + 채도 + 가장자리 비네트)를 코드로 만든 전역 Volume 으로 적용 (씬에 에셋 추가 없음)
    ///  - 카메라의 Post Processing 이 꺼져 있으면 연출 동안만 켰다가 원래대로 돌려 놓음
    /// 플레이어(TimeRewind 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class RewindScreenTint : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public TimeRewind rewind;
        [Tooltip("후처리를 켤 카메라 (비우면 플레이어 카메라)")]
        public Camera targetCamera;

        [Header("색")]
        [Tooltip("화면에 곱해지는 색 (흰색 = 변화 없음). 옅은 황금색")]
        public Color tintColor = new Color(1f, 0.93f, 0.75f);
        [Tooltip("채도 변화 (-100 ~ 100). 살짝 빼면 오래된 사진처럼 바랜 느낌")]
        [Range(-100f, 100f)] public float saturation = -10f;
        [Tooltip("밝기 변화 (노출, EV)")]
        [Range(-2f, 2f)] public float exposure = 0.15f;
        [Tooltip("가장자리 비네트 색")]
        public Color vignetteColor = new Color(0.55f, 0.38f, 0.1f);
        [Tooltip("가장자리 비네트 세기 (0 = 끔)")]
        [Range(0f, 1f)] public float vignetteIntensity = 0.3f;

        [Header("시간 (초, 실제 시간 기준)")]
        [Min(0f)] public float fadeIn = 0.15f;
        [Min(0f)] public float fadeOut = 0.3f;

        Volume _volume;
        VolumeProfile _profile;
        ColorAdjustments _color;
        Vignette _vignette;
        Coroutine _fade;
        UniversalAdditionalCameraData _camData;
        bool _restorePost;

        void Awake()
        {
            if (!rewind) rewind = GetComponent<TimeRewind>();
        }

        void OnEnable()
        {
            if (!rewind) return;
            rewind.RewindStarted += OnRewindStarted;
            rewind.RewindFinished += OnRewindFinished;
        }

        void OnDisable()
        {
            if (rewind)
            {
                rewind.RewindStarted -= OnRewindStarted;
                rewind.RewindFinished -= OnRewindFinished;
            }
            if (_fade != null) { StopCoroutine(_fade); _fade = null; }
            SetWeight(0f);
            RestorePost();
        }

        void OnDestroy()
        {
            if (_volume) Destroy(_volume.gameObject);
            if (_profile) Destroy(_profile);
        }

        void OnRewindStarted()
        {
            if (!Prepare()) return;
            FadeTo(1f, fadeIn);
        }

        void OnRewindFinished() => FadeTo(0f, fadeOut);

        void FadeTo(float target, float seconds)
        {
            if (!_volume) return;
            if (_fade != null) StopCoroutine(_fade);
            _fade = StartCoroutine(FadeRoutine(target, seconds));
        }

        IEnumerator FadeRoutine(float target, float seconds)
        {
            float start = _volume.weight;
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime)
            {
                SetWeight(Mathf.Lerp(start, target, Mathf.SmoothStep(0f, 1f, t / seconds)));
                yield return null;
            }
            SetWeight(target);
            _fade = null;
            if (target <= 0f) RestorePost();
        }

        void SetWeight(float w)
        {
            if (!_volume) return;
            _volume.weight = w;
            _volume.enabled = w > 0f;
        }

        // 연출 때문에 켰던 카메라 후처리를 원래대로 (완전히 꺼진 뒤에만)
        void RestorePost()
        {
            if (_restorePost && _camData) _camData.renderPostProcessing = false;
            _restorePost = false;
        }

        /// <summary>Volume 을 만들고(처음 한 번) 현재 설정 값을 적용</summary>
        bool Prepare()
        {
            if (!targetCamera)
            {
                var tpc = FindAnyObjectByType<ThirdPersonCamera>();
                targetCamera = tpc ? tpc.GetComponent<Camera>() : Camera.main;
            }
            if (targetCamera && !_camData) _camData = targetCamera.GetComponent<UniversalAdditionalCameraData>();
            if (_camData && !_camData.renderPostProcessing) { _camData.renderPostProcessing = true; _restorePost = true; }

            if (!_volume)
            {
                _profile = ScriptableObject.CreateInstance<VolumeProfile>();
                _profile.name = "NGH_RewindTint";
                _color = _profile.Add<ColorAdjustments>(true);
                _vignette = _profile.Add<Vignette>(true);
                var go = new GameObject("NGH_RewindScreenTint");
                go.transform.SetParent(transform, false);
                _volume = go.AddComponent<Volume>();
                _volume.isGlobal = true;
                _volume.priority = 100f;   // 다른 Volume 보다 위
                _volume.sharedProfile = _profile;
                _volume.weight = 0f;
                _volume.enabled = false;
            }
            _color.colorFilter.value = tintColor;
            _color.saturation.value = saturation;
            _color.postExposure.value = exposure;
            _vignette.active = vignetteIntensity > 0f;
            _vignette.color.value = vignetteColor;
            _vignette.intensity.value = vignetteIntensity;
            _vignette.smoothness.value = 0.5f;
            return true;
        }
    }
}
