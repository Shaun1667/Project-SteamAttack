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
        // 발도·납도 소리를 동작 시작보다 늦게 냄
        private const float DrawSheathSoundDelay = 0.5f;
        private string pendingPath;
        private float pendingAt;
        private int pendingSerial;

        // 달리기 발자국: 애니메이션에서 발이 땅에 닿는 순간마다 NghSfxLibrary.runStep 중 하나를 냄
        private class Foot
        {
            public Transform bone;
            public float prevHeight, lastPlant = -9f;
            public bool descending, lifted;
            // 최근 발 높이 기록 (시간, 높이) — 가장 낮은 높이를 최근 걸음들만으로 정해 서 있다가 걷기 시작해도 바로 맞음
            public readonly Queue<Vector2> recent = new Queue<Vector2>();
            public float RecentMin(float now, float window, float h)
            {
                recent.Enqueue(new Vector2(now, h));
                while (recent.Count > 0 && now - recent.Peek().x > window) recent.Dequeue();
                float m = h;
                foreach (var r in recent) if (r.y < m) m = r.y;
                return m;
            }
        }
        private const float StepGroundBand = 0.01f;   // 최근 가장 낮은 높이에서 이만큼(m) 안으로 내려와 멈추면 디딘 것
        private const float StepLift = 0.045f;        // 한 번 디딘 뒤 이만큼(m) 들려야 다음 디딤으로 셈 (걸을 때 발이 땅에서 구르며 출렁이는 것을 무시)
        private const float StepWindow = 1.2f;        // 가장 낮은 높이를 볼 최근 시간 (초) — 걷기 한 걸음 주기(1.03초)보다 길게
        private const float StepMinGap = 0.2f;        // 같은 발이 이보다 빨리 다시 디디지 않음
        private CharacterController controller;
        private NghSfxLibrary sfx;
        private readonly Foot[] feet = { new Foot(), new Foot() };
        private AudioSource[] stepSources;
        private int nextStepSource;

        private void Delayed(string path)
        {
            pendingPath = path;
            pendingAt = Time.time + DrawSheathSoundDelay;
            pendingSerial = player.ActionSerial;
        }

        private void Awake()
        {
            player = GetComponent<PlayerController>();
            controller = GetComponent<CharacterController>();
            rollSource = gameObject.AddComponent<AudioSource>();
            rollSource.playOnAwake = false;
            rollSource.spatialBlend = 0f;
            rollSource.clip = Resources.Load<AudioClip>("NGH/VARCO_Reviewed/Initial/01_Movement/SFX_SnowRoll_03");
            sfx = Resources.Load<NghSfxLibrary>(NghSfxLibrary.ResourcePath);
            // 걸음이 빨라 앞 소리가 끝나기 전에 다음 소리가 나므로 두 개를 번갈아 씀 (앞부분을 건너뛰어야 해서 PlayOneShot 은 못 씀)
            stepSources = new AudioSource[2];
            for (int i = 0; i < stepSources.Length; i++)
            {
                stepSources[i] = gameObject.AddComponent<AudioSource>();
                stepSources[i].playOnAwake = false;
                stepSources[i].spatialBlend = 0f;
            }
            if (player && player.animator)
            {
                feet[0].bone = player.animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                feet[1].bone = player.animator.GetBoneTransform(HumanBodyBones.RightFoot);
            }
        }

        // 애니메이션이 자세를 잡은 뒤 발 높이를 읽음
        private void LateUpdate()
        {
            if (!player || !player.isActiveAndEnabled || Time.timeScale <= 0f || sfx == null) return;
            bool moving = false, running = false;
            if (controller && controller.isGrounded && player.CurrentAction == PlayerController.ActionState.None)
            {
                Vector3 v = controller.velocity; v.y = 0f;
                moving = v.magnitude > 0.3f;
                running = v.magnitude > Mathf.Max(1.5f, player.runSpeed * 0.65f);
            }
            foreach (var f in feet)
            {
                if (!f.bone) continue;
                float h = f.bone.position.y - transform.position.y;
                float ground = f.RecentMin(Time.time, StepWindow, h);
                if (h > ground + StepLift) f.lifted = true;
                // 내려오던 발이 다시 올라가기 시작하면 바로 앞 프레임이 가장 낮은 점 — 그 높이로 판정 (프레임이 성겨도 놓치지 않게)
                bool planted = f.lifted && f.descending && h >= f.prevHeight - 0.0005f && f.prevHeight < ground + StepGroundBand;
                if (planted && Time.time - f.lastPlant >= StepMinGap)
                {
                    f.lastPlant = Time.time;
                    f.lifted = false;
                    if (moving) PlayRunStep(running);
                }
                if (Mathf.Abs(h - f.prevHeight) > 0.0005f) f.descending = h < f.prevHeight;
                f.prevHeight = h;
            }
        }

        // 걸을 때도 같은 발자국 소리를 내되 조금 작게 (sfx.walkStepVolumeScale)
        private void PlayRunStep(bool running)
        {
            // 돌 바닥(궁궐 바닥·박석) 밖의 눈밭이면 눈 밟는 소리, 아니면 기본 발자국 (FootSurfaceMap 지도로 판정)
            bool snow = FootSurfaceMap.IsSnow(transform.position) && sfx.snowRunStep != null && sfx.snowRunStep.clips != null && sfx.snowRunStep.clips.Length > 0;
            var s = snow ? sfx.snowRunStep : sfx.runStep;
            float impactAt = snow ? sfx.snowRunStepImpactAt : sfx.runStepImpactAt;
            var clip = s != null ? s.Pick() : null;
            if (!clip) return;
            var src = stepSources[nextStepSource];
            nextStepSource = (nextStepSource + 1) % stepSources.Length;
            src.clip = clip;
            src.volume = s.volume * (running ? 1f : sfx.walkStepVolumeScale);
            src.pitch = 1f + Random.Range(-s.pitchJitter, s.pitchJitter);
            src.time = Mathf.Clamp(impactAt, 0f, Mathf.Max(0f, clip.length - 0.02f));
            src.Play();
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
                        Delayed(player.weapons && player.weapons.Current && !player.weapons.Current.isMelee
                            ? "Initial/04_Steam/SFX_GunDraw_02" : "Initial/02_Sword/SFX_SwordDraw_03");
                        break;
                    case PlayerController.ActionState.Sheath:
                        Delayed(player.weapons && player.weapons.Current && !player.weapons.Current.isMelee
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
            // 발도·납도 소리: 동작이 시작되고 조금 뒤 (칼이 실제로 빠지고 꽂히는 순간), 그 사이 동작이 끊기면 내지 않음
            if (pendingPath != null && Time.time >= pendingAt)
            {
                if (player.ActionSerial == pendingSerial) ReviewedAudio.Play(pendingPath);
                pendingPath = null;
            }
            if (!player.IsDead && player.Hp > 0 && player.Hp <= 1 && Time.time >= nextHeartbeat)
            {
                nextHeartbeat = Time.time + 2f;
                ReviewedAudio.Play("Initial/06_Player/LowHealth_v01", 0.7f);
            }
            // 걷기 발자국·달릴 때 가방 소리는 뺐고, 달리기 발자국은 LateUpdate 에서 새 소리(runStep)로 냄 (2026-11-02 요청)
        }
    }
}
