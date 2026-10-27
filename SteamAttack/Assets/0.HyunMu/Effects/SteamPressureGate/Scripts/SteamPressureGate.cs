using UnityEngine;

namespace SteamGateEffects
{
    [DisallowMultipleComponent]
    public sealed class SteamPressureGate : MonoBehaviour
    {
        [SerializeField] private ParticleSystem[] steamSystems;
        [SerializeField] private Renderer[] heatedNozzles;
        [SerializeField] private Renderer statusLamp;
        [SerializeField] private Transform pressureNeedle;
        [SerializeField] private Transform valveWheel;
        [SerializeField] private BoxCollider barrier;
        [SerializeField] private bool startLocked = true;
        [SerializeField, Min(.05f)] private float pressureChangeSeconds = .45f;
        private static readonly int ColorId = Shader.PropertyToID("_BaseColor");
        private MaterialPropertyBlock block;
        private bool initialized, locked;
        private float pressure;
        public bool IsLocked => locked;
        public float Pressure => pressure;
        public BoxCollider Barrier => barrier;
        public int ActiveParticles
        {
            get
            {
                int count = 0;
                if (steamSystems != null) foreach (var ps in steamSystems) if (ps != null) count += ps.particleCount;
                return count;
            }
        }
        private void Initialize()
        {
            if (initialized) return;
            initialized = true;
            locked = startLocked;
            pressure = locked ? 1f : 0f;
            block = new MaterialPropertyBlock();
        }
        private void Awake() => Initialize();
        private void OnEnable() { Initialize(); ApplyState(true); ApplyPressure(); }
        private void Start() => ApplyState(false);
        public void SetLocked(bool value)
        {
            Initialize();
            bool changed = locked != value;
            locked = value;
            if (barrier != null) barrier.enabled = locked && isActiveAndEnabled;
            if (changed) { ApplyState(false); ApplyPressure(); }
        }
        [ContextMenu("Pressurize / Lock")]
        public void Lock() => SetLocked(true);
        [ContextMenu("Release steam / Unlock")]
        public void Unlock() => SetLocked(false);
        private void ApplyState(bool clear)
        {
            bool emit = locked && isActiveAndEnabled;
            if (barrier != null) barrier.enabled = emit;
            if (steamSystems == null) return;
            foreach (var ps in steamSystems)
                if (ps != null)
                {
                    if (emit) { if (!ps.isPlaying) ps.Play(false); }
                    else ps.Stop(false, clear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
                }
        }
        private void Update()
        {
            pressure = Mathf.MoveTowards(pressure, locked ? 1f : 0f, Time.deltaTime / Mathf.Max(.05f, pressureChangeSeconds));
            ApplyPressure();
        }
        private void ApplyPressure()
        {
            block.SetColor(ColorId, Color.Lerp(new Color(.10f, .055f, .025f), new Color(1.3f, .38f, .06f), pressure));
            if (heatedNozzles != null) foreach (var nozzle in heatedNozzles) if (nozzle != null) nozzle.SetPropertyBlock(block);
            if (statusLamp != null)
            {
                block.SetColor(ColorId, locked ? new Color(1.3f, .38f, .06f) : new Color(.1f, .95f, .6f));
                statusLamp.SetPropertyBlock(block);
            }
            if (pressureNeedle != null) pressureNeedle.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(58f, -58f, pressure));
            if (valveWheel != null) valveWheel.localRotation = Quaternion.Euler(0f, 0f, pressure * 130f);
        }
        private void OnDisable()
        {
            if (barrier != null) barrier.enabled = false;
            if (steamSystems != null) foreach (var ps in steamSystems) if (ps != null) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }
}
