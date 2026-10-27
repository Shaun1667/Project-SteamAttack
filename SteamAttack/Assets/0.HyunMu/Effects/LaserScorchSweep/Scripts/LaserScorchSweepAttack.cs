using UnityEngine;
using UnityEngine.SceneManagement;
namespace LaserScorchEffects
{
    [DisallowMultipleComponent]
    public sealed class LaserScorchSweepAttack : MonoBehaviour
    {
        public enum AttackPhase { Idle, Sweeping, Armed, Detonating, Cooling }
        [Header("One-way sweep")]
        [SerializeField] private Transform muzzle, aimingHead;
        [SerializeField] private LineRenderer beam;
        [SerializeField] private ParticleSystem contactSparks, leakingSmoke;
        [SerializeField] private GameObject burstPrefab;
        [SerializeField] private Material trailMaterial;
        [SerializeField, Range(10f, 150f)] private float fanAngle = 95f;
        [SerializeField, Min(1f)] private float radius = 7f;
        [SerializeField, Range(9, 81)] private int sampleCount = 33;
        [SerializeField, Min(.2f)] private float sweepSeconds = 1.65f;
        [SerializeField, Min(.2f)] private float detonationDelay = 1.25f;
        [SerializeField, Min(.1f)] private float aftermathSeconds = 3f;
        [SerializeField] private bool playOnStart;
        [Header("Ground surface")]
        [SerializeField] private LayerMask groundMask = ~4;
        [SerializeField, Min(.1f)] private float groundProbeHeight = 10f;
        [SerializeField, Min(.1f)] private float groundProbeDepth = 30f;
        [SerializeField, Min(.2f)] private float traceWidth = 1.15f;
        [Header("Optional timing event")]
        [SerializeField] private UnityEngine.Events.UnityEvent onDetonated = new UnityEngine.Events.UnityEvent();
        public UnityEngine.Events.UnityEvent OnDetonated => onDetonated;
        private readonly RaycastHit[] groundHits = new RaycastHit[32];
        private readonly Vector3[] points = new Vector3[81], normals = new Vector3[81];
        private readonly bool[] valid = new bool[81];
        private readonly ScorchTrailGeometry geometry = new ScorchTrailGeometry();
        private System.Func<Vector3, Vector3> snapGround;
        private GameObject cacheRoot;
        private Mesh trailMesh;
        private Renderer trailRenderer;
        private ScorchBurstLifetime burstPool;
        private MaterialPropertyBlock block;
        private float elapsed, leakTimer, reveal, blastProgress = -1f;
        private int activeSamples, nextBlast;
        public AttackPhase Phase { get; private set; }
        public bool IsBusy => Phase != AttackPhase.Idle;
        public int CastCount { get; private set; }
        public int DetonatedSamples => nextBlast;
        public float SweepProgress => reveal;
        public int GroundSampleCount => activeSamples;
        public int ValidSamples { get; private set; }
        public Vector3 ContactPoint { get; private set; }

        private void Awake()
        {
            block = new MaterialPropertyBlock();
            snapGround = SnapGround;
            if (beam != null) beam.enabled = false;
            StopParticles();
            EnsureResources();
        }
        // Allocate once per emitter. Subsequent casts only rewrite the existing mesh and emit particles.
        private bool EnsureResources()
        {
            if (cacheRoot != null) return true;
            if (trailMaterial == null) return false;
            cacheRoot = new GameObject("LaserScorchVFX_Cache_" + GetEntityId());
            cacheRoot.SetActive(false);
            SceneManager.MoveGameObjectToScene(cacheRoot, gameObject.scene);
            var ground = new GameObject("ScorchedChannel");
            ground.transform.SetParent(cacheRoot.transform, false);
            trailMesh = new Mesh { name = "ReusableScorchTrace" };
            trailMesh.MarkDynamic();
            ground.AddComponent<MeshFilter>().sharedMesh = trailMesh;
            var renderer = ground.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = trailMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            trailRenderer = renderer;
            if (burstPrefab != null)
            {
                var poolObject = Instantiate(burstPrefab, cacheRoot.transform);
                poolObject.name = "ReusableScorchBurst";
                burstPool = poolObject.GetComponent<ScorchBurstLifetime>();
                if (burstPool != null) burstPool.PreparePool();
                else poolObject.SetActive(false);
            }
            return true;
        }
        private void Start() { if (playOnStart) Fire(); }
        [ContextMenu("Fire one sweep (Play Mode)")]
        public void FireOnce() { Fire(); }
        public bool Fire()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || IsBusy || muzzle == null || beam == null || !EnsureResources()) return false;
            ResetVisuals();
            activeSamples = Mathf.Clamp(sampleCount, 9, 81);
            Vector3 origin = transform.position;
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) forward = Vector3.forward;
            ValidSamples = 0;
            for (int i = 0; i < activeSamples; i++)
            {
                float angle = Mathf.Lerp(-fanAngle * .5f, fanAngle * .5f, (float)i / (activeSamples - 1));
                Vector3 expected = origin + Quaternion.AngleAxis(angle, Vector3.up) * forward * radius;
                valid[i] = TryGround(expected, out points[i], out normals[i]);
                if (valid[i]) ValidSamples++;
            }
            if (ValidSamples < 2) { activeSamples = 0; return false; }
            geometry.UpdateMesh(trailMesh, points, normals, valid, activeSamples, traceWidth, snapGround);
            nextBlast = 0; reveal = 0f; blastProgress = -1f; elapsed = 0f; leakTimer = 0f;
            CastCount++; Phase = AttackPhase.Sweeping;
            cacheRoot.SetActive(true);
            if (contactSparks != null) contactSparks.Play(false);
            if (leakingSmoke != null) leakingSmoke.Play(false);
            UpdateBeam(); ApplyVisuals(1f, 1f);
            return true;
        }
        private bool TryGround(Vector3 expected, out Vector3 point, out Vector3 normal)
        {
            point = expected; normal = Vector3.up;
            int count = Physics.RaycastNonAlloc(expected + Vector3.up * groundProbeHeight, Vector3.down,
                groundHits, groundProbeDepth, groundMask, QueryTriggerInteraction.Ignore);
            float nearest = float.PositiveInfinity; bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = groundHits[i];
                if (hit.collider == null || hit.normal.y < .35f || hit.collider.transform.IsChildOf(transform) || hit.distance >= nearest) continue;
                nearest = hit.distance; point = hit.point; normal = hit.normal; found = true;
            }
            return found;
        }
        private Vector3 SnapGround(Vector3 position)
        {
            Vector3 point, normal;
            return TryGround(position, out point, out normal) ? point : position;
        }
        public bool TryGetSample(int index, out Vector3 point)
        {
            point = Vector3.zero;
            if (index < 0 || index >= activeSamples || !valid[index]) return false;
            point = points[index]; return true;
        }
        private void Update()
        {
            if (!IsBusy || Time.deltaTime <= 0f) return;
            elapsed += Time.deltaTime;
            switch (Phase)
            {
                case AttackPhase.Sweeping:
                    reveal = Mathf.Clamp01(elapsed / Mathf.Max(.2f, sweepSeconds));
                    UpdateBeam(); LeakSteam(); ApplyVisuals(1f, 1f);
                    if (reveal >= 1f)
                    {
                        beam.enabled = false;
                        if (contactSparks != null) contactSparks.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                        elapsed = 0f; Phase = AttackPhase.Armed;
                    }
                    break;
                case AttackPhase.Armed:
                    LeakSteam(); ApplyVisuals(1f, 1f);
                    if (elapsed >= Mathf.Max(.2f, detonationDelay)) { elapsed = 0f; Phase = AttackPhase.Detonating; }
                    break;
                case AttackPhase.Detonating:
                    if (burstPool != null) burstPool.Begin();
                    // Emit continuously along the trace with three shared systems, all in one frame.
                    for (int i = 0; i < activeSamples; i++)
                        if (valid[i] && burstPool != null)
                            burstPool.EmitAt(points[i], normals[i], CastCount * 1013 + i * 47);
                    nextBlast = activeSamples; blastProgress = 1f;
                    ApplyVisuals(0f, 1f);
                    elapsed = 0f; Phase = AttackPhase.Cooling;
                    if (leakingSmoke != null) leakingSmoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
                    onDetonated?.Invoke();
                    break;
                case AttackPhase.Cooling:
                    float fade = 1f - Mathf.SmoothStep(0f, 1f, (elapsed - aftermathSeconds * .55f) / Mathf.Max(.1f, aftermathSeconds * .45f));
                    ApplyVisuals(0f, fade);
                    if (elapsed >= Mathf.Max(.1f, aftermathSeconds)) { ResetVisuals(); StopParticles(); Phase = AttackPhase.Idle; }
                    break;
            }
        }
        private void UpdateBeam()
        {
            float index = reveal * (activeSamples - 1);
            int a = Mathf.Min(Mathf.FloorToInt(index), activeSamples - 2), b = a + 1;
            beam.enabled = valid[a] && valid[b];
            ContactPoint = Vector3.Lerp(points[a], points[b], index - a) + Vector3.up * .045f;
            if (aimingHead != null) aimingHead.rotation = Quaternion.LookRotation(ContactPoint - aimingHead.position);
            beam.positionCount = 2; beam.SetPosition(0, muzzle.position); beam.SetPosition(1, ContactPoint);
            if (contactSparks != null) contactSparks.transform.position = ContactPoint;
        }
        private void LeakSteam()
        {
            if (leakingSmoke == null) return;
            leakTimer -= Time.deltaTime;
            if (leakTimer > 0f) return;
            leakTimer = .28f;
            int revealed = Mathf.FloorToInt(reveal * (activeSamples - 1));
            for (int i = 0; i <= revealed; i += 2)
                if (valid[i])
                {
                    var emit = new ParticleSystem.EmitParams();
                    int next = Mathf.Min(i + 1, revealed);
                    Vector3 origin = valid[next] ? Vector3.Lerp(points[i], points[next], Mathf.Repeat(Time.time * .7f + i * .618f, 1f)) : points[i];
                    emit.position = origin + Vector3.up * .06f;
                    emit.velocity = Vector3.up * .3f;
                    leakingSmoke.Emit(emit, 1);
                }
        }
        private void ApplyVisuals(float heat, float fade)
        {
            block.SetFloat("_Reveal", reveal + .0001f); block.SetFloat("_Heat", heat);
            block.SetFloat("_Fade", fade); block.SetFloat("_BlastProgress", blastProgress);
            if (trailRenderer != null) trailRenderer.SetPropertyBlock(block);
        }
        private void StopParticles()
        {
            if (contactSparks != null) contactSparks.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (leakingSmoke != null) leakingSmoke.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
        [ContextMenu("Cancel attack")]
        public void Cancel()
        {
            Phase = AttackPhase.Idle;
            if (beam != null) beam.enabled = false;
            StopParticles(); ResetVisuals();
        }
        private void ResetVisuals()
        {
            if (burstPool != null) burstPool.StopAndHide();
            if (cacheRoot != null) cacheRoot.SetActive(false);
        }
        private void OnDisable() => Cancel();
        private void OnDestroy()
        {
            // Release owned resources only when the emitter itself is destroyed or its scene is unloaded.
            if (cacheRoot != null) Destroy(cacheRoot);
            if (trailMesh != null) Destroy(trailMesh);
        }
    }
}
