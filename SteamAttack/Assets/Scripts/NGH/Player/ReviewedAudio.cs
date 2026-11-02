using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGH
{
    // The reviewed WAVs live in Resources so the player can use them without
    // requiring edits to every scene or prefab. Existing audio is not replaced.
    public sealed class ReviewedAudio : MonoBehaviour
    {
        private const string Root = "NGH/VARCO_Reviewed/";
        private static ReviewedAudio instance;
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource source;
        private AudioSource uiSource;
        private AudioSource combatMusic;
        private AudioSource wind;
        private readonly List<AudioSource> pausedMusic = new List<AudioSource>();
        private bool combatActive;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Initialize()
        {
            Ensure();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            InstallPlayers();
            instance.SetWorldAmbience(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (instance && mode == LoadSceneMode.Single) instance.StopCombatMusic();
            InstallPlayers();
            if (instance && mode == LoadSceneMode.Single) instance.SetWorldAmbience(scene);
        }

        private static void Ensure()
        {
            if (instance) return;
            var host = new GameObject("ReviewedAudio");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<ReviewedAudio>();
        }

        private void Awake()
        {
            source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            uiSource = gameObject.AddComponent<AudioSource>();
            uiSource.playOnAwake = false;
            uiSource.spatialBlend = 0f;
            uiSource.ignoreListenerPause = true;
            wind = gameObject.AddComponent<AudioSource>();
            wind.playOnAwake = false;
            wind.loop = true;
            wind.spatialBlend = 0f;
            wind.volume = 0.2f;
            var musicObject = new GameObject("ReviewedCombatMusic");
            musicObject.transform.SetParent(transform, false);
            combatMusic = musicObject.AddComponent<AudioSource>();
            combatMusic.playOnAwake = false;
            combatMusic.loop = true;
            musicObject.AddComponent<NMJ.NmjMusic>().Volume = 0.7f;
        }

        private void Update()
        {
            UpdateCombatMusic();
            // Pooled or dynamically spawned players may appear after scene loading.
            if (Time.unscaledTime < nextScan) return;
            nextScan = Time.unscaledTime + 1f;
            InstallPlayers();
            if (needsEnvironmentScan && Time.unscaledTime >= nextEnvironmentScan)
            {
                needsEnvironmentScan = false;
                InstallWorldEmitters();
            }
        }
        private float nextScan;
        private float nextEnvironmentScan;
        private bool needsEnvironmentScan;

        private void SetWorldAmbience(Scene scene)
        {
            if (wind) wind.Stop();
            needsEnvironmentScan = false;
            string n = scene.name;
            bool snowyWorld = n == "Main" || n == "NGH_Main" || n == "NMJ_Main" ||
                n == "HyeonmuWorld_Main" || n == "BossArena_Hyangwonjeong" || n == "NMJ_BossArena_Hyangwonjeong";
            if (!snowyWorld) return;
            wind.clip = Resources.Load<AudioClip>(Root + "Initial/09_World/Blizzard_v01");
            if (wind.clip) wind.Play();
            nextEnvironmentScan = Time.unscaledTime + 2f;
            needsEnvironmentScan = true;
        }

        private static void InstallWorldEmitters()
        {
            foreach (var t in FindObjectsByType<Transform>())
            {
                if (!t || t.GetComponent<ReviewedWorldEmitter>()) continue;
                string n = t.gameObject.name;
                string clip = null;
                if (n.StartsWith("VARCO_PR_Cheongsachorong") || n.StartsWith("VARCO_PR_TorchStand"))
                    clip = "Initial/09_World/Lantern_v03";
                else if (n.StartsWith("VARCO_PR_Jumak"))
                    clip = "Revision/57_Village_v02";
                if (clip != null) t.gameObject.AddComponent<ReviewedWorldEmitter>().StartLoop(clip);
            }
        }

        private void UpdateCombatMusic()
        {
            bool fighting = false;
            foreach (var enemy in YPH.EnemyAI.ActiveEnemies)
                if (enemy && enemy.isActiveAndEnabled && enemy.IsAwareOfPlayer) { fighting = true; break; }
            if (fighting && !combatActive)
            {
                var clip = Resources.Load<AudioClip>(Root + "Revision/60_CombatJoseon_v02");
                if (!clip) return;
                pausedMusic.Clear();
                foreach (var music in FindObjectsByType<NMJ.NmjMusic>())
                {
                    if (!music || music.gameObject == combatMusic.gameObject) continue;
                    var existing = music.GetComponent<AudioSource>();
                    if (existing && existing.isPlaying) { existing.Pause(); pausedMusic.Add(existing); }
                }
                combatMusic.clip = clip;
                combatMusic.Play();
                combatActive = true;
            }
            else if (!fighting && combatActive) StopCombatMusic();
        }

        private void StopCombatMusic()
        {
            if (combatMusic) combatMusic.Stop();
            foreach (var existing in pausedMusic)
                if (existing && existing.isActiveAndEnabled) existing.UnPause();
            pausedMusic.Clear();
            combatActive = false;
        }

        private static void InstallPlayers()
        {
            foreach (var player in FindObjectsByType<PlayerController>())
                if (player && !player.GetComponent<ReviewedPlayerAudio>())
                    player.gameObject.AddComponent<ReviewedPlayerAudio>();
        }

        public static void Play(string path, float volume = 1f)
            => PlayInternal(path, volume, false);

        public static void PlayUi(string path, float volume = 1f)
            => PlayInternal(path, volume, true);

        private static void PlayInternal(string path, float volume, bool ui)
        {
            if (string.IsNullOrEmpty(path)) return;
            Ensure();
            if (!instance.clips.TryGetValue(path, out var clip))
            {
                clip = Resources.Load<AudioClip>(Root + path);
                instance.clips[path] = clip;
                if (!clip) Debug.LogWarning("[ReviewedAudio] Missing clip: " + Root + path);
            }
            var output = ui ? instance.uiSource : instance.source;
            if (clip && output) output.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
    }

    public sealed class ReviewedWorldEmitter : MonoBehaviour
    {
        public void StartLoop(string relativePath)
        {
            var clip = Resources.Load<AudioClip>("NGH/VARCO_Reviewed/" + relativePath);
            if (!clip) return;
            var audio = gameObject.AddComponent<AudioSource>();
            audio.clip = clip;
            audio.loop = true;
            audio.playOnAwake = false;
            audio.spatialBlend = 1f;
            audio.minDistance = 2f;
            audio.maxDistance = 18f;
            audio.volume = 0.35f;
            audio.Play();
        }
    }

    [DisallowMultipleComponent]
    public sealed class ReviewedPlayerAudio : MonoBehaviour
    {
        private const float RollFadeOut = 0.08f;
        private PlayerController player;
        private int serial = -1;
        private PlayerController.ActionState previousAction;
        private float nextHeartbeat;
        // 구르기 소리는 구르기가 끝나면 멈춰야 해서 따로 재생 (PlayOneShot 은 도중에 멈출 수 없음)
        private AudioSource rollSource;
        private float rollVolume;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            rollSource = gameObject.AddComponent<AudioSource>();
            rollSource.playOnAwake = false;
            rollSource.spatialBlend = 0f;
            rollSource.clip = Resources.Load<AudioClip>("NGH/VARCO_Reviewed/Initial/01_Movement/SFX_SnowRoll_03");
        }

        private void Update()
        {
            if (!player || !player.isActiveAndEnabled || Time.timeScale <= 0f) return;
            var action = player.CurrentAction;
            // 구르기가 끝나면 (다른 동작으로 넘어가거나 다시 서면) 구르기 소리를 짧게 줄이며 멈춤
            if (rollSource.isPlaying && action != PlayerController.ActionState.Roll)
            {
                rollSource.volume = Mathf.MoveTowards(rollSource.volume, 0f, rollVolume * Time.unscaledDeltaTime / RollFadeOut);
                if (rollSource.volume <= 0f) rollSource.Stop();
            }
            if (action != previousAction && action == PlayerController.ActionState.Death)
                ReviewedAudio.Play("AdditionalReview/31_PlayerDeath_v01_from_3s");
            previousAction = action;
            if (serial != player.ActionSerial)
            {
                serial = player.ActionSerial;
                switch (action)
                {
                    case PlayerController.ActionState.Roll:
                        if (rollSource.clip)
                        {
                            rollVolume = 1f;
                            rollSource.volume = rollVolume;
                            rollSource.Play();
                        }
                        break;
                    case PlayerController.ActionState.Draw:
                        ReviewedAudio.Play(player.weapons && player.weapons.Current && !player.weapons.Current.isMelee
                            ? "Initial/04_Steam/SFX_GunDraw_02" : "Initial/02_Sword/SFX_SwordDraw_03");
                        break;
                    case PlayerController.ActionState.Sheath:
                        ReviewedAudio.Play(player.weapons && player.weapons.Current && !player.weapons.Current.isMelee
                            ? "Initial/04_Steam/SFX_GunHolster_01" : "Revision/09_Sheath_v01");
                        break;
                    case PlayerController.ActionState.HeavyAttack:
                        ReviewedAudio.Play("Revision/10_HeavySwing_v02");
                        break;
                    case PlayerController.ActionState.HitLarge:
                        ReviewedAudio.Play("AdditionalReview/R29_HeavyHurt_v02");
                        break;
                    case PlayerController.ActionState.HitSmall:
                        ReviewedAudio.Play("LatestReview/LightHurt_mix_v02");
                        break;
                }
            }
            if (!player.IsDead && player.Hp > 0 && player.Hp <= 1 && Time.time >= nextHeartbeat)
            {
                nextHeartbeat = Time.time + 2f;
                ReviewedAudio.Play("Initial/06_Player/LowHealth_v01", 0.7f);
            }
            // 걷기·달리기 발자국 소리(와 달릴 때 가방 흔들리는 소리)는 뺐습니다 (2026-11-02 요청)
        }
    }
}
