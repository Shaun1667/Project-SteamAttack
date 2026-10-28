using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 게임 효과음 모음. Resources/NGH_SfxLibrary 에 하나 두고 NghSfxHooks 가 읽는다.
    /// 칸이 비어 있으면 그 소리는 나지 않는다. 여러 개 넣으면 매번 하나를 골라 같은 소리 반복을 숨긴다.
    /// </summary>
    [CreateAssetMenu(menuName = "NGH/Sfx Library", fileName = "NGH_SfxLibrary")]
    public class NghSfxLibrary : ScriptableObject
    {
        public const string ResourcePath = "NGH_SfxLibrary";

        [System.Serializable]
        public class Sound
        {
            public AudioClip[] clips = new AudioClip[0];
            [Range(0f, 1f)] public float volume = 0.8f;
            [Tooltip("높낮이 흔들림 (±)")]
            [Range(0f, 0.3f)] public float pitchJitter = 0.06f;

            public AudioClip Pick()
            {
                if (clips == null || clips.Length == 0) return null;
                return clips[Random.Range(0, clips.Length)];
            }
        }

        [Header("플레이어")]
        [Tooltip("칼이 적·보스에 맞을 때")]
        public Sound swordHit = new Sound();
        [Tooltip("구르기 (옷자락 스침)")]
        public Sound rollCloth = new Sound();

        [Header("궁수")]
        [Tooltip("활시위 당기기 (조준 시작)")]
        public Sound bowPull = new Sound();
        [Tooltip("화살 날아가는 소리 (발사)")]
        public Sound arrowWhoosh = new Sound();
        [Tooltip("화살이 꽂히는 소리")]
        public Sound arrowImpact = new Sound();

        [Header("보스 (현무)")]
        [Tooltip("다리 한 걸음이 땅에 닿을 때")]
        public Sound bossStomp = new Sound();
        [Tooltip("뱀 머리 숨결(브레스) 공격 시작")]
        public Sound bossBreath = new Sound();

        [Header("환경")]
        [Tooltip("플레이어가 있는 씬에서 계속 깔리는 바탕 소리 (반복)")]
        public AudioClip ambientLoop;
        [Range(0f, 1f)] public float ambientVolume = 0.35f;
    }
}
