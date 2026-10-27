using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using LaserScorchEffects;

// Visual observer only: never starts attacks, rotates the boss, or deals damage.
[DisallowMultipleComponent]
[DefaultExecutionOrder(250)]
public sealed class HyunMuBreathVfxBridge : MonoBehaviour
{
    [Header("Existing breath components (read only)")]
    [SerializeField] private BossBreathAttack breath;
    [SerializeField] private SnakeSphereTarget head;
    [SerializeField] private Transform mouthOrigin;
    [SerializeField] private Vector3 mouthLocalOffset = new Vector3(0f, 0f, .15f);

    [Header("Existing reusable effect assets")]
    [SerializeField] private Material coreMaterial;
    [SerializeField] private Material spiralMaterial;
    [SerializeField] private Material scorchMaterial;
    [SerializeField] private ParticleSystem contactPrefab;
    [SerializeField] private ParticleSystem smokePrefab;
    [Tooltip("기존 ScorchGroundExplosion 프리팹. 폭발 대기/지속 시간은 연결된 브레스의 Explosion Delay / Attack Time을 사용합니다.")]
    [SerializeField] private GameObject explosionPrefab;

    [Header("Blue beam")]
    [SerializeField, Min(.01f)] private float coreWidth = .22f;
    [SerializeField, Min(.001f)] private float spiralWidth = .065f;
    [SerializeField, Min(0f)] private float spiralRadius = .13f;
    [SerializeField, Range(1f, 12f)] private float spiralTurns = 5f;
    [SerializeField] private float rotationsPerSecond = .65f;

    [Header("Ground trace")]
    [SerializeField] private LayerMask groundMask = 1 << 6;
    [SerializeField, Min(.1f)] private float traceWidth = 1.15f;
    [SerializeField, Min(.03f)] private float sampleSpacing = .12f;
    [Tooltip("폭발이 끝난 뒤 그을음만 남겨 두는 시간입니다. 피해 판정 시간과 별개입니다.")]
    [SerializeField, Min(0f)] private float traceHoldSeconds = .8f;
    [SerializeField, Min(.1f)] private float traceFadeSeconds = 2.4f;
    [SerializeField, Min(.1f)] private float groundProbeHeight = 3f;
    [SerializeField, Min(.1f)] private float groundProbeDepth = 6f;

    private const int Capacity = 81, HelixSamples = 97;
    private readonly Vector3[] points = new Vector3[Capacity];
    private readonly Vector3[] normals = new Vector3[Capacity];
    private readonly bool[] valid = new bool[Capacity];
    private readonly Vector3[] helixPoints = new Vector3[HelixSamples];
    private readonly ScorchTrailGeometry geometry = new ScorchTrailGeometry();
    private System.Func<Vector3, Vector3> snapGround;
    private GameObject cacheRoot;
    private LineRenderer core, spiral;
    private Mesh traceMesh;
    private MeshRenderer traceRenderer;
    private ParticleSystem contact, smoke;
    private MaterialPropertyBlock traceBlock;
    private int count;
    private bool sweeping, contactPlaying, explosionArmed, exploded;
    private ScorchBurstLifetime burstPool;
    private ParticleSystem[] burstParticles;
    private float burstNativeDuration;
    private float spacing, aftermath, phase, smokeTimer;

    private static readonly int RevealId = Shader.PropertyToID("_Reveal");
    private static readonly int HeatId = Shader.PropertyToID("_Heat");
    private static readonly int FadeId = Shader.PropertyToID("_Fade");
    private static readonly int BlastId = Shader.PropertyToID("_BlastProgress");

    public bool IsShowingBeam => core != null && core.enabled;
    public int RecordedPointCount => count;
    public int SweepCount { get; private set; }
    public int ExplosionCount { get; private set; }
    public bool IsExplosionVisible => burstPool != null && burstPool.gameObject.activeInHierarchy;
    public EntityId BurstPoolInstanceId =>
     burstPool != null ? burstPool.GetEntityId() : EntityId.None;

    public EntityId TraceMeshInstanceId =>
        traceMesh != null ? traceMesh.GetEntityId() : EntityId.None;
    public Vector3 BeamStart => core != null ? core.GetPosition(0) : Vector3.zero;
    public Vector3 BeamEnd => core != null ? core.GetPosition(1) : Vector3.zero;
    public Vector3 LastTracePoint => count > 0 ? points[count - 1] : Vector3.zero;
    public bool IsTraceVisible => traceRenderer != null && traceRenderer.enabled;
    public bool TryGetTracePoint(int index, out Vector3 point)
    {
        point = Vector3.zero;
        if (index < 0 || index >= count || !valid[index]) return false;
        point = points[index];
        return true;
    }

    private void Awake()
    {
        if (breath == null) breath = GetComponentInParent<BossBreathAttack>();
        if (head == null && breath != null) head = breath.headTarget;
        if (head == null && breath != null)
            head = breath.GetComponentInChildren<SnakeSphereTarget>(true);
        if (breath == null || head == null || coreMaterial == null ||
            spiralMaterial == null || scorchMaterial == null || explosionPrefab == null)
        {
            Debug.LogWarning("HyunMu breath VFX needs the breath, head, three effect materials and explosion prefab.", this);
            enabled = false;
            return;
        }
        CreateCache();
        ClearVisuals();
    }

    // Allocate once. Every attack reuses these objects, mesh, arrays and particle systems.
    private void CreateCache()
    {
        traceBlock = new MaterialPropertyBlock();
        snapGround = SnapGround;
        cacheRoot = new GameObject(
    "HyunMu_BreathVFX_Cache_" + GetEntityId());
        SceneManager.MoveGameObjectToScene(cacheRoot, gameObject.scene);
        core = CreateLine("BlueLaserCore", coreMaterial, coreWidth, 2);
        spiral = CreateLine("BlueLaserHelix", spiralMaterial, spiralWidth, HelixSamples);
        var ground = new GameObject("BreathGroundTrace");
        ground.transform.SetParent(cacheRoot.transform, false);
        traceMesh = new Mesh { name = "HyunMuReusableBreathTrace" };
        traceMesh.MarkDynamic();
        ground.AddComponent<MeshFilter>().sharedMesh = traceMesh;
        traceRenderer = ground.AddComponent<MeshRenderer>();
        traceRenderer.sharedMaterial = scorchMaterial;
        traceRenderer.shadowCastingMode = ShadowCastingMode.Off;
        traceRenderer.receiveShadows = false;
        if (contactPrefab != null)
        {
            contact = Instantiate(contactPrefab, cacheRoot.transform);
            contact.name = "BreathContactSparks";
            var main = contact.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }
        if (smokePrefab != null)
        {
            smoke = Instantiate(smokePrefab, cacheRoot.transform);
            smoke.name = "BreathResidualSmoke";
            var main = smoke.main;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = smoke.emission;
            emission.enabled = false;
        }
        var burstObject = Instantiate(explosionPrefab, cacheRoot.transform);
        burstObject.name = "HyunMuReusableGroundExplosion";
        burstPool = burstObject.GetComponent<ScorchBurstLifetime>();
        if (burstPool != null)
        {
            burstPool.PreparePool();
            // The breath owns the duration. The reusable emitter supplies only its burst pattern.
            burstPool.enabled = false;
            burstParticles = burstObject.GetComponentsInChildren<ParticleSystem>(true);
            burstNativeDuration = .01f;
            foreach (var system in burstParticles)
                burstNativeDuration = Mathf.Max(burstNativeDuration, system.main.startLifetime.constantMax);
        }
        else
        {
            burstObject.SetActive(false);
            Debug.LogWarning("Explosion prefab must contain ScorchBurstLifetime.", this);
        }
    }

    private LineRenderer CreateLine(string label, Material material, float width, int samples)
    {
        var go = new GameObject(label);
        go.transform.SetParent(cacheRoot.transform, false);
        var line = go.AddComponent<LineRenderer>();
        line.sharedMaterial = material;
        line.useWorldSpace = true;
        line.positionCount = samples;
        line.startWidth = line.endWidth = width;
        line.startColor = line.endColor = Color.white;
        line.numCapVertices = 4;
        line.shadowCastingMode = ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.enabled = false;
        return line;
    }

    private void LateUpdate()
    {
        if (cacheRoot == null) return;
        bool firing = breath != null && breath.isActiveAndEnabled && breath.IsRunning &&
            head != null && head.isActiveAndEnabled && head.IsBreathSweeping;
        float dt = Time.deltaTime;
        if (firing)
        {
            if (!sweeping) BeginVisualSweep();
            Vector3 target, normal;
            bool onGround = TryGround(head.BreathAimPoint, out target, out normal);
            RecordPoint(target, normal, onGround, false);
            DrawBeam(target + Vector3.up * .035f, dt);
            if (contact != null)
            {
                contact.transform.position = target + normal * .045f;
                if (onGround && !contactPlaying) { contact.Play(true); contactPlaying = true; }
                if (!onGround && contactPlaying) StopContact();
            }
            ApplyTrace(1f, 1f);
            EmitSmoke(dt, .14f);
            return;
        }
        if (sweeping)
        {
            // Capture the final end point even when the head finishes between two frames.
            if (head != null && head.IsBreathSweepComplete)
            {
                Vector3 target, normal;
                bool onGround = TryGround(head.BreathAimPoint, out target, out normal);
                RecordPoint(target, normal, onGround, true);
            }
            sweeping = false;
            explosionArmed = head != null && head.IsBreathSweepComplete;
            aftermath = 0f;
            core.enabled = spiral.enabled = false;
            StopContact();
        }
        if (count == 0) return;
        if (explosionArmed && !exploded && breath != null && breath.IsExplosionActive)
            PlayGroundExplosion();
        if (exploded && breath != null && breath.IsExplosionActive)
        {
            ApplyTrace(0f, 1f);
            return;
        }
        if (IsExplosionVisible)
        {
            burstPool.StopAndHide();
            aftermath = 0f;
        }
        if (explosionArmed && breath != null && breath.IsRunning)
        {
            // Keep the completed trace visible for the whole attack-owned waiting period.
            ApplyTrace(1f, 1f);
            EmitSmoke(dt, .2f);
            return;
        }
        explosionArmed = false; // Cancellation never detonates the trace.
        if (dt <= 0f) return;
        aftermath += dt;
        float fade = 1f - Mathf.SmoothStep(0f, 1f,
            Mathf.Clamp01((aftermath - traceHoldSeconds) / Mathf.Max(.1f, traceFadeSeconds)));
        ApplyTrace(exploded ? 0f : Mathf.Clamp01(1f - aftermath / 1.2f), fade);
        if (!exploded && aftermath < traceHoldSeconds + traceFadeSeconds * .4f) EmitSmoke(dt, .28f);
        if (fade <= 0f)
        {
            count = 0;
            traceRenderer.enabled = false;
            traceMesh.Clear(false);
        }
    }

    private void PlayGroundExplosion()
    {
        exploded = true;
        explosionArmed = false;
        aftermath = 0f;
        ExplosionCount++;
        if (smoke != null) smoke.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        if (burstPool == null) return;
        float speed = burstNativeDuration / Mathf.Max(.01f, breath.ExplosionDuration);
        foreach (var system in burstParticles)
        {
            var main = system.main;
            main.simulationSpeed = speed;
        }
        burstPool.Begin();
        // All recorded ground points erupt together, using the same three pooled systems.
        for (int i = 0; i < count; i++)
            if (valid[i]) burstPool.EmitAt(points[i], normals[i], ExplosionCount * 1013 + i * 47);
    }

    private void BeginVisualSweep()
    {
        sweeping = true;
        explosionArmed = exploded = false;
        if (burstPool != null) burstPool.StopAndHide();
        count = 0;
        spacing = Mathf.Max(.03f, sampleSpacing);
        aftermath = 0f;
        smokeTimer = 0f;
        traceMesh.Clear(false);
        traceRenderer.enabled = false;
        SweepCount++;
        // Existing smoke can finish naturally; no extra objects are spawned.
    }

    private void DrawBeam(Vector3 target, float dt)
    {
        Transform origin = mouthOrigin != null ? mouthOrigin :
            (breath.breathLineOrigin != null ? breath.breathLineOrigin :
            (head.mouthHead != null ? head.mouthHead : head.ikTarget));
        if (origin == null) { core.enabled = spiral.enabled = false; return; }
        Vector3 start = origin.TransformPoint(mouthLocalOffset);
        core.SetPosition(0, start);
        core.SetPosition(1, target);
        core.startWidth = core.endWidth = Mathf.Max(.01f, coreWidth);
        core.enabled = true;
        Vector3 delta = target - start;
        if (delta.sqrMagnitude < .0001f) { spiral.enabled = false; return; }
        phase = Mathf.Repeat(phase + dt * rotationsPerSecond * Mathf.PI * 2f, Mathf.PI * 2f);
        Vector3 direction = delta.normalized;
        Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .98f ? Vector3.right : Vector3.up;
        Vector3 side = Vector3.Cross(direction, reference).normalized;
        Vector3 up = Vector3.Cross(side, direction).normalized;
        for (int i = 0; i < HelixSamples; i++)
        {
            float t = (float)i / (HelixSamples - 1);
            float taper = Mathf.SmoothStep(0f, 1f, t / .06f) * Mathf.SmoothStep(0f, 1f, (1f - t) / .06f);
            float angle = t * spiralTurns * Mathf.PI * 2f - phase;
            helixPoints[i] = Vector3.Lerp(start, target, t) +
                (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * spiralRadius * taper;
        }
        spiral.SetPositions(helixPoints);
        spiral.startWidth = spiral.endWidth = Mathf.Max(.001f, spiralWidth);
        spiral.enabled = true;
    }

    private void RecordPoint(Vector3 point, Vector3 normal, bool onGround, bool force)
    {
        if (count > 0)
        {
            float distanceSquared = (point - points[count - 1]).sqrMagnitude;
            if (distanceSquared < .000001f || (!force && distanceSquared < spacing * spacing)) return;
        }
        if (count == Capacity)
        {
            // Keep both ends and halve density, so long sweeps remain bounded.
            for (int i = 0; i <= Capacity / 2; i++)
            {
                points[i] = points[i * 2];
                normals[i] = normals[i * 2];
                valid[i] = valid[i * 2];
            }
            count = Capacity / 2 + 1;
            spacing *= 2f;
        }
        points[count] = point;
        normals[count] = normal;
        valid[count] = onGround;
        count++;
        if (count >= 2)
        {
            geometry.UpdateMesh(traceMesh, points, normals, valid, count, traceWidth, snapGround);
            traceRenderer.enabled = true;
        }
    }

    private bool TryGround(Vector3 expected, out Vector3 point, out Vector3 normal)
    {
        RaycastHit hit;
        if (Physics.Raycast(expected + Vector3.up * groundProbeHeight, Vector3.down,
            out hit, groundProbeDepth, groundMask, QueryTriggerInteraction.Ignore) && hit.normal.y > .35f)
        {
            point = hit.point;
            normal = hit.normal;
            return true;
        }
        point = expected;
        normal = Vector3.up;
        return false;
    }

    private Vector3 SnapGround(Vector3 expected)
    {
        Vector3 point, normal;
        return TryGround(expected, out point, out normal) ? point : expected;
    }

    private void ApplyTrace(float heat, float fade)
    {
        // Only points already visited exist in the mesh, so no guessed fan is revealed.
        traceBlock.SetFloat(RevealId, 1.01f);
        traceBlock.SetFloat(HeatId, heat);
        traceBlock.SetFloat(FadeId, fade);
        traceBlock.SetFloat(BlastId, exploded ? 1f : -1f);
        traceRenderer.SetPropertyBlock(traceBlock);
    }

    private void EmitSmoke(float dt, float interval)
    {
        if (smoke == null || count == 0 || dt <= 0f) return;
        smokeTimer -= dt;
        if (smokeTimer > 0f) return;
        smokeTimer = interval;
        for (int i = 0; i < count; i += 5)
        {
            if (!valid[i]) continue;
            var emission = new ParticleSystem.EmitParams();
            emission.position = points[i] + normals[i] * .06f;
            emission.velocity = normals[i] * .3f;
            smoke.Emit(emission, 1);
        }
    }

    private void StopContact()
    {
        if (contact != null) contact.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        contactPlaying = false;
    }

    private void ClearVisuals()
    {
        sweeping = false;
        explosionArmed = exploded = false;
        if (burstPool != null) burstPool.StopAndHide();
        count = 0;
        contactPlaying = false;
        if (core != null) core.enabled = false;
        if (spiral != null) spiral.enabled = false;
        if (traceRenderer != null) traceRenderer.enabled = false;
        if (traceMesh != null) traceMesh.Clear(false);
        if (contact != null) contact.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (smoke != null) smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable() => ClearVisuals();
    private void OnDestroy()
    {
        if (cacheRoot != null) Destroy(cacheRoot);
        if (traceMesh != null) Destroy(traceMesh);
    }
}
