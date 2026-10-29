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
        private PlayerController player;
        private CharacterController controller;
        private int serial = -1;
        private PlayerController.ActionState previousAction;
        private float nextStep;
        private float nextHeartbeat;
        private bool alternate;

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (!player || !player.isActiveAndEnabled || Time.timeScale <= 0f) return;
            var action = player.CurrentAction;
            if (action != previousAction && action == PlayerController.ActionState.Death)
                ReviewedAudio.Play("AdditionalReview/31_PlayerDeath_v01_from_3s");
            if (previousAction == PlayerController.ActionState.Roll && action == PlayerController.ActionState.None)
                nextStep = Time.time + 0.15f;
            previousAction = action;
            if (serial != player.ActionSerial)
            {
                serial = player.ActionSerial;
                switch (action)
                {
                    case PlayerController.ActionState.Roll:
                        ReviewedAudio.Play("Initial/01_Movement/SFX_SnowRoll_03");
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
            if (!controller || !controller.isGrounded || action != PlayerController.ActionState.None || Time.time < nextStep) return;
            Vector3 velocity = controller.velocity; velocity.y = 0f;
            float speed = velocity.magnitude;
            if (speed < 0.15f) return;
            bool running = speed > Mathf.Max(1.5f, player.runSpeed * 0.65f);
            nextStep = Time.time + (running ? 0.34f : 0.65f);
            string step;
            if (Physics.Raycast(transform.position + Vector3.up * 0.2f, Vector3.down, out var hit, 1.5f,
                ~0, QueryTriggerInteraction.Ignore) && hit.collider &&
                (hit.collider.name.ToLowerInvariant().Contains("stone") || hit.collider.name.Contains("박석")))
                step = "Revision/03_StoneStep_v01";
            else if (running) step = "Initial/01_Movement/SFX_SnowRun_01";
            else
            {
                alternate = !alternate;
                step = alternate ? "Initial/01_Movement/SFX_Player_Walk_Snow_L_01" : "Initial/01_Movement/SFX_Player_Walk_Snow_R_01";
            }
            ReviewedAudio.Play(step, 0.8f);
            if (running) ReviewedAudio.Play("Revision/07_PackRun_v01", 0.45f);
        }
    }
}
