using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace NGH
{
    /// <summary>
    /// 구르기(대시)·강공격 동작 동안만 모션 블러를 짧게 켬 (평소에는 꺼져 있음).
    ///  - URP 후처리 Motion Blur 를 코드로 만든 전역 Volume 으로 적용 (씬에 에셋 추가 없음)
    /// 플레이어(PlayerController 가 있는 오브젝트)에 붙입니다.
    /// </summary>
    public class ActionMotionBlur : MonoBehaviour
    {
        [Header("참조 (비우면 자동으로 찾음)")]
        public PlayerController player;

        [Header("세기")]
        [Tooltip("구르기 때 블러 세기 (0~1)")]
        [Range(0f, 1f)] public float rollIntensity = 0.55f;
        [Tooltip("강공격 때 블러 세기 (0~1)")]
        [Range(0f, 1f)] public float heavyIntensity = 0.7f;
        [Tooltip("한 프레임에 번지는 최대 길이 (화면 비율)")]
        [Range(0f, 0.2f)] public float clamp = 0.08f;

        [Header("시간 (초, 실제 시간 기준)")]
        [Min(0.01f)] public float fadeIn = 0.06f;
        [Min(0.01f)] public float fadeOut = 0.25f;

        Volume _volume;
        VolumeProfile _profile;
        MotionBlur _blur;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
        }

        void OnDestroy()
        {
            if (_volume) Destroy(_volume.gameObject);
            if (_profile) Destroy(_profile);
        }

        void Update()
        {
            if (!player) return;
            PlayerController.ActionState a = player.CurrentAction;
            float target = a == PlayerController.ActionState.Roll ? rollIntensity
                         : a == PlayerController.ActionState.HeavyAttack ? heavyIntensity : 0f;
            if (target <= 0f && (!_volume || _volume.weight <= 0f)) return;
            Prepare();
            if (target > 0f) _blur.intensity.value = target;
            float w = Mathf.MoveTowards(_volume.weight, target > 0f ? 1f : 0f,
                Time.unscaledDeltaTime / (target > 0f ? fadeIn : fadeOut));
            _volume.weight = w;
            _volume.enabled = w > 0f;
        }

        void Prepare()
        {
            if (_volume) return;
            _profile = ScriptableObject.CreateInstance<VolumeProfile>();
            _profile.name = "NGH_ActionMotionBlur";
            _blur = _profile.Add<MotionBlur>(true);
            _blur.mode.value = MotionBlurMode.CameraAndObjects;
            _blur.quality.value = MotionBlurQuality.High;
            _blur.clamp.value = clamp;
            var go = new GameObject("NGH_ActionMotionBlur");
            go.transform.SetParent(transform, false);
            _volume = go.AddComponent<Volume>();
            _volume.isGlobal = true;
            _volume.priority = 80f;
            _volume.sharedProfile = _profile;
            _volume.weight = 0f;
            _volume.enabled = false;
        }
    }
}
