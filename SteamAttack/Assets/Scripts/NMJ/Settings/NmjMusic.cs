using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 배경음 표시. 배경음을 트는 AudioSource에 붙이면 환경 설정의 '배경음' 볼륨을 따른다.
    /// (나머지 소리는 모두 '효과음'으로 친다 — GameSettings 참고)
    ///
    /// 볼륨은 이 컴포넌트의 Volume으로 정한다 (AudioSource의 Volume은 여기서 덮어씀).
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    [DisallowMultipleComponent]
    public class NmjMusic : MonoBehaviour
    {
        [Tooltip("이 배경음의 원래 볼륨. 실제 볼륨 = 이 값 × 마스터 × 배경음")]
        [SerializeField, Range(0f, 1f)] private float volume = 1f;

        private AudioSource source;

        public float Volume
        {
            get => volume;
            set { volume = Mathf.Clamp01(value); Apply(); }
        }

        private void Awake()
        {
            source = GetComponent<AudioSource>();
        }

        private void OnEnable()
        {
            GameSettings.Changed += Apply;
            Apply();
        }

        private void OnDisable() => GameSettings.Changed -= Apply;

        private void Apply()
        {
            if (source == null) return;
            source.ignoreListenerVolume = true;   // 전체 볼륨(= 효과음 볼륨)은 무시하고 직접 맞춘다
            source.volume = volume * GameSettings.MusicGain;
        }
    }
}
