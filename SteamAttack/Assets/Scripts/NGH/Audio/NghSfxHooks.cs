using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using YPH;

namespace NGH
{
    /// <summary>
    /// 이미 있던 효과음 파일들을 게임 동작에 연결한다 (NghSfxLibrary 의 소리를 재생).
    /// 씬·프리팹·팀원 코드는 거의 건드리지 않고, 공개된 이벤트·값을 지켜보다가 소리를 낸다.
    ///  - 플레이어: 칼 적중(OnHitLanded), 구르기 시작(CurrentAction → Roll)
    ///  - 궁수: 조준 시작(활시위), 발사(화살 날아감), 화살이 꽂힘(EnemyBullet.AnyHit)
    ///  - 현무: 다리가 땅에 닿을 때(FourLegWalk 의 다리 목표점이 멈추는 순간), 숨결 공격 시작(BossBreathAttack.IsRunning)
    ///  - 환경: 플레이어가 있는 씬이면 바탕 소리 반복
    /// 씬이 열릴 때 저절로 생기고, 새로 생기는 적(리젠 등)은 1초마다 다시 찾아 붙는다.
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public class NghSfxHooks : MonoBehaviour
    {
        static NghSfxHooks instance;

        NghSfxLibrary lib;
        readonly List<AudioSource> pool = new List<AudioSource>();
        AudioSource ambient;
        float nextScan, nextStomp;

        readonly HashSet<PlayerController> players = new HashSet<PlayerController>();
        readonly Dictionary<PlayerController, PlayerController.ActionState> lastAction = new Dictionary<PlayerController, PlayerController.ActionState>();
        readonly HashSet<EnemyRangedAttack> archers = new HashSet<EnemyRangedAttack>();
        readonly Dictionary<BossBreathAttack, bool> breaths = new Dictionary<BossBreathAttack, bool>();

        class Leg { public Transform t; public Vector3 last; public float moved, still; }
        readonly Dictionary<FourLegWalk, Leg[]> walkers = new Dictionary<FourLegWalk, Leg[]>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Create()
        {
            if (instance != null) return;
            var go = new GameObject("NGH_SfxHooks");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<NghSfxHooks>();
        }

        void Awake()
        {
            lib = Resources.Load<NghSfxLibrary>(NghSfxLibrary.ResourcePath);
            ambient = gameObject.AddComponent<AudioSource>();
            ambient.playOnAwake = false;
            ambient.loop = true;
            ambient.spatialBlend = 0f;
        }

        void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
            EnemyBullet.AnyHit += OnArrowHit;
        }

        void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            EnemyBullet.AnyHit -= OnArrowHit;
            foreach (var p in players) if (p) p.OnHitLanded -= OnSwordHit;
        }

        void OnSceneLoaded(Scene s, LoadSceneMode m) => nextScan = 0f;

        void Update()
        {
            if (!lib) return;
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + 1f; Scan(); }

            // 구르기 시작
            foreach (var p in players)
            {
                if (!p) continue;
                var a = p.CurrentAction;
                lastAction.TryGetValue(p, out var prev);
                if (a == PlayerController.ActionState.Roll && prev != PlayerController.ActionState.Roll)
                    Play(lib.rollCloth, p.transform.position + Vector3.up, false);
                lastAction[p] = a;
            }

            // 숨결 공격 시작
            var keys = new List<BossBreathAttack>(breaths.Keys);
            foreach (var b in keys)
            {
                if (!b) { breaths.Remove(b); continue; }
                bool run = b.isActiveAndEnabled && b.IsRunning;
                if (run && !breaths[b]) Play(lib.bossBreath, b.transform.position, true);
                breaths[b] = run;
            }

            // 현무 다리: 움직이던 다리 목표점이 멈추면 한 걸음 디딘 것
            foreach (var kv in walkers)
            {
                if (!kv.Key || !kv.Key.isActiveAndEnabled) continue;
                foreach (var leg in kv.Value)
                {
                    if (!leg.t) continue;
                    Vector3 p = leg.t.position;
                    float d = (p - leg.last).magnitude;
                    leg.last = p;
                    // 다리는 FixedUpdate 로 움직여 렌더 프레임 사이엔 멈춰 보일 수 있으니, 잠깐(0.06초) 멈춰 있어야 디딘 것으로 봄
                    if (d > 0.002f) { leg.moved += d; leg.still = 0f; }
                    else if (leg.moved > 0f)
                    {
                        leg.still += Time.deltaTime;
                        if (leg.still >= 0.06f)
                        {
                            // 네 다리가 거의 동시에 디뎌도 소리가 겹쳐 뭉개지지 않게 0.3초 간격
                            if (leg.moved > 0.15f && Time.time >= nextStomp) { Play(lib.bossStomp, p, true); nextStomp = Time.time + 0.3f; }
                            leg.moved = 0f;
                        }
                    }
                }
            }
        }

        // 씬에서 새로 생긴 대상 찾아 붙이기
        void Scan()
        {
            players.RemoveWhere(p => !p);
            archers.RemoveWhere(a => !a);
            foreach (var p in FindObjectsByType<PlayerController>(FindObjectsInactive.Exclude))
                if (players.Add(p)) p.OnHitLanded += OnSwordHit;
            foreach (var a in FindObjectsByType<EnemyRangedAttack>(FindObjectsInactive.Include))
            {
                if (!archers.Add(a)) continue;
                var archer = a;
                a.AimStarted += () => { if (archer) Play(lib.bowPull, archer.transform.position + Vector3.up * 1.4f, true); };
                a.Fired += () => { if (archer) Play(lib.arrowWhoosh, archer.transform.position + Vector3.up * 1.4f, true); };
            }
            foreach (var b in FindObjectsByType<BossBreathAttack>(FindObjectsInactive.Include))
                if (!breaths.ContainsKey(b)) breaths[b] = false;
            var dead = new List<FourLegWalk>();
            foreach (var w in walkers.Keys) if (!w) dead.Add(w);
            foreach (var w in dead) walkers.Remove(w);
            foreach (var w in FindObjectsByType<FourLegWalk>(FindObjectsInactive.Include))
            {
                if (walkers.ContainsKey(w)) continue;
                var ts = new[] { w.leftFrontTarget, w.rightFrontTarget, w.leftRearTarget, w.rightRearTarget };
                var legs = new Leg[ts.Length];
                for (int i = 0; i < ts.Length; i++) legs[i] = new Leg { t = ts[i], last = ts[i] ? ts[i].position : Vector3.zero };
                walkers[w] = legs;
            }

            // 바탕 소리: 플레이어가 있는 씬에서만
            bool want = players.Count > 0 && lib.ambientLoop;
            if (want && (!ambient.isPlaying || ambient.clip != lib.ambientLoop))
            {
                ambient.clip = lib.ambientLoop;
                ambient.volume = lib.ambientVolume;
                ambient.Play();
            }
            else if (!want && ambient.isPlaying) ambient.Stop();
        }

        void OnSwordHit(Damageable d)
        {
            if (d) Play(lib.swordHit, d.transform.position + Vector3.up * 1.1f, true);
        }

        void OnArrowHit(Vector3 pos, bool hitPlayer) => Play(lib.arrowImpact, pos, true);

        // 3D 위치 소리(spatial) 또는 화면 소리. AudioSource 를 돌려 쓴다
        void Play(NghSfxLibrary.Sound s, Vector3 pos, bool spatial)
        {
            if (s == null || !lib) return;
            var clip = s.Pick();
            if (!clip) return;
            AudioSource src = null;
            foreach (var a in pool) if (!a.isPlaying) { src = a; break; }
            if (!src)
            {
                if (pool.Count >= 24) src = pool[0];
                else
                {
                    var go = new GameObject("Sfx");
                    go.transform.SetParent(transform, false);
                    src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.rolloffMode = AudioRolloffMode.Linear;
                    src.minDistance = 3f;
                    src.maxDistance = 40f;
                    src.dopplerLevel = 0f;
                    pool.Add(src);
                }
            }
            src.transform.position = pos;
            src.spatialBlend = spatial ? 1f : 0f;
            src.pitch = 1f + Random.Range(-s.pitchJitter, s.pitchJitter);
            src.clip = clip;
            src.volume = s.volume;
            src.Play();
        }
    }
}
