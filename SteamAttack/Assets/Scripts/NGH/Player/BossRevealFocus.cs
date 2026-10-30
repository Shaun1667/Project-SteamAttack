using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NGH
{
    /// <summary>
    /// 보스 등장 연출: 현무가 처음 화면에 들어오는 순간 잠깐 보스 뒤 배경을 흐리게 해(초점 흐림) 보스를 돋보이게 함.
    ///  - 씬마다 한 번. 보스까지 거리가 revealDistance 안이고 카메라 화면 안에 있을 때 시작
    ///  - 보스와 플레이어는 선명하게, 보스 뒤쪽만 흐려짐 (URP Depth of Field, Gaussian)
    ///  - 코드로 만든 전역 Volume 으로 적용 (씬에 에셋 추가 없음)
    /// 플레이어에 붙입니다.
    /// </summary>
    public class BossRevealFocus : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public Camera targetCamera;

        [Header("언제")]
        [Tooltip("보스가 이 거리(m) 안에 들어오고 화면에 보이면 시작")]
        public float revealDistance = 40f;

        [Header("흐림")]
        [Tooltip("보스보다 이만큼(m) 뒤부터 흐려지기 시작")]
        public float blurStartBehind = 4f;
        [Tooltip("보스보다 이만큼(m) 뒤에서 가장 흐려짐")]
        public float blurEndBehind = 30f;
        [Range(0.5f, 1.5f)] public float maxRadius = 1.5f;

        [Header("시간 (초, 실제 시간 기준)")]
        [Min(0.01f)] public float fadeIn = 0.4f;
        [Min(0f)] public float hold = 2.2f;
        [Min(0.01f)] public float fadeOut = 0.9f;

        Transform _boss;
        bool _done;
        Volume _volume;
        VolumeProfile _profile;
        DepthOfField _dof;

        void Start()
        {
            BossCoreController core = FindAnyObjectByType<BossCoreController>();
            _boss = core ? core.transform : null;
            _done = _boss == null;
        }

        void OnDestroy()
        {
            if (_volume) Destroy(_volume.gameObject);
            if (_profile) Destroy(_profile);
        }

        void Update()
        {
            if (_done || _boss == null) return;
            if (!targetCamera)
            {
                var tpc = FindAnyObjectByType<ThirdPersonCamera>();
                targetCamera = tpc ? tpc.GetComponent<Camera>() : Camera.main;
                if (!targetCamera) return;
            }
            float dist = Vector3.Distance(targetCamera.transform.position, _boss.position);
            if (dist > revealDistance) return;
            Vector3 vp = targetCamera.WorldToViewportPoint(_boss.position + Vector3.up * 2f);
            if (vp.z <= 0f || vp.x < 0.1f || vp.x > 0.9f || vp.y < 0.05f || vp.y > 0.95f) return;
            _done = true;
            StartCoroutine(Reveal());
        }

        IEnumerator Reveal()
        {
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "NGH_BossRevealFocus";
            _dof = _profile.Add<DepthOfField>(true);
            _dof.mode.value = DepthOfFieldMode.Gaussian;
            _dof.gaussianMaxRadius.value = maxRadius;
            _dof.highQualitySampling.value = true;
            var go = new GameObject("NGH_BossRevealFocus");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 70f;
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;

            float total = fadeIn + hold + fadeOut;
            for (float t = 0f; t < total; t += Time.unscaledDeltaTime)
            {
                if (_boss && targetCamera)
                {
                    float d = Vector3.Distance(targetCamera.transform.position, _boss.position);
                    _dof.gaussianStart.value = d + blurStartBehind;
                    _dof.gaussianEnd.value = d + blurEndBehind;
                }
                _volume.weight = t < fadeIn ? Mathf.SmoothStep(0f, 1f, t / fadeIn)
                               : t < fadeIn + hold ? 1f
                               : 1f - Mathf.SmoothStep(0f, 1f, (t - fadeIn - hold) / fadeOut);
                yield return null;
            }
            Destroy(go);
            Destroy(_profile);
            _volume = null;
        }
    }
}
