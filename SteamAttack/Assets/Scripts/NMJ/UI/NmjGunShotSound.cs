using UnityEngine;
using UnityEngine.SceneManagement;

namespace NMJ
{
    /// <summary>
    /// 증기총 발사음. 씬이 열릴 때마다 YPH.SteamGun을 찾아 붙고, 발사 성공(OnFired)마다 흑색화약 소총 소리를 낸다.
    /// 씬·프리팹을 건드리지 않으려고 저절로 붙는다. 소리는 Resources/NMJ/Sounds/SFX_Rifle_SteamShot_1
    /// = Freesound CC0 머스킷 실사격 2개(Audios/NMJ/SFX/SFX_Rifle_Shot_1·2)를 겹치고 VARCO 증기 분출음(SFX_Rifle_SteamHiss_1~3)을 0.06초 뒤에 얹은 것.
    /// </summary>
    public class NmjGunShotSound : MonoBehaviour
    {
        private const string ClipFolder = "NMJ/Sounds/";
        private static AudioClip[] clips;

        [SerializeField, Range(0f, 1f)] private float volume = 0.8f;

        private YPH.SteamGun gun;
        private AudioSource source;
        private int last = -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachAll();
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m) => AttachAll();

        private static AudioClip Load(int i) => Resources.Load<AudioClip>(ClipFolder + "SFX_Rifle_SteamShot_" + i);

        private static void AttachAll()
        {
            if (clips == null)
                clips = new[] { Load(1) };   // 1번으로 확정 (2·3번은 Audios/NMJ/SFX에 보관)
            foreach (var g in FindObjectsByType<YPH.SteamGun>(FindObjectsInactive.Include))
                if (g.GetComponent<NmjGunShotSound>() == null) g.gameObject.AddComponent<NmjGunShotSound>();
        }

        private void Awake()
        {
            gun = GetComponent<YPH.SteamGun>();
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;   // 주인공 총이라 화면 소리로
        }

        private void OnEnable() { if (gun != null) gun.OnFired += HandleFired; }
        private void OnDisable() { if (gun != null) gun.OnFired -= HandleFired; }

        private void HandleFired(Vector3 muzzle, Vector3 end, bool hitEnemy)
        {
            if (clips == null) return;
            // 여러 개면 겹치지 않게 돌리고, 높낮이를 조금 흔들어 같은 소리 반복을 숨긴다
            int i = clips.Length > 1 ? (last + 1 + Random.Range(0, clips.Length - 1)) % clips.Length : 0;
            if (clips[i] == null) return;
            last = i;
            source.pitch = Random.Range(0.94f, 1.06f);
            source.PlayOneShot(clips[i], volume);
        }
    }
}
