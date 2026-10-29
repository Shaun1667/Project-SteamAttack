using UnityEngine;

// Observes bite preparation only. Movement and damage stay in BossSnakeBiteAttack.
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class HyunMuSnakeEyeTelegraph : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private BossSnakeBiteAttack biteAttack;
    [SerializeField] private Renderer eyeRenderer;
    [SerializeField] private ParticleSystem[] eyeHalos = new ParticleSystem[0];
    [SerializeField] private ParticleSystem mouthCold;
    [SerializeField] private ParticleSystem mouthGlow;
    [SerializeField] private AudioSource warningAudio;
    [SerializeField] private MeshFilter warningMeshFilter;
    [SerializeField] private MeshRenderer groundWarning;
    [SerializeField] private LayerMask groundMask = 64;

    [Header("물기 준비 신호")]
    [SerializeField] private bool chargeDuringPreparation = true;
    [SerializeField, Range(.1f, .95f)] private float warningTimeRatio = .9f;
    [SerializeField, Range(1, 4)] private int flashCount = 2;
    [ColorUsage(false, true)]
    [SerializeField] private Color flashEmission = new Color(8f, 18f, 24f, 1f);
    [SerializeField] private Color haloColor = new Color(.55f, .95f, 1f, 1f);
    [SerializeField, Min(.01f)] private float haloDiameter = .85f;
    [SerializeField, Min(.01f)] private float coldGatherRadius = .65f;
    [SerializeField, Range(0, 1)] private float soundVolume = .65f;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private static readonly int ProgressId = Shader.PropertyToID("_Progress");
    private MaterialPropertyBlock originalBlock, flashBlock, groundBlock;
    private readonly ParticleSystem.Particle[] particle = new ParticleSystem.Particle[1];
    private readonly ParticleSystem.Particle[] coldParticles = new ParticleSystem.Particle[36];
    private const int Segments = 12;
    private readonly Vector3[] groundVertices = new Vector3[(Segments + 1) * 2];
    private readonly Color[] groundColors = new Color[(Segments + 1) * 2];
    private Mesh groundMesh;
    private Color originalEmission;
    private bool ready, capturedBlock, suppressCurrentPreparation;

    public bool IsWarning { get; private set; }
    public float WarningStrength { get; private set; }
    public int WarningsPlayed { get; private set; }
    public bool IsGroundWarningVisible => groundWarning != null && groundWarning.enabled;

    private void OnEnable()
    {
        if (biteAttack == null) biteAttack = GetComponentInParent<BossSnakeBiteAttack>();
        ready = biteAttack != null;
        suppressCurrentPreparation = ready && biteAttack.IsPreparingBite;
        originalBlock = new MaterialPropertyBlock();
        flashBlock = new MaterialPropertyBlock();
        groundBlock = new MaterialPropertyBlock();
        StopVisuals();
    }

    private void LateUpdate()
    {
        if (!ready) return;
        bool preparing = biteAttack != null && biteAttack.isActiveAndEnabled && biteAttack.IsPreparingBite;
        if (!preparing)
        {
            suppressCurrentPreparation = false;
            FinishWarning();
            return;
        }
        if (suppressCurrentPreparation) return;
        if (!IsWarning) BeginWarning();
        float progress = biteAttack.BitePreparationProgress;
        float phase = Mathf.Repeat(progress / Mathf.Max(.1f, warningTimeRatio) * Mathf.Max(1, flashCount), 1f);
        float pulse = Mathf.Sin(phase * Mathf.PI);
        WarningStrength = chargeDuringPreparation
            ? Mathf.SmoothStep(.15f, 1f, progress) : (progress < warningTimeRatio ? pulse * pulse : 0f);
        if (capturedBlock && eyeRenderer != null)
        {
            flashBlock.SetColor(EmissionId, Color.LerpUnclamped(originalEmission, flashEmission, WarningStrength));
            eyeRenderer.SetPropertyBlock(flashBlock);
        }
        if (eyeHalos != null)
            foreach (var halo in eyeHalos) SetGlow(halo, haloDiameter, WarningStrength);
        SetGlow(mouthGlow, .70f, WarningStrength * .9f);
        DrawGatheringCold(progress);
        DrawGroundWarning(progress);
    }

    private void BeginWarning()
    {
        capturedBlock = eyeRenderer != null && eyeRenderer.sharedMaterial != null && eyeRenderer.sharedMaterial.HasProperty(EmissionId);
        if (capturedBlock)
        {
            eyeRenderer.GetPropertyBlock(originalBlock);
            eyeRenderer.GetPropertyBlock(flashBlock);
            originalEmission = originalBlock.HasColor(EmissionId) ? originalBlock.GetColor(EmissionId) : eyeRenderer.sharedMaterial.GetColor(EmissionId);
        }
        IsWarning = true;
        WarningsPlayed++;
        if (eyeHalos != null) foreach (var halo in eyeHalos) if (halo != null) halo.Play(false);
        if (mouthGlow != null) mouthGlow.Play(false);
        if (mouthCold != null) mouthCold.Play(false);
        if (warningAudio != null && warningAudio.clip != null)
        {
            warningAudio.volume = soundVolume;
            warningAudio.pitch = Mathf.Clamp(warningAudio.clip.length / Mathf.Max(.05f, biteAttack.readyTime), .1f, 3f);
            warningAudio.Play();
        }
    }

    private void SetGlow(ParticleSystem system, float diameter, float strength)
    {
        if (system == null) return;
        particle[0].position = Vector3.zero;
        particle[0].velocity = Vector3.zero;
        particle[0].startLifetime = particle[0].remainingLifetime = 10f;
        particle[0].startSize = diameter * Mathf.Lerp(.35f, 1f, strength);
        Color tint = haloColor; tint.a *= strength;
        particle[0].startColor = tint;
        system.SetParticles(particle, 1);
    }

    private void DrawGatheringCold(float progress)
    {
        if (mouthCold == null) return;
        for (int i = 0; i < coldParticles.Length; i++)
        {
            float t = Mathf.Repeat(progress * 2f + i * .618034f, 1f);
            float angle = i * 2.399963f + t * .65f;
            Vector3 direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), Mathf.Sin(i * 1.7f) * .45f).normalized;
            float radius = coldGatherRadius * (1f - t) * (1f - t);
            coldParticles[i].position = direction * radius + Vector3.forward * .08f;
            coldParticles[i].velocity = -direction * .8f;
            coldParticles[i].startLifetime = coldParticles[i].remainingLifetime = 10f;
            coldParticles[i].startSize = Mathf.Lerp(.13f, .035f, t);
            coldParticles[i].startColor = new Color(.65f, .91f, 1f, Mathf.Sin(t * Mathf.PI) * Mathf.Lerp(.20f, .65f, progress));
        }
        mouthCold.SetParticles(coldParticles, coldParticles.Length);
    }

    private void EnsureGroundMesh()
    {
        if (groundMesh != null || warningMeshFilter == null) return;
        groundMesh = new Mesh { name = "ReusableSnakeBiteWarning" };
        groundMesh.MarkDynamic();
        var uv = new Vector2[groundVertices.Length];
        var triangles = new int[Segments * 6];
        for (int i = 0; i <= Segments; i++)
        {
            uv[i * 2] = new Vector2(0, (float)i / Segments);
            uv[i * 2 + 1] = new Vector2(1, (float)i / Segments);
            if (i == Segments) continue;
            int v = i * 2, k = i * 6;
            triangles[k] = v; triangles[k + 1] = v + 2; triangles[k + 2] = v + 1;
            triangles[k + 3] = v + 1; triangles[k + 4] = v + 2; triangles[k + 5] = v + 3;
        }
        groundMesh.vertices = groundVertices;
        groundMesh.uv = uv;
        groundMesh.triangles = triangles;
        warningMeshFilter.sharedMesh = groundMesh;
    }

    private void DrawGroundWarning(float progress)
    {
        if (groundWarning == null || warningMeshFilter == null) return;
        EnsureGroundMesh();
        Vector3 start = biteAttack.BiteWarningStart, end = biteAttack.BiteWarningEnd;
        Vector3 flat = Vector3.ProjectOnPlane(end - start, Vector3.up);
        if (flat.sqrMagnitude < .01f) { groundWarning.enabled = false; return; }
        Vector3 side = Vector3.Cross(Vector3.up, flat.normalized);
        float radius = Mathf.Max(.3f, biteAttack.BiteWarningRadius);
        bool anyHit = false;
        for (int i = 0; i <= Segments; i++)
        {
            Vector3 p = Vector3.Lerp(start, end, (float)i / Segments);
            for (int j = 0; j < 2; j++)
            {
                int k = i * 2 + j;
                Vector3 sample = p + side * radius * (j == 0 ? -1f : 1f);
                RaycastHit hit;
                bool onGround = Physics.Raycast(sample + Vector3.up * 30f, Vector3.down, out hit, 100f, groundMask, QueryTriggerInteraction.Ignore);
                anyHit |= onGround;
                groundVertices[k] = warningMeshFilter.transform.InverseTransformPoint(onGround ? hit.point + hit.normal * .035f : sample);
                groundColors[k] = new Color(1, 1, 1, onGround ? 1 : 0);
            }
        }
        groundMesh.vertices = groundVertices;
        groundMesh.colors = groundColors;
        groundMesh.RecalculateBounds();
        groundBlock.SetFloat(ProgressId, progress);
        groundWarning.SetPropertyBlock(groundBlock);
        groundWarning.enabled = anyHit;
    }

    private void FinishWarning()
    {
        if (!IsWarning && !capturedBlock) return;
        IsWarning = false;
        WarningStrength = 0;
        if (capturedBlock && eyeRenderer != null) eyeRenderer.SetPropertyBlock(originalBlock.isEmpty ? null : originalBlock);
        capturedBlock = false;
        StopVisuals();
    }

    private void StopVisuals()
    {
        if (eyeHalos != null) foreach (var halo in eyeHalos) StopParticle(halo);
        StopParticle(mouthGlow);
        StopParticle(mouthCold);
        if (warningAudio != null) warningAudio.Stop();
        if (groundWarning != null) groundWarning.enabled = false;
    }

    private static void StopParticle(ParticleSystem system)
    {
        if (system != null) system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable()
    {
        FinishWarning();
        StopVisuals();
        ready = false;
    }

    private void OnDestroy()
    {
        if (groundMesh != null) Destroy(groundMesh);
    }
}
