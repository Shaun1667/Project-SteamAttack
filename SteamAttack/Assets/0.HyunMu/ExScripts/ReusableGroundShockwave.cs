using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace HyunMuEffects
{
    /// <summary>Visual-only pooled pressure wave. Distorts the rendered floor without editing it.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class ReusableGroundShockwave : MonoBehaviour
    {
        [Header("재생 / 월드 단위")]
        public bool playOnEnable = true;
        public bool loop;
        [Min(0f)] public float repeatDelay = .65f;
        [Min(.1f)] public float radius = 5f;
        [Min(.1f)] public float speed = 5f;
        [Min(.05f)] public float width = 1f;
        [Header("압력막 / 굴절")]
        [Min(0f)] public float crestHeight = .28f;
        [Range(.02f, .4f)] public float rimWidth = .1f;
        [Range(0f, 1.5f)] public float refractionStrength = .45f;
        public Color color = new Color(.78f, .91f, 1f, .9f);
        [Tooltip("실행 중 Game 카메라의 Opaque/Depth Texture를 켜고 재생이 끝나면 원래 설정으로 복원합니다. URP 3D Renderer용입니다.")]
        public bool autoConfigureGameCameras = true;
        [Header("지면 감지")]
        public LayerMask groundMask = Physics.DefaultRaycastLayers;
        [Min(.1f)] public float probeHeight = 3f;
        [Min(.1f)] public float probeDepth = 8f;
        [Min(.001f)] public float groundOffset = .03f;
        [Tooltip("새 재생마다 지면을 다시 읽습니다. 지면 Collider가 없으면 배치한 높이의 평면에서 재생합니다.")]
        public bool refreshGroundEachPlay = true;
        [Range(.2f, 2f)] public float groundSampleSpacing = .5f;
        [Header("메시 품질")]
        [Range(32, 192)] public int segments = 96;
        [Range(4, 16)] public int radialSlices = 8;

        public bool IsPlaying { get; private set; }
        public Vector2 VisibleRadii { get; private set; }
        public Transform IgnoreRoot { get; set; }
        MeshFilter filter;
        MeshRenderer surface;
        Mesh mesh, originalMesh;
        MaterialPropertyBlock block;
        Vector3[] vertices, directions;
        Vector2[] uvs;
        float[] heights;
        readonly RaycastHit[] hits = new RaycastHit[32];
        int builtSegments, builtSlices, groundRows;
        float sampledRadius = -1f, elapsed, delayLeft, activeRadius, activeWidth;
        Vector3 origin;
        bool pendingPlay, external, lease, cacheReady;

        static readonly int OriginId = Shader.PropertyToID("_Origin");
        static readonly int OuterId = Shader.PropertyToID("_OuterRadius");
        static readonly int InnerId = Shader.PropertyToID("_InnerRadius");
        static readonly int RimId = Shader.PropertyToID("_RimWidth");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int FadeId = Shader.PropertyToID("_Fade");
        static readonly int RefractionId = Shader.PropertyToID("_RefractionStrength");
        static readonly int AvailableId = Shader.PropertyToID("_RefractionAvailable");

        void Awake() { EnsureMesh(); }
        void OnEnable() { pendingPlay = playOnEnable; external = false; delayLeft = 0f; }
        void OnDisable() { Stop(); }
        void OnDestroy()
        {
            ReleaseCameraLease();
            if (filter != null && filter.sharedMesh == mesh) filter.sharedMesh = originalMesh;
            if (mesh != null) Destroy(mesh);
        }

        void Update()
        {
            if (external) return;
            if (pendingPlay) { Play(); return; }
            if (!IsPlaying)
            {
                if (delayLeft > 0f && loop)
                {
                    delayLeft -= Time.deltaTime;
                    if (delayLeft <= 0f) Play();
                }
                return;
            }
            elapsed += Time.deltaTime;
            float front = elapsed * Mathf.Max(.1f, speed);
            if (front >= activeRadius + activeWidth)
            {
                Stop();
                if (loop) delayLeft = Mathf.Max(.001f, repeatDelay);
                return;
            }
            Draw(Mathf.Max(0f, front - activeWidth), Mathf.Min(front, activeRadius), color, rimWidth);
        }

        [ContextMenu("Play (실행 모드)")]
        public void Play()
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            pendingPlay = external = false;
            elapsed = delayLeft = 0f;
            activeRadius = Mathf.Max(.1f, radius);
            activeWidth = Mathf.Clamp(width, .05f, activeRadius);
            Begin(transform.position, activeRadius);
            Draw(0f, .001f, color, rimWidth);
        }

        public void PlayAt(Vector3 worldPosition)
        {
            transform.position = worldPosition;
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            Play();
        }

        [ContextMenu("Stop (실행 모드)")]
        public void Stop()
        {
            pendingPlay = external = IsPlaying = false;
            delayLeft = 0f;
            VisibleRadii = Vector2.zero;
            if (surface != null) surface.enabled = false;
            ReleaseCameraLease();
        }

        // Optional adapter: an existing attack can supply its exact collision front without adding damage here.
        public void RenderWave(float inner, float outer, float maxRadius, float bandWidth, Color tint, float edgeWidth)
        {
            if (!Application.isPlaying || !isActiveAndEnabled) return;
            maxRadius = Mathf.Max(.1f, maxRadius);
            if (!external || !IsPlaying || (origin - transform.position).sqrMagnitude > .0001f ||
                !Mathf.Approximately(activeRadius, maxRadius)) Begin(transform.position, maxRadius);
            pendingPlay = false;
            external = true;
            activeRadius = maxRadius;
            activeWidth = Mathf.Clamp(bandWidth, .05f, maxRadius);
            Draw(Mathf.Clamp(inner, 0f, maxRadius), Mathf.Clamp(outer, 0f, maxRadius), tint, edgeWidth);
        }

        void Begin(Vector3 position, float maxRadius)
        {
            EnsureMesh();
            bool moved = (origin - position).sqrMagnitude > .0001f;
            origin = position;
            activeRadius = maxRadius;
            if (!cacheReady || moved || refreshGroundEachPlay || !Mathf.Approximately(sampledRadius, maxRadius)) SampleGround(maxRadius);
            IsPlaying = true;
            AcquireCameraLease();
        }

        void EnsureMesh()
        {
            if (filter == null)
            {
                filter = GetComponent<MeshFilter>();
                surface = GetComponent<MeshRenderer>();
                originalMesh = filter.sharedMesh;
                block = new MaterialPropertyBlock();
                mesh = new Mesh { name = "Reusable Pressure Surface", hideFlags = HideFlags.DontSave };
                mesh.MarkDynamic();
                filter.sharedMesh = mesh;
                surface.shadowCastingMode = ShadowCastingMode.Off;
                surface.receiveShadows = false;
                surface.enabled = false;
            }
            int n = Mathf.Clamp(segments, 32, 192), rows = Mathf.Clamp(radialSlices, 4, 16);
            if (n == builtSegments && rows == builtSlices) return;
            builtSegments = n; builtSlices = rows; cacheReady = false;
            int columns = n + 1;
            vertices = new Vector3[columns * (rows + 1)];
            uvs = new Vector2[vertices.Length];
            directions = new Vector3[columns];
            int[] triangles = new int[n * rows * 6];
            for (int s = 0; s <= n; s++)
            {
                float a = 2f * Mathf.PI * s / n;
                directions[s] = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
                for (int r = 0; r <= rows; r++) uvs[r * columns + s] = new Vector2((float)s / n, (float)r / rows);
            }
            int index = 0;
            for (int r = 0; r < rows; r++) for (int s = 0; s < n; s++)
            {
                int a = r * columns + s, b = a + columns;
                triangles[index++] = a; triangles[index++] = b; triangles[index++] = a + 1;
                triangles[index++] = a + 1; triangles[index++] = b; triangles[index++] = b + 1;
            }
            mesh.Clear(); mesh.vertices = vertices; mesh.uv = uvs; mesh.triangles = triangles;
        }

        void SampleGround(float maxRadius)
        {
            sampledRadius = maxRadius;
            groundRows = Mathf.Clamp(Mathf.CeilToInt(maxRadius / Mathf.Max(.2f, groundSampleSpacing)), 1, 256);
            int count = (builtSegments + 1) * (groundRows + 1);
            if (heights == null || heights.Length != count) heights = new float[count];
            Physics.SyncTransforms();
            for (int r = 0; r <= groundRows; r++) for (int s = 0; s <= builtSegments; s++)
            {
                Vector3 p = origin + directions[s] * (maxRadius * r / groundRows);
                float y = origin.y, nearest = float.PositiveInfinity;
                int found = Physics.RaycastNonAlloc(p + Vector3.up * probeHeight, Vector3.down, hits,
                    probeHeight + probeDepth, groundMask, QueryTriggerInteraction.Ignore);
                for (int h = 0; h < found; h++)
                {
                    var hit = hits[h];
                    if (hit.normal.y < .35f || hit.collider.attachedRigidbody != null ||
                        hit.transform.IsChildOf(transform) || (IgnoreRoot != null && hit.transform.IsChildOf(IgnoreRoot))) continue;
                    if (hit.distance < nearest) { nearest = hit.distance; y = hit.point.y; }
                }
                heights[r * (builtSegments + 1) + s] = y;
            }
            cacheReady = true;
        }

        void Draw(float inner, float outer, Color tint, float edgeWidth)
        {
            VisibleRadii = new Vector2(inner, outer);
            if (outer <= inner) { surface.enabled = false; return; }
            float band = outer - inner, fade = Mathf.Clamp01(band / Mathf.Max(.05f, activeWidth));
            Matrix4x4 toLocal = transform.worldToLocalMatrix;
            int columns = builtSegments + 1;
            for (int r = 0; r <= builtSlices; r++)
            {
                float t = (float)r / builtSlices, distance = Mathf.Lerp(inner, outer, t);
                float rise = crestHeight * Mathf.Pow(Mathf.Clamp01(Mathf.Sin(t * Mathf.PI)), 1.7f) * fade;
                float f = Mathf.Clamp(distance / sampledRadius * groundRows, 0f, groundRows);
                int low = Mathf.Min((int)f, groundRows - 1), high = low + 1;
                for (int s = 0; s <= builtSegments; s++)
                {
                    Vector3 p = origin + directions[s] * distance;
                    p.y = Mathf.Lerp(heights[low * columns + s], heights[high * columns + s], f - low) + groundOffset + rise;
                    vertices[r * columns + s] = toLocal.MultiplyPoint3x4(p);
                }
            }
            mesh.vertices = vertices;
            mesh.RecalculateBounds();
            block.SetVector(OriginId, origin);
            block.SetFloat(OuterId, outer); block.SetFloat(InnerId, inner);
            block.SetFloat(RimId, edgeWidth); block.SetColor(ColorId, tint);
            block.SetFloat(FadeId, fade); block.SetFloat(RefractionId, refractionStrength);
            var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            block.SetFloat(AvailableId, urp != null && (lease || urp.supportsCameraOpaqueTexture) ? 1f : 0f);
            surface.SetPropertyBlock(block);
            surface.enabled = true;
        }

        // Shared leases keep overlapping waves from restoring camera settings while another one is visible.
        sealed class CameraState
        {
            public UniversalAdditionalCameraData data;
            public CameraOverrideOption color, depth;
            public bool added;
        }
        static int cameraUsers;
        static readonly Dictionary<Camera, CameraState> cameras = new Dictionary<Camera, CameraState>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RestoreCameras(); cameraUsers = 0;
        }
        void AcquireCameraLease()
        {
            if (lease || !autoConfigureGameCameras || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset)) return;
            lease = true;
            if (++cameraUsers != 1) return;
            RenderPipelineManager.beginCameraRendering += BeforeCamera;
            foreach (Camera camera in Camera.allCameras) ConfigureCamera(camera);
        }
        void ReleaseCameraLease()
        {
            if (!lease) return;
            lease = false;
            if (--cameraUsers > 0) return;
            cameraUsers = 0;
            RenderPipelineManager.beginCameraRendering -= BeforeCamera;
            RestoreCameras();
        }
        static void BeforeCamera(ScriptableRenderContext context, Camera camera) { ConfigureCamera(camera); }
        static void ConfigureCamera(Camera camera)
        {
            if (camera == null || camera.cameraType != CameraType.Game || cameras.ContainsKey(camera)) return;
            var data = camera.GetComponent<UniversalAdditionalCameraData>();
            bool added = data == null;
            if (added) data = camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
            cameras.Add(camera, new CameraState { data = data, color = data.requiresColorOption, depth = data.requiresDepthOption, added = added });
            data.requiresColorOption = CameraOverrideOption.On;
            data.requiresDepthOption = CameraOverrideOption.On;
        }
        static void RestoreCameras()
        {
            foreach (var pair in cameras)
            {
                var saved = pair.Value;
                if (saved.data == null) continue;
                saved.data.requiresColorOption = saved.color;
                saved.data.requiresDepthOption = saved.depth;
                // Keep newly added camera data at its original defaults until the camera is destroyed.
                // Destroying it here would race a new wave starting in the same frame.
            }
            cameras.Clear();
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.5f, .85f, 1f, .7f);
            Vector3 center = Application.isPlaying && IsPlaying ? origin : transform.position;
            for (int s = 0; s < 64; s++)
            {
                float a = 2f * Mathf.PI * s / 64, b = 2f * Mathf.PI * (s + 1) / 64;
                Gizmos.DrawLine(center + new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)) * radius,
                    center + new Vector3(Mathf.Sin(b), 0, Mathf.Cos(b)) * radius);
            }
        }
    }
}
