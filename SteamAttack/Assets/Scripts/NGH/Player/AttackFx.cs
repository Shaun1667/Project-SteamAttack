using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

namespace NGH
{
    /// <summary>
    /// 근접 공격 타마다 베기/찌르기 이펙트를 띄웁니다. NGH_Player 에 붙입니다.
    ///  - 동작이 시작된 뒤 클립 진행도가 Spawn At 에 닿으면 이펙트를 풀(ObjectPool)에서 꺼내 재생
    ///  - 위치·방향·크기는 플레이어 기준. 컴포넌트 메뉴(⋮) "칼 궤적으로 위치·방향 계산" 으로 애니메이션에서 자동 계산
    ///  - 베기(Slash): 반원 메시가 회전하며 쓸고 지나가는 파티클. Start Angle 로 굵은 끝이 칼이 출발하는 곳에서 시작
    ///  - 찌르기(Thrust): 손 → 칼끝 방향으로 뻗어 나가는 파티클
    /// </summary>
    [DefaultExecutionOrder(1100)]   // PlayerController 다음에 Update, GroundClamp(1000) 다음에 LateUpdate
    public class AttackFx : MonoBehaviour
    {
        public enum FxKind { Slash, Thrust }

        [System.Serializable]
        public class Entry
        {
            public string label;
            public PlayerController.ActionState action;
            public FxKind kind;
            public GameObject prefab;
            [Tooltip("클립 진행도(0~1)가 이 값에 닿으면 이펙트 생성")]
            [Range(0, 1)] public float spawnAt;
            [Header("소리 (이펙트가 나오는 순간 재생)")]
            public AudioClip sound;
            [Range(0, 1)] public float soundVolume = 1f;
            [Tooltip("소리 시점 보정(초). 이펙트가 나오는 순간 기준: 음수 = 더 일찍(예: -0.1), 양수 = 더 늦게")]
            public float soundOffset;
            [Tooltip("재생 높낮이를 매번 이 범위에서 무작위로 (같은 소리가 반복돼도 덜 단조롭게). 둘 다 1이면 고정")]
            public Vector2 soundPitch = new Vector2(0.96f, 1.04f);
            [Tooltip("칼 궤적 계산에 쓸 클립 구간(0~1). 휘두르기 시작 ~ 끝")]
            public Vector2 swingRange;
            [Header("자동 계산 값 (플레이어 기준, 직접 고쳐도 됨)")]
            public Vector3 localPosition;
            public Vector3 localEuler;
            [Tooltip("이펙트 크기 배율 (프리팹 기준 단위 6 = 1m 일 때 1/6)")]
            public float scale = 0.2f;
            [Tooltip("베기: 굵은 끝이 시작하는 각도(도). 찌르기는 사용 안 함")]
            public float startAngle;
            [Tooltip("베기: 회전 재생 속도 배율. 칼이 도는 빠르기에 맞춰 자동 계산")]
            public float spinSpeed = 1f;
            [Tooltip("베기: 회전 총량 배율 (1 = 자동 계산 그대로)")]
            public float spinScale = 1f;
            [Tooltip("베기: 칼이 도는 빠르기를 따라가는 회전 속도 곡선 (rad/s, 시간 = 이펙트 수명 기준 0~1). 자동 계산")]
            public AnimationCurve spinCurve;
            [Tooltip("자동 계산 크기에 곱할 배율 (강공격 등 더 크게 보이고 싶을 때)")]
            public float sizeBoost = 1f;
            [Tooltip("베기: 휘두르는 시간(초). 이 시간 동안 굵은 끝이 칼끝을 따라감. 자동 계산")]
            public float swingSeconds = 0.15f;
            [Tooltip("베기: 원판 대신 칼날이 실제로 지나간 면을 따라 궤적 메시를 그림 (칼과 정확히 일치)")]
            public bool useTrail = true;
            [Tooltip("궤적 메시: 휘두르기 시작점 앞쪽으로 같은 원을 따라 꼬리를 더 그릴 각도(도). 이펙트가 짧아 보일 때")]
            [Range(0f, 120f)] public float tailExtend;
            [HideInInspector] public Vector3[] pathOuter;   // 칼끝 쪽 가장자리 (플레이어 기준, [꼬리 →] 휘두르기 시작 → 끝)
            [HideInInspector] public Vector3[] pathInner;   // 칼날 안쪽 가장자리
            [HideInInspector] public int pathPrefix;        // 경로 앞의 꼬리 점 개수
        }

        public PlayerController player;
        [Tooltip("이펙트가 플레이어를 따라 움직임 (공격 중 전진해도 칼과 어긋나지 않음)")]
        public bool followPlayer = true;
        [Tooltip("이펙트를 풀에 반납할 때까지 시간(초)")]
        public float fxLifetime = 2f;
        [Tooltip("베기 크기 = 칼끝 궤적 반지름 × 이 값")]
        public float slashRadiusPadding = 1.1f;
        [Tooltip("베기 회전 총량 = 휘두른 각도 × 이 값 (1보다 크면 칼이 멈춘 뒤 조금 더 밀려 나감)")]
        public float slashOvershoot = 1.03f;
        [Tooltip("찌르기 길이 = 손~칼끝 이동 거리 + 이 값(m)")]
        public float thrustExtraLength = 0.35f;
        [Tooltip("궤적 계산에 쓸 무기 (비우면 첫 번째 근접 무기)")]
        public Weapon bakeWeapon;
        [Tooltip("베기 이펙트의 굵은 끝이 실행 중 실제 칼끝 각도를 매 프레임 따라감 (끄면 미리 계산한 회전 곡선 사용)")]
        public bool trackSword = true;
        [Tooltip("궤적 메시 너비 = 칼날 길이 × 이 값 (칼끝에서 손잡이 쪽으로)")]
        [Range(0.1f, 1f)] public float trailWidth = 0.8f;
        [Tooltip("궤적 바깥 가장자리를 칼끝보다 이만큼(칼날 길이 비율) 더 바깥으로")]
        [Range(0f, 0.5f)] public float trailTipExtend = 0.08f;
        [Tooltip("칼끝이 붙은 뼈와 그 안의 칼끝 위치. 자동 계산")]
        public Transform tipBone;
        public Vector3 tipLocal;
        public List<Entry> entries = new List<Entry>();

        // 이펙트 프리팹 기준 단위: 베기 바깥 반지름 / 찌르기 길이 = 6 (Start Size 6 × 메시 1)
        const float FxUnit = 6f;
        // 베기 메시(SlashMesh)의 굵은 끝이 놓인 기본 각도(도, 메시 XY 평면 기준)
        const float SlashHeadAngle = 191.5f;

        int _serial = -1;
        readonly List<Entry> _pending = new List<Entry>();
        static readonly List<ParticleSystem> _psBuffer = new List<ParticleSystem>();

        AudioSource _audio;

        void Awake()
        {
            if (!player) player = GetComponent<PlayerController>();
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 0f;   // 2D: 플레이어 자신의 소리라 카메라 거리와 상관없이 같은 크기
        }

        void PlaySound(Entry e)
        {
            if (!e.sound || !_audio) return;
            // 연타하면 앞 소리 위에 겹쳐 재생 (PlayOneShot). 높낮이를 조금씩 흔듦
            _audio.pitch = Random.Range(Mathf.Min(e.soundPitch.x, e.soundPitch.y), Mathf.Max(e.soundPitch.x, e.soundPitch.y));
            _audio.PlayOneShot(e.sound, e.soundVolume);
        }

        void Update()
        {
            if (!player) return;
            if (player.ActionSerial != _serial)
            {
                _serial = player.ActionSerial;
                _pending.Clear();
                _pendingSound.Clear();
                foreach (var e in entries)
                {
                    if (e == null || e.action != player.CurrentAction) continue;
                    if (e.prefab) _pending.Add(e);
                    if (e.sound) _pendingSound.Add(e);
                }
            }
            float t = player.ActionClipProgress;
            for (int i = _pending.Count - 1; i >= 0; i--)
            {
                var e = _pending[i];
                if (player.CurrentAction != e.action) { _pending.RemoveAt(i); continue; }   // 동작이 끊김
                if (t >= e.spawnAt) { Spawn(e); _pending.RemoveAt(i); }
            }
            // 소리는 이펙트와 따로: 이펙트 시점 + 보정(초)을 클립 진행도로 바꿔서 비교
            float secondsPerClip = player.ActionClipSeconds;
            for (int i = _pendingSound.Count - 1; i >= 0; i--)
            {
                var e = _pendingSound[i];
                if (player.CurrentAction != e.action) { _pendingSound.RemoveAt(i); continue; }
                float at = e.spawnAt + (secondsPerClip > 0.001f ? e.soundOffset / secondsPerClip : 0f);
                if (t >= at) { PlaySound(e); _pendingSound.RemoveAt(i); }
            }
        }

        readonly List<Entry> _pendingSound = new List<Entry>();

        /// <summary>항목 하나의 이펙트를 지금 플레이어 위치 기준으로 재생</summary>
        public GameObject Spawn(Entry e)
        {
            var t = player ? player.transform : transform;
            if (!e.prefab) return null;
            // 플레이어 아래에 붙여서 공격 중 앞으로 나가는 이동(Lunge)을 따라가게 함
            var go = ObjectPool.Spawn(e.prefab, t.TransformPoint(e.localPosition), t.rotation * Quaternion.Euler(e.localEuler), followPlayer ? t : null);
            if (!go) return null;
            go.transform.localScale = Vector3.one * e.scale;

            bool trail = e.kind == FxKind.Slash && e.useTrail && e.pathOuter != null && e.pathOuter.Length >= 2
                         && e.pathInner != null && e.pathInner.Length == e.pathOuter.Length;
            bool track = e.kind == FxKind.Slash && !trail && trackSword && tipBone;
            Tracked tracked = null;
            go.GetComponentsInChildren(true, _psBuffer);
            var baseSpin = BaseSpin(e.prefab);
            for (int i = 0; i < _psBuffer.Count; i++)
            {
                var ps = _psBuffer[i];
                ps.Clear(false);
                if (e.kind != FxKind.Slash || i >= baseSpin.Length || float.IsNaN(baseSpin[i])) continue;   // 회전하는 베기 메시만
                ps.GetComponent<ParticleSystemRenderer>().enabled = !trail;   // 궤적 메시를 쓰면 원판은 숨기고 불꽃·먼지만 남김
                if (trail) continue;
                var main = ps.main;
                main.startRotation = (e.startAngle - SlashHeadAngle) * Mathf.Deg2Rad;
                var rol = ps.rotationOverLifetime;
                if (track)
                {
                    // 회전은 LateUpdate 에서 실제 칼끝 각도로 직접 맞춤
                    rol.enabled = false;
                    main.simulationSpeed = 1f;
                    if (tracked == null) { tracked = new Tracked { go = go, entry = e, angle = e.startAngle, until = Time.time + e.swingSeconds + 0.03f }; _tracked.Add(tracked); }
                    tracked.systems.Add(ps);
                }
                else
                {
                    rol.enabled = true;
                    main.simulationSpeed = Mathf.Max(0.05f, e.spinSpeed);
                    if (e.spinCurve != null && e.spinCurve.length > 1) rol.z = new ParticleSystem.MinMaxCurve(e.spinScale, e.spinCurve);
                    else rol.zMultiplier = baseSpin[i] * e.spinScale;
                }
            }
            var root = go.GetComponent<ParticleSystem>();
            if (root) root.Play(true);
            else foreach (var ps in _psBuffer) ps.Play(false);
            _psBuffer.Clear();
            ObjectPool.Despawn(go, fxLifetime);

            if (trail) SpawnTrail(e, t);
            return go;
        }

        // ================================================================ 칼날 궤적 메시

        class TrailRun
        {
            public SlashTrail trail;
            public Entry entry;
            public int serial;
        }
        readonly List<TrailRun> _trails = new List<TrailRun>();
        static readonly Dictionary<GameObject, List<ParticleSystem>> _trailSources = new Dictionary<GameObject, List<ParticleSystem>>();

        void SpawnTrail(Entry e, Transform t)
        {
            var go = ObjectPool.Spawn("NGH_FX_SlashTrail", () => new GameObject("NGH_FX_SlashTrail", typeof(SlashTrail)), followPlayer ? t : null);
            if (!go) return;
            if (!followPlayer) go.transform.SetPositionAndRotation(t.position, t.rotation);
            go.transform.localScale = Vector3.one;
            if (!_trailSources.TryGetValue(e.prefab, out var sources))
            {
                sources = new List<ParticleSystem>();
                var spin = BaseSpin(e.prefab);
                var list = e.prefab.GetComponentsInChildren<ParticleSystem>(true);
                for (int i = 0; i < list.Length; i++) if (!float.IsNaN(spin[i])) sources.Add(list[i]);
                _trailSources[e.prefab] = sources;
            }
            var trail = go.GetComponent<SlashTrail>();
            trail.Begin(e.pathOuter, e.pathInner, sources, e.pathPrefix);
            _trails.Add(new TrailRun { trail = trail, entry = e, serial = player ? player.ActionSerial : 0 });
            ObjectPool.Despawn(go, fxLifetime);
        }

        // 궤적 머리를 지금 공격 진행도(= 애니메이션 시간)에 맞춤. 동작이 끝나거나 바뀌면 끝까지
        void UpdateTrails()
        {
            for (int k = _trails.Count - 1; k >= 0; k--)
            {
                var run = _trails[k];
                if (!run.trail || !run.trail.gameObject.activeInHierarchy) { _trails.RemoveAt(k); continue; }
                var e = run.entry;
                bool same = player && player.ActionSerial == run.serial && player.CurrentAction == e.action;
                float u = same ? Mathf.InverseLerp(e.swingRange.x, e.swingRange.y, ClipTime(e)) : 1f;
                run.trail.SetHead(u);
                if (u >= 1f) _trails.RemoveAt(k);
            }
        }

        // 애니메이터가 지금 그리고 있는 그 동작의 클립 시간(0~1). 못 찾으면 컨트롤러의 진행도
        float ClipTime(Entry e)
        {
            var an = player.animator;
            string state = StateFor(e.action);
            if (an && !string.IsNullOrEmpty(state))
            {
                if (an.IsInTransition(0))
                {
                    var next = an.GetNextAnimatorStateInfo(0);
                    if (next.IsName(state)) return next.normalizedTime;
                }
                var cur = an.GetCurrentAnimatorStateInfo(0);
                if (cur.IsName(state)) return cur.normalizedTime;
            }
            return player.ActionClipProgress;
        }

        string StateFor(PlayerController.ActionState a)
        {
            switch (a)
            {
                case PlayerController.ActionState.LightAttack: return player.lightState;
                case PlayerController.ActionState.LightCombo2: return player.combo2State;
                case PlayerController.ActionState.LightCombo3: return player.combo3State;
                case PlayerController.ActionState.LightCombo4: return player.combo4State;
                case PlayerController.ActionState.LightCombo5: return player.combo5State;
                case PlayerController.ActionState.HeavyAttack: return player.heavyState;
                default: return null;
            }
        }

        // ================================================================ 칼끝 따라가기

        class Tracked
        {
            public GameObject go;
            public Entry entry;
            public float angle, until;
            public readonly List<ParticleSystem> systems = new List<ParticleSystem>();
        }
        readonly List<Tracked> _tracked = new List<Tracked>();
        static ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[8];

        // 애니메이션·GroundClamp(LateUpdate, 순서 1000)가 끝난 뒤의 칼끝 위치로 굵은 끝 각도를 맞춤
        void LateUpdate()
        {
            UpdateTrails();
            for (int k = _tracked.Count - 1; k >= 0; k--)
            {
                var tr = _tracked[k];
                if (!tr.go || !tr.go.activeInHierarchy || !tipBone) { _tracked.RemoveAt(k); continue; }
                var ft = tr.go.transform;
                Vector3 tip = Quaternion.Inverse(ft.rotation) * (tipBone.TransformPoint(tipLocal) - ft.position);
                float a = Mathf.Atan2(tip.y, tip.x) * Mathf.Rad2Deg;
                float delta = Mathf.DeltaAngle(tr.angle, a);
                if (delta > 0f && delta < 120f) tr.angle += delta;   // 칼이 도는 방향으로만 (되돌아가거나 튀는 값 무시)
                float zDeg = tr.angle - SlashHeadAngle;
                foreach (var ps in tr.systems)
                {
                    int n = ps.GetParticles(_particles);
                    for (int i = 0; i < n; i++) _particles[i].rotation3D = new Vector3(0f, 0f, zDeg);
                    if (n > 0) ps.SetParticles(_particles, n);
                    else { var m = ps.main; m.startRotation = zDeg * Mathf.Deg2Rad; }   // 아직 나오기 전이면 시작 각도로
                }
                if (Time.time >= tr.until) _tracked.RemoveAt(k);   // 휘두르기가 끝나면 그 자리에서 멈춤
            }
        }

        // 프리팹 원본의 회전(Rotation over Lifetime) 배율. 회전하지 않는 파티클은 NaN.
        // 풀에서 재사용하며 값을 바꾸므로 원본 기준으로 판단
        static readonly Dictionary<GameObject, float[]> _baseSpin = new Dictionary<GameObject, float[]>();
        static float[] BaseSpin(GameObject prefab)
        {
            if (_baseSpin.TryGetValue(prefab, out var v)) return v;
            var list = prefab.GetComponentsInChildren<ParticleSystem>(true);
            v = new float[list.Length];
            for (int i = 0; i < list.Length; i++) v[i] = list[i].rotationOverLifetime.enabled ? list[i].rotationOverLifetime.zMultiplier : float.NaN;
            _baseSpin[prefab] = v;
            return v;
        }

#if UNITY_EDITOR
        // ================================================================ 에디터: 칼 궤적에서 위치·방향 계산

        [ContextMenu("칼 궤적으로 위치·방향 계산")]
        public void BakeFromAnimation()
        {
            if (!player) player = GetComponent<PlayerController>();
            var animator = player && player.animator ? player.animator : GetComponentInChildren<Animator>();
            var ctrl = animator ? animator.runtimeAnimatorController as AnimatorController : null;
            if (!ctrl) { Debug.LogError("[NGH] AttackFx: Animator Controller 를 찾지 못했습니다.", this); return; }
            if (!FindTip(out var bone, out var tip, out var gripLocal)) { Debug.LogError("[NGH] AttackFx: 무기를 찾지 못했습니다.", this); return; }
            Undo.RecordObject(this, "NGH AttackFx Bake");
            tipBone = bone;
            tipLocal = tip;

            const int N = 64;
            var tips = new Vector3[N];
            var grips = new Vector3[N];
            AnimationMode.StartAnimationMode();
            try
            {
                foreach (var e in entries)
                {
                    var clip = FindClip(ctrl, StateFor(e.action));
                    if (!clip) { Debug.LogWarning($"[NGH] AttackFx: {e.label} 클립 없음"); continue; }
                    for (int i = 0; i < N; i++)
                    {
                        float u = Mathf.Lerp(e.swingRange.x, e.swingRange.y, i / (N - 1f));
                        AnimationMode.BeginSampling();
                        AnimationMode.SampleAnimationClip(animator.gameObject, clip, u * clip.length);
                        AnimationMode.EndSampling();
                        tips[i] = transform.InverseTransformPoint(bone.TransformPoint(tipLocal));
                        grips[i] = transform.InverseTransformPoint(bone.TransformPoint(gripLocal));
                    }
                    float swingSeconds = (e.swingRange.y - e.swingRange.x) * clip.length / Mathf.Max(0.01f, PlaySpeedFor(e.action));
                    e.spawnAt = e.swingRange.x;
                    e.swingSeconds = swingSeconds;
                    if (e.kind == FxKind.Slash)
                    {
                        BakeSlash(e, tips, swingSeconds);
                        // 칼날 궤적: 바깥 = 칼끝(조금 더 바깥), 안쪽 = 칼끝에서 손잡이 쪽으로 trailWidth 만큼
                        // 꼬리: 첫 점을 베기 원의 중심·회전축으로 거꾸로 돌려 앞에 덧붙임 (5° 간격)
                        int K = e.tailExtend > 0.01f ? Mathf.CeilToInt(e.tailExtend / 5f) : 0;
                        e.pathPrefix = K;
                        e.pathOuter = new Vector3[K + N];
                        e.pathInner = new Vector3[K + N];
                        for (int i = 0; i < N; i++)
                        {
                            Vector3 blade = tips[i] - grips[i];
                            e.pathOuter[K + i] = tips[i] + blade * trailTipExtend;
                            e.pathInner[K + i] = tips[i] - blade * trailWidth;
                        }
                        Vector3 axis = Quaternion.Euler(e.localEuler) * Vector3.forward, center = e.localPosition;
                        for (int k = 0; k < K; k++)
                        {
                            var q = Quaternion.AngleAxis(-e.tailExtend * (K - k) / K, axis);
                            e.pathOuter[k] = center + q * (e.pathOuter[K] - center);
                            e.pathInner[k] = center + q * (e.pathInner[K] - center);
                        }
                    }
                    else BakeThrust(e, tips, grips);
                }
            }
            finally { AnimationMode.StopAnimationMode(); }
            EditorUtility.SetDirty(this);
        }

        void BakeSlash(Entry e, Vector3[] p, float swingSeconds)
        {
            int n = p.Length;
            // 궤적 전체에 가장 잘 맞는 평면(칼이 도는 방향 = 오른손 법칙)과 그 평면 위의 원
            Vector3 centroid = Vector3.zero;
            foreach (var q in p) centroid += q;
            centroid /= n;
            // 평면은 시작·중간·끝 칼끝을 정확히 지나게 (끝나는 지점이 칼과 어긋나지 않도록)
            Vector3 normal = Vector3.Cross(p[n / 2] - p[0], p[n - 1] - p[n / 2]);
            Vector3 turn = Vector3.zero;   // 도는 방향 확인용
            for (int i = 0; i < n - 1; i++) turn += Vector3.Cross(p[i] - centroid, p[i + 1] - centroid);
            if (normal.sqrMagnitude < 1e-8f) normal = turn;
            if (Vector3.Dot(normal, turn) < 0f) normal = -normal;
            normal.Normalize();
            centroid = (p[0] + p[n / 2] + p[n - 1]) / 3f;
            Vector3 ax = Vector3.ProjectOnPlane(p[0] - centroid, normal).normalized, ay = Vector3.Cross(normal, ax);
            var flat = new Vector2[n];
            for (int i = 0; i < n; i++) flat[i] = new Vector2(Vector3.Dot(p[i] - centroid, ax), Vector3.Dot(p[i] - centroid, ay));
            Vector3 c;
            float r;
            if (FitCircle(flat, out var c2, out r)) c = centroid + ax * c2.x + ay * c2.y;
            else
            {
                c = Circumcenter(p[0], p[n / 2], p[n - 1]);
                r = 0f;
                foreach (var q in p) r += (q - c).magnitude;
                r /= n;
            }
            r *= e.sizeBoost;
            // 이펙트 +Y(타격 불꽃 쪽)를 최대한 플레이어 정면으로
            Vector3 up = Vector3.ProjectOnPlane(Vector3.forward, normal);
            if (up.sqrMagnitude < 0.04f) up = Vector3.ProjectOnPlane(Vector3.up, normal);
            var rot = Quaternion.LookRotation(normal, up.normalized);
            Vector3 s = Quaternion.Inverse(rot) * (p[0] - c);
            e.localPosition = c;
            e.localEuler = rot.eulerAngles;
            e.scale = r * slashRadiusPadding / FxUnit;
            e.startAngle = Mathf.Atan2(s.y, s.x) * Mathf.Rad2Deg;

            // 칼이 휘두르는 동안 돈 각도만큼 이펙트 굵은 끝도 돌도록 재생 속도를 맞춤
            float swept = 0f;
            for (int i = 0; i < n - 1; i++) swept += Vector3.SignedAngle(p[i] - c, p[i + 1] - c, normal);
            swept = Mathf.Abs(swept);
            // 굵은 끝이 칼끝 각도를 그대로 따라가도록 회전 속도 곡선을 만듦.
            // 칼이 멈춘 뒤에는 slashOvershoot 만큼만 조금 더 밀려 나가고 멈춤 (원을 따라 다시 올라가지 않게)
            var spin = SpinSystem(e.prefab);
            float life = spin ? Mathf.Max(0.01f, spin.main.startLifetime.constant) : 1f;
            e.spinScale = 1f;
            e.spinSpeed = 1f;
            e.spinCurve = null;
            if (swingSeconds > 0f)
            {
                var ang = new float[n];   // 칼끝 누적 각도(도)
                for (int i = 1; i < n; i++) ang[i] = ang[i - 1] + Mathf.Abs(Vector3.SignedAngle(p[i - 1] - c, p[i] - c, normal));
                float dt = swingSeconds / (n - 1);
                var keys = new List<Keyframe>();
                for (int i = 0; i < n; i++)
                {
                    int a = Mathf.Max(0, i - 1), b = Mathf.Min(n - 1, i + 1);
                    float w = (ang[b] - ang[a]) / ((b - a) * dt) * Mathf.Deg2Rad;   // rad/s
                    keys.Add(new Keyframe(i * dt / life, w));
                }
                // 끝난 뒤: 남은 밀림 각도를 같은 속도로 감속하며 소화 → 0
                float endW = keys[n - 1].value;
                float extra = swept * Mathf.Max(0f, slashOvershoot - 1f) * Mathf.Deg2Rad;
                float stop = endW > 0.01f ? Mathf.Clamp(2f * extra / endW, 0.005f, 0.3f) : 0.005f;
                keys.Add(new Keyframe(Mathf.Min(0.99f, (swingSeconds + stop) / life), 0f));
                keys.Add(new Keyframe(1f, 0f));
                e.spinCurve = new AnimationCurve(keys.ToArray());
                for (int i = 0; i < e.spinCurve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(e.spinCurve, i, AnimationUtility.TangentMode.Linear);
                    AnimationUtility.SetKeyRightTangentMode(e.spinCurve, i, AnimationUtility.TangentMode.Linear);
                }
            }
            Debug.Log($"[NGH] AttackFx {e.label}: 중심 {c:F2} 반지름 {r:F2}m 회전축 {normal:F2} 시작각 {e.startAngle:F0} 휘두른 각도 {swept:F0}° / {swingSeconds:F2}초");
        }

        static ParticleSystem SpinSystem(GameObject prefab)
        {
            if (prefab)
                foreach (var ps in prefab.GetComponentsInChildren<ParticleSystem>(true))
                    if (ps.rotationOverLifetime.enabled) return ps;
            return null;
        }

        float PlaySpeedFor(PlayerController.ActionState a)
        {
            switch (a)
            {
                case PlayerController.ActionState.LightAttack: return player.lightAttack.playSpeed;
                case PlayerController.ActionState.LightCombo2: return player.lightCombo2.playSpeed;
                case PlayerController.ActionState.LightCombo3: return player.lightCombo3.playSpeed;
                case PlayerController.ActionState.LightCombo4: return player.lightCombo4.playSpeed;
                case PlayerController.ActionState.LightCombo5: return player.lightCombo5.playSpeed;
                case PlayerController.ActionState.HeavyAttack: return player.heavyAttack.playSpeed;
                default: return 1f;
            }
        }

        void BakeThrust(Entry e, Vector3[] tips, Vector3[] grips)
        {
            Vector3 start = grips[0], end = tips[tips.Length - 1];
            Vector3 dir = end - start;
            float len = dir.magnitude;
            e.localPosition = start;
            e.localEuler = Quaternion.LookRotation(dir / Mathf.Max(0.001f, len), Vector3.up).eulerAngles;
            e.scale = (len + thrustExtraLength) * e.sizeBoost / FxUnit;
            e.startAngle = 0f;
            Debug.Log($"[NGH] AttackFx {e.label}: 시작 {start:F2} 방향 {(dir / len):F2} 길이 {len + thrustExtraLength:F2}m");
        }

        // 최소제곱 원 맞춤 (Kåsa): x²+y² + Dx + Ey + F = 0
        static bool FitCircle(Vector2[] q, out Vector2 center, out float radius)
        {
            double sxx = 0, sxy = 0, syy = 0, sx = 0, sy = 0, sxz = 0, syz = 0, sz = 0, n = q.Length;
            foreach (var v in q)
            {
                double z = v.x * v.x + v.y * v.y;
                sxx += v.x * v.x; sxy += v.x * v.y; syy += v.y * v.y; sx += v.x; sy += v.y;
                sxz += v.x * z; syz += v.y * z; sz += z;
            }
            double[,] a = { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
            double[] b = { -sxz, -syz, -sz };
            double det = Det3(a);
            center = Vector2.zero; radius = 0f;
            if (System.Math.Abs(det) < 1e-12) return false;
            var sol = new double[3];
            for (int k = 0; k < 3; k++)
            {
                var m = (double[,])a.Clone();
                for (int i = 0; i < 3; i++) m[i, k] = b[i];
                sol[k] = Det3(m) / det;
            }
            center = new Vector2((float)(-sol[0] * 0.5), (float)(-sol[1] * 0.5));
            double r2 = center.sqrMagnitude - sol[2];
            if (r2 <= 0) return false;
            radius = (float)System.Math.Sqrt(r2);
            return true;
        }

        static double Det3(double[,] m) =>
            m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1])
            - m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0])
            + m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

        static Vector3 Circumcenter(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a, ac = c - a, n = Vector3.Cross(ab, ac);
            if (n.sqrMagnitude < 1e-8f) return (a + b + c) / 3f;
            return a + (Vector3.Cross(n, ab) * ac.sqrMagnitude + Vector3.Cross(ac, n) * ab.sqrMagnitude) / (2f * n.sqrMagnitude);
        }

        // 칼끝·손잡이 위치를 무기가 붙은 뼈(또는 오브젝트) 기준으로 구함
        bool FindTip(out Transform bone, out Vector3 tip, out Vector3 grip)
        {
            bone = null; tip = grip = Vector3.zero;
            var w = bakeWeapon;
            if (!w && player && player.weapons)
                foreach (var s in player.weapons.slots) if (s && s.isMelee) { w = s; break; }
            if (!w) return false;

            Vector3[] verts;
            var smr = w.GetComponentInChildren<SkinnedMeshRenderer>(true);
            if (smr && smr.sharedMesh)
            {
                var mesh = smr.sharedMesh;
                var weights = mesh.boneWeights;
                var count = new int[smr.bones.Length];
                foreach (var bw in weights) count[bw.boneIndex0]++;
                int idx = 0;
                for (int i = 1; i < count.Length; i++) if (count[i] > count[idx]) idx = i;
                bone = smr.bones[idx];
                var bp = mesh.bindposes[idx];
                verts = mesh.vertices;
                for (int i = 0; i < verts.Length; i++) verts[i] = bp.MultiplyPoint3x4(verts[i]);
            }
            else
            {
                var mf = w.GetComponentInChildren<MeshFilter>(true);
                if (!mf || !mf.sharedMesh) return false;
                bone = mf.transform;
                verts = mf.sharedMesh.vertices;
            }
            float far = -1f, near = float.MaxValue;
            foreach (var v in verts)
            {
                float d = v.sqrMagnitude;
                if (d > far) { far = d; tip = v; }
                if (d < near) { near = d; grip = v; }
            }
            return bone;
        }

        static AnimationClip FindClip(AnimatorController ctrl, string state)
        {
            if (string.IsNullOrEmpty(state)) return null;
            foreach (var layer in ctrl.layers)
                foreach (var cs in layer.stateMachine.states)
                    if (cs.state.name == state) return cs.state.motion as AnimationClip;
            return null;
        }
#endif
    }
}
