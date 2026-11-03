using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 보스방(현무) 배경 음악.
    /// - 보스방에 들어오면 서서히 커지며 반복 재생, 현무(BossCoreController)가 쓰러지면 서서히 꺼진다.
    /// - 환경 설정의 '배경음' 볼륨을 따른다 (NMJ.NmjMusic).
    /// 보스방 씬에 놓은 오브젝트에 붙인다.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BossArenaMusic : MonoBehaviour
    {
        [Tooltip("보스전 음악")]
        [SerializeField] private AudioClip clip;
        [Tooltip("배경음 볼륨 (0~1, 환경 설정 배경음 볼륨이 곱해짐)")]
        [SerializeField, Range(0f, 1f)] private float volume = 0.7f;
        [Tooltip("시작할 때 커지는 시간(초)")]
        [SerializeField, Min(0f)] private float fadeIn = 2f;
        [Tooltip("보스가 쓰러진 뒤 꺼지는 시간(초)")]
        [SerializeField, Min(0.1f)] private float fadeOut = 3f;

        private AudioSource source;
        private NMJ.NmjMusic music;
        private BossCoreController boss;
        private float level;   // 0~1 페이드

        private void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
            source.spatialBlend = 0f;
            source.clip = clip;
            music = GetComponent<NMJ.NmjMusic>();
            if (music == null) music = gameObject.AddComponent<NMJ.NmjMusic>();
            music.Volume = 0f;
        }

        private void Start()
        {
            boss = FindAnyObjectByType<BossCoreController>();
            if (clip != null) source.Play();
        }

        private void Update()
        {
            if (!source.isPlaying && level <= 0f) return;
            bool bossDown = boss != null && boss.IsDead;
            float target = bossDown ? 0f : 1f;
            float time = bossDown ? fadeOut : Mathf.Max(0.01f, fadeIn);
            level = Mathf.MoveTowards(level, target, Time.unscaledDeltaTime / time);
            music.Volume = volume * level;
            if (bossDown && level <= 0f) source.Stop();
        }
    }
}
