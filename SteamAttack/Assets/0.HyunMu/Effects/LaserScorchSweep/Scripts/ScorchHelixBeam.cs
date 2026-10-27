using UnityEngine;

namespace LaserScorchEffects
{
    /// <summary>BlueHelixLaserGate-style spiral following the attack's world-space core beam.</summary>
    [DisallowMultipleComponent]
    public sealed class ScorchHelixBeam : MonoBehaviour
    {
        [SerializeField] private LineRenderer core;
        [SerializeField] private LineRenderer spiral;
        [SerializeField, Min(0f)] private float radius = .12f;
        [SerializeField, Range(1f, 12f)] private float turns = 5f;
        [SerializeField] private float rotationsPerSecond = .65f;
        private const int Samples = 97;
        private readonly Vector3[] points = new Vector3[Samples];
        private float phase;

        private void Awake() { if (spiral != null) spiral.enabled = false; }
        private void LateUpdate()
        {
            if (spiral == null) return;
            bool visible = core != null && core.enabled && core.gameObject.activeInHierarchy && core.positionCount >= 2;
            spiral.enabled = visible;
            if (!visible) return;
            phase = Mathf.Repeat(phase + Time.deltaTime * rotationsPerSecond * Mathf.PI * 2f, Mathf.PI * 2f);
            Vector3 start = core.GetPosition(0), end = core.GetPosition(core.positionCount - 1);
            if (!core.useWorldSpace) { start = core.transform.TransformPoint(start); end = core.transform.TransformPoint(end); }
            Vector3 delta = end - start;
            if (delta.sqrMagnitude < .0001f) { spiral.enabled = false; return; }
            Vector3 direction = delta.normalized;
            Vector3 reference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > .98f ? Vector3.right : Vector3.up;
            Vector3 side = Vector3.Cross(direction, reference).normalized;
            Vector3 up = Vector3.Cross(side, direction).normalized;
            for (int i = 0; i < Samples; i++)
            {
                float t = (float)i / (Samples - 1);
                float envelope = Mathf.SmoothStep(0f, 1f, t / .06f) * Mathf.SmoothStep(0f, 1f, (1f - t) / .06f);
                float angle = t * turns * Mathf.PI * 2f - phase;
                points[i] = Vector3.Lerp(start, end, t) + (side * Mathf.Cos(angle) + up * Mathf.Sin(angle)) * radius * envelope;
            }
            spiral.useWorldSpace = true;
            spiral.positionCount = Samples;
            spiral.SetPositions(points);
        }
        private void OnDisable() { if (spiral != null) spiral.enabled = false; }
    }
}
