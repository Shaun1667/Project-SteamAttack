using UnityEngine;
namespace CyanChimneyEffects
{
    [DisallowMultipleComponent]
    public sealed class CyanChimneyJet : MonoBehaviour
    {
        [SerializeField] private bool startEnabled = true;
        [SerializeField, Min(0.01f)] private float fadeSeconds = 0.25f;
        private Renderer[] visuals;
        private MaterialPropertyBlock properties;
        private bool initialized, emitting;
        private float visibility;
        private static readonly int Intensity = Shader.PropertyToID("_Intensity");
        public bool IsEmitting => emitting;
        public float Visibility => visibility;
        private void Initialize()
        {
            if (initialized) return;
            visuals = GetComponentsInChildren<Renderer>(true);
            properties = new MaterialPropertyBlock();
            emitting = startEnabled;
            visibility = emitting ? 1f : 0f;
            initialized = true;
            Apply();
        }
        private void Awake() { Initialize(); }
        private void OnEnable() { Initialize(); Apply(); }
        private void Update()
        {
            float next = Mathf.MoveTowards(visibility, emitting ? 1f : 0f, Time.deltaTime / Mathf.Max(0.01f, fadeSeconds));
            if (Mathf.Approximately(next, visibility)) return;
            visibility = next;
            Apply();
        }
        private void Apply()
        {
            foreach (Renderer visual in visuals)
            {
                if (visual == null) continue;
                visual.GetPropertyBlock(properties);
                properties.SetFloat(Intensity, visibility);
                visual.SetPropertyBlock(properties);
                visual.enabled = visibility > 0.001f;
            }
        }
        public void SetEmitting(bool value) { Initialize(); emitting = value; }
        [ContextMenu("Start Jet")] public void StartJet() { SetEmitting(true); }
        [ContextMenu("Stop Jet")] public void StopJet() { SetEmitting(false); }
    }
}
