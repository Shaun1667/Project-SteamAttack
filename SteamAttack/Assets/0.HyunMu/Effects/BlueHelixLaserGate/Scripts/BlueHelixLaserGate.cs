using UnityEngine;

namespace BlueHelixEffects
{
    /// <summary>Five reusable blue laser bars. Local X spans the opening; local Y is up.</summary>
    [DisallowMultipleComponent]
    public sealed class BlueHelixLaserGate : MonoBehaviour
    {
        [SerializeField] private LineRenderer[] cores;
        [SerializeField] private LineRenderer[] spirals;
        [SerializeField] private Renderer[] emitterLenses;
        [SerializeField] private BoxCollider barrier;
        [SerializeField, Min(.1f)] private float beamLength = 4.6f;
        [SerializeField, Min(0f)] private float helixRadius = .12f;
        [SerializeField, Range(1f, 12f)] private float helixTurns = 5f;
        [SerializeField] private float rotationsPerSecond = .65f;
        [SerializeField, Min(.01f)] private float fadeSeconds = .22f;
        [SerializeField] private bool startLocked = true;
        private const int Samples = 97;
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock lineBlock, lensBlock;
        private Vector3[] points;
        private bool initialized, locked;
        private float visibility, phase;
        public bool IsLocked => locked;
        public float Visibility => visibility;
        public int BeamCount => cores == null ? 0 : cores.Length;
        public BoxCollider Barrier => barrier;

        private void Awake() => Initialize();
        private void OnEnable()
        {
            Initialize();
            if (barrier != null) barrier.enabled = locked;
            RefreshGeometry();
            ApplyVisuals();
        }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            lineBlock = new MaterialPropertyBlock();
            lensBlock = new MaterialPropertyBlock();
            points = new Vector3[Samples];
            locked = startLocked;
            visibility = locked ? 1f : 0f;
        }
        private void Update()
        {
            phase = Mathf.Repeat(phase + Time.deltaTime * rotationsPerSecond * Mathf.PI * 2f, Mathf.PI * 2f);
            visibility = Mathf.MoveTowards(visibility, locked ? 1f : 0f, Time.deltaTime / Mathf.Max(.01f, fadeSeconds));
            RefreshGeometry();
            ApplyVisuals();
        }
        public void SetLocked(bool value)
        {
            Initialize();
            locked = value;
            if (barrier != null) barrier.enabled = value && isActiveAndEnabled;
        }
        [ContextMenu("Lock gate")]
        public void Lock() => SetLocked(true);
        [ContextMenu("Unlock gate")]
        public void Unlock() => SetLocked(false);
        public void RefreshGeometry()
        {
            Initialize();
            if (cores != null)
                foreach (var line in cores)
                    if (line != null)
                    {
                        line.positionCount = 2;
                        line.SetPosition(0, new Vector3(-beamLength * .5f, 0f, 0f));
                        line.SetPosition(1, new Vector3(beamLength * .5f, 0f, 0f));
                    }
            if (spirals == null) return;
            for (int beam = 0; beam < spirals.Length; beam++)
            {
                var line = spirals[beam];
                if (line == null) continue;
                for (int i = 0; i < Samples; i++)
                {
                    float t = (float)i / (Samples - 1);
                    float envelope = Mathf.SmoothStep(0f, 1f, t / .06f) * Mathf.SmoothStep(0f, 1f, (1f - t) / .06f);
                    float angle = t * helixTurns * Mathf.PI * 2f - phase + beam * .85f;
                    float radius = helixRadius * envelope;
                    points[i] = new Vector3((t - .5f) * beamLength, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
                }
                line.positionCount = Samples;
                line.SetPositions(points);
            }
        }
        private void ApplyVisuals()
        {
            lineBlock.SetFloat(IntensityId, visibility);
            ApplyLines(cores);
            ApplyLines(spirals);
            if (emitterLenses == null) return;
            lensBlock.SetColor(BaseColorId, Color.Lerp(new Color(.025f, .045f, .07f), new Color(.12f, .7f, 1.8f), visibility));
            foreach (var lens in emitterLenses)
                if (lens != null) lens.SetPropertyBlock(lensBlock);
        }
        private void ApplyLines(LineRenderer[] lines)
        {
            if (lines == null) return;
            foreach (var line in lines)
                if (line != null)
                {
                    line.enabled = visibility > .001f && isActiveAndEnabled;
                    line.SetPropertyBlock(lineBlock);
                }
        }
        private void OnDisable()
        {
            if (barrier != null) barrier.enabled = false;
            if (cores != null) foreach (var line in cores) if (line != null) line.enabled = false;
            if (spirals != null) foreach (var line in spirals) if (line != null) line.enabled = false;
        }
    }
}
