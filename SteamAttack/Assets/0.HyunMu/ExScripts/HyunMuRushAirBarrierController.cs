using UnityEngine;

namespace HyunMuEffects
{
    [DisallowMultipleComponent]
    public sealed class HyunMuRushAirBarrierController : MonoBehaviour
    {
        [SerializeField] private GameObject effectRoot;
        [Tooltip("Follow the existing rush torch ParticleSystem. No player or attack code dependency.")]
        [SerializeField] private ParticleSystem activitySource;
        [SerializeField, Min(0.001f)] private float lineWidth = 0.052f;

        [Tooltip("Use short flank steam paths for the heavy boss instead of the scientist's air shell.")]
        [SerializeField] private bool steamSlipstream;
        private ParticleSystem[] steamLayers;
        private bool emitting;
        private LineRenderer[] windLines;
        private Vector3[][] linePoints;
        private int[] flowIndices;
        private float[] pulseOffsets;
        private float flowTime;
        [SerializeField, Range(0.2f, 3f)] private float flowSpeed = 1.35f;
        [SerializeField, Range(0.1f, 0.5f)] private float streakSpan = 0.34f;
        private float visibility;
        private static readonly float[] FlowAngles = { -35f, -10f, 15f, 40f, 65f, 90f, 115f, 140f, 165f, 190f, 215f };

        [Header("Air shell outside the character")]
        [SerializeField] private float shellCenterHeight = 1.08f;
        [SerializeField, Min(0.1f)] private float shellHalfWidth = 0.9f;
        [SerializeField, Min(0.1f)] private float shellHalfHeight = 1.16f;
        [SerializeField, Min(0.2f)] private float frontReach = 1.45f;
        [SerializeField, Min(0.1f)] private float trailingLength = 3.0f;
        
        private const float LineDrainSeconds = 0.2f;
        private const int SamplesPerLine = 32;

private void Awake()
        {
            if (effectRoot == null)
            {
                Transform child = transform.Find("ScientistRunAirBarrier");
                if (child != null) effectRoot = child.gameObject;
            }
            if (effectRoot == null)
            {
                Debug.LogError("HyunMuRushAirBarrierController needs an effect prefab child.", this);
                enabled = false;
                return;
            }
            steamLayers = effectRoot.GetComponentsInChildren<ParticleSystem>(true);
            foreach (var layer in steamLayers)
                layer.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            emitting = false;
            windLines = effectRoot.GetComponentsInChildren<LineRenderer>(true);
            linePoints = new Vector3[windLines.Length][];
            flowIndices = new int[windLines.Length];
            pulseOffsets = new float[windLines.Length];
            for (int i = 0; i < windLines.Length; i++)
            {
                LineRenderer line = windLines[i];
                string[] parts = line.name.Split('_');
                int index;
                flowIndices[i] = parts.Length > 1 && int.TryParse(parts[1], out index)
                    ? Mathf.Clamp(index, 0, FlowAngles.Length - 1) : i % FlowAngles.Length;
                pulseOffsets[i] = line.name.EndsWith("Stroke1") ? 0.5f : 0f;
                line.useWorldSpace = false;
                line.loop = false;
                line.positionCount = SamplesPerLine;
                linePoints[i] = new Vector3[SamplesPerLine];
            }
            flowTime = 0f;
            visibility = 0f;
            effectRoot.SetActive(false);
        }

private void Update()
        {
            if (effectRoot == null || windLines == null) return;
            bool shouldEmit = activitySource != null
                ? activitySource.gameObject.activeInHierarchy && activitySource.isPlaying
                : false;
            if (shouldEmit && !emitting)
            {
                effectRoot.SetActive(true);
                foreach (var layer in steamLayers) layer.Play(false);
            }
            else if (!shouldEmit && emitting)
            {
                foreach (var layer in steamLayers)
                    layer.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
            emitting = shouldEmit;
            visibility = Mathf.MoveTowards(visibility, shouldEmit ? 1f : 0f,
                Time.deltaTime / (shouldEmit ? 0.12f : LineDrainSeconds));
            bool particlesAlive = false;
            foreach (var layer in steamLayers)
                particlesAlive |= layer.IsAlive(false);
            if (visibility <= 0.001f && !particlesAlive)
            {
                if (effectRoot.activeSelf) effectRoot.SetActive(false);
                return;
            }
            flowTime += Time.deltaTime;
            if (!effectRoot.activeSelf) effectRoot.SetActive(true);
            UpdateAirLines();
        }

private void UpdateAirLines()
        {
            if (windLines == null || linePoints == null) return;
            for (int i = 0; i < windLines.Length; i++)
            {
                LineRenderer line = windLines[i];
                int stream = flowIndices[i];
                float angle = FlowAngles[stream] * Mathf.Deg2Rad;
                float radialX = Mathf.Cos(angle);
                float radialY = Mathf.Sin(angle);
                float tail = trailingLength * (0.90f + 0.10f * Mathf.Sin(stream * 1.7f));
                float span = streakSpan * (0.85f + 0.15f * Mathf.Sin(stream * 1.1f + 1f));
                float cycle = 1f + span;
                float head = Mathf.Repeat(flowTime * flowSpeed + stream * 0.137f
                    + pulseOffsets[i] * cycle, cycle);
                float first = Mathf.Max(0f, head - span);
                float last = Mathf.Min(1f, head);
                float visiblePart = Mathf.Clamp01((last - first) / Mathf.Max(0.01f, span));
                Vector3[] points = linePoints[i];
                for (int j = 0; j < points.Length; j++)
                {
                    // Move a short ribbon along the shell instead of drawing a stationary rail.
                    float t = Mathf.Lerp(first, last, j / (float)(points.Length - 1));
                    if (steamSlipstream)
                    {
                        points[j] = SteamPoint(t, stream, flowTime);
                        continue;
                    }
                    float radius;
                    float z;
                    float wake = Mathf.Clamp01((t - 0.42f) / 0.58f);
                    if (t <= 0.42f)
                    {
                        float capAngle = t / 0.42f * Mathf.PI * 0.5f;
                        radius = Mathf.Sin(capAngle);
                        z = -0.2f + (frontReach + 0.2f) * Mathf.Cos(capAngle);
                    }
                    else
                    {
                        radius = 1f + wake * 0.08f;
                        z = -0.2f - wake * tail;
                    }
                    float ripple = (0.5f + 0.5f * Mathf.Sin(flowTime * 5f - t * 8f + stream))
                        * 0.04f * wake;
                    float x = radialX * (shellHalfWidth * radius + ripple);
                    float y = shellCenterHeight + radialY * (shellHalfHeight * radius + ripple);
                    points[j] = new Vector3(x, y, z);
                }
                line.SetPositions(points);
                line.widthMultiplier = lineWidth * visibility * Mathf.Sqrt(visiblePart);
            }
        }



    

private void OnDisable()
        {
            visibility = 0f;
            emitting = false;
            if (steamLayers != null)
                foreach (var layer in steamLayers)
                    if (layer != null) layer.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (effectRoot != null) effectRoot.SetActive(false);
        }


private Vector3 SteamPoint(float t, int stream, float clock)
        {
            float side = stream < 2 ? -1f : 1f;
            bool upper = (stream & 1) != 0;
            Vector3 a = upper ? new Vector3(0.8f, 2.8f, 2.8f) : new Vector3(1.25f, 1.1f, 3.0f);
            Vector3 b = upper ? new Vector3(2.45f, 3.1f, 1.65f) : new Vector3(2.8f, 0.95f, 1.5f);
            Vector3 c = upper ? new Vector3(2.35f, 2.75f, -0.9f) : new Vector3(2.5f, 0.8f, -1.1f);
            Vector3 d = upper ? new Vector3(2.6f, 2.9f, -4.1f) : new Vector3(2.8f, 1.05f, -4.5f);
            float u = 1f - t;
            Vector3 p = u*u*u*a + 3f*u*u*t*b + 3f*u*t*t*c + t*t*t*d;
            float wake = Mathf.SmoothStep(0f, 1f, t);
            p.x = side * (p.x + Mathf.Sin(t*13f-clock*4.8f+stream)*0.08f*wake);
            p.y += Mathf.Sin(t*17f-clock*3.6f+stream)*0.11f*wake;
            return p;
        }
}
}