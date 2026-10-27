using UnityEngine;

// Observes the existing bite preparation. Does not steer, start attacks or deal damage.
[DisallowMultipleComponent]
[DefaultExecutionOrder(300)]
public sealed class HyunMuSnakeEyeTelegraph : MonoBehaviour
{
    [Header("연결")]
    [SerializeField] private BossSnakeBiteAttack biteAttack;
    [SerializeField] private Renderer eyeRenderer;
    [SerializeField] private ParticleSystem[] eyeHalos = new ParticleSystem[0];

    [Header("돌진 준비 신호")]
    [Tooltip("기존 Ready Time 중 신호를 표시할 비율입니다. 끝부분은 비워 돌진 전에 원래 밝기로 복구합니다.")]
    [SerializeField, Range(.1f, .95f)] private float warningTimeRatio = .9f;
    [SerializeField, Range(1, 4)] private int flashCount = 2;
    [ColorUsage(false, true)]
    [SerializeField] private Color flashEmission = new Color(8f, 18f, 24f, 1f);
    [SerializeField] private Color haloColor = new Color(.55f, .95f, 1f, 1f);
    [Tooltip("각 눈 빛 번짐의 최대 지름 (월드 단위).")]
    [SerializeField, Min(.01f)] private float haloDiameter = .85f;

    private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
    private MaterialPropertyBlock originalBlock;
    private MaterialPropertyBlock flashBlock;
    private readonly ParticleSystem.Particle[] particle = new ParticleSystem.Particle[1];
    private Color originalEmission;
    private bool ready, wasRunning, capturedBlock;
    private float elapsed, duration;

    public bool IsWarning { get; private set; }
    public float WarningStrength { get; private set; }
    public int WarningsPlayed { get; private set; }

    private void OnEnable()
    {
        if (biteAttack == null) biteAttack = GetComponentInParent<BossSnakeBiteAttack>();
        ready = biteAttack != null && eyeRenderer != null &&
            eyeRenderer.sharedMaterial != null && eyeRenderer.sharedMaterial.HasProperty(EmissionId);
        if (!ready)
        {
            Debug.LogWarning("Snake eye telegraph needs the bite attack and an emissive eye renderer.", this);
            return;
        }
        // Re-enabling this visual component midway through an attack must not announce a new attack.
        wasRunning = biteAttack.IsRunning;
        originalBlock = new MaterialPropertyBlock();
        flashBlock = new MaterialPropertyBlock();
        StopHalos();
    }

    private void LateUpdate()
    {
        if (!ready) return;
        bool running = biteAttack != null && biteAttack.isActiveAndEnabled && biteAttack.IsRunning;
        if (!running || eyeRenderer == null || !eyeRenderer.gameObject.activeInHierarchy)
        {
            wasRunning = running;
            FinishWarning();
            return;
        }

        if (!wasRunning)
        {
            wasRunning = true;
            BeginWarning();
        }
        if (!IsWarning) return;
        // Count the first observed frame too, so the warning finishes before Ready Time expires.
        elapsed += Time.deltaTime;
        if (elapsed >= duration)
        {
            FinishWarning();
            return;
        }

        float phase = Mathf.Repeat(elapsed / duration * Mathf.Max(1, flashCount), 1f);
        float pulse = Mathf.Sin(phase * Mathf.PI);
        WarningStrength = pulse * pulse;
        flashBlock.SetColor(EmissionId, Color.LerpUnclamped(originalEmission, flashEmission, WarningStrength));
        eyeRenderer.SetPropertyBlock(flashBlock);

        if (eyeHalos == null) return;
        for (int i = 0; i < eyeHalos.Length; i++)
        {
            ParticleSystem halo = eyeHalos[i];
            if (halo == null) continue;
            particle[0].position = Vector3.zero;
            particle[0].velocity = Vector3.zero;
            particle[0].startLifetime = 10f;
            particle[0].remainingLifetime = 10f;
            particle[0].startSize = haloDiameter * Mathf.Lerp(.35f, 1f, WarningStrength);
            Color tint = haloColor;
            tint.a *= WarningStrength;
            particle[0].startColor = tint;
            halo.SetParticles(particle, 1);
        }
    }

    private void BeginWarning()
    {
        elapsed = 0f;
        duration = Mathf.Max(0f, biteAttack.readyTime) * Mathf.Clamp(warningTimeRatio, .1f, .95f);
        if (duration <= .001f) return;
        eyeRenderer.GetPropertyBlock(originalBlock);
        eyeRenderer.GetPropertyBlock(flashBlock);
        originalEmission = originalBlock.HasColor(EmissionId)
            ? originalBlock.GetColor(EmissionId)
            : eyeRenderer.sharedMaterial.GetColor(EmissionId);
        capturedBlock = true;
        IsWarning = true;
        WarningsPlayed++;
        if (eyeHalos == null) return;
        for (int i = 0; i < eyeHalos.Length; i++)
            if (eyeHalos[i] != null) eyeHalos[i].Play(false);
    }

    private void FinishWarning()
    {
        if (!IsWarning && !capturedBlock) return;
        IsWarning = false;
        WarningStrength = 0f;
        if (capturedBlock && eyeRenderer != null)
            eyeRenderer.SetPropertyBlock(originalBlock.isEmpty ? null : originalBlock);
        capturedBlock = false;
        StopHalos();
    }

    private void StopHalos()
    {
        if (eyeHalos == null) return;
        for (int i = 0; i < eyeHalos.Length; i++)
            if (eyeHalos[i] != null)
                eyeHalos[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void OnDisable()
    {
        FinishWarning();
        StopHalos();
        ready = false;
        wasRunning = false;
    }
}
