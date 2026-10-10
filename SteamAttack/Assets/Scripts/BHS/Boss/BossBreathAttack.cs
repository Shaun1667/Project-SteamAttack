using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BHS
{
    public class BossBreathAttack : BossAttack
    {
        [Header("브레스 판정 - 월드 거리")]
        [Min(0.01f)] public float innerRadius = 3f;
        [Min(0.02f)] public float outerRadius = 5f;
        [Range(1f, 180f)] public float breathAngle = 60f;

        [Header("사다리꼴 기둥 콜라이더")]
        [Tooltip("60도 / 20도 = 기둥 3개. 나누어떨어지지 않으면 마지막 조각만 작아집니다.")]
        [Range(1f, 60f)] public float segmentAngle = 20f;
        [Min(0.01f)] public float hitboxHeight = 2f;
        public float hitboxBottomOffset = 0f;
        public bool showHitboxGizmos = true;

        [Header("URP 데칼 예고 표시")]
        public Material warningMaterial;
        [Tooltip("바닥 전용 Rendering Layer를 선택하세요.")]
        public RenderingLayerMask warningRenderingLayers;
        public Color warningColor = new Color(1f, 0.15f, 0.1f, 0.5f);
        public float groundY = 0f;
        [Min(0.1f)] public float projectionDepth = 2f;

        private GameObject hitboxRoot;
        private MeshCollider[] hitboxes;
        private Mesh[] hitboxMeshes;
        private Vector3[][] footprints;
        private Collider[] playerColliders;
        private Vector4 builtShape;
        private float builtHeight;

        private GameObject warningObject;
        private DecalProjector warningProjector;
        private Material runtimeWarningMaterial;
        private Texture2D warningTexture;
        private Color previousColor;
        private bool warningInitialized;
        private const int TextureSize = 512;

        // 아래/위 각 2개, 옆면 각 2개: 총 12개 삼각형으로 닫힌 기둥.
        private static readonly int[] PrismTriangles =
        {
            0, 2, 1, 0, 3, 2,
            4, 5, 6, 4, 6, 7,
            0, 1, 5, 0, 5, 4,
            1, 2, 6, 1, 6, 5,
            2, 3, 7, 2, 7, 6,
            3, 0, 4, 3, 4, 7
        };

        protected override bool PrepareAttack()
        {
            if (Target == null || !Target.CompareTag("Player")) return false;

            // Player 태그가 부모에 있고 Collider가 자식에 있는 경우도 포함.
            playerColliders = Target.GetComponentsInChildren<Collider>(true);
            if (playerColliders.Length == 0)
            {
                Debug.LogWarning("Player 오브젝트 또는 자식에 3D Collider가 필요합니다.", this);
                return false;
            }

            Vector4 shape = GetShape();
            float height = Mathf.Max(0.01f, hitboxHeight);
            if (!IsShapeValid(shape))
            {
                Debug.LogError("반경 차이에 비해 Segment Angle이 큽니다. 각도를 줄이거나 Outer Radius를 늘려 주세요.", this);
                return false;
            }
            if (!EnsureWarningResources()) return false;
            EnsureHitboxes(shape, height);
            BuildWarningTexture();

            Vector3 floor = new Vector3(Origin.x, groundY, Origin.z);
            hitboxRoot.transform.SetPositionAndRotation(
                floor + Vector3.up * hitboxBottomOffset, Direction);
            hitboxRoot.SetActive(false);

            warningObject.transform.SetPositionAndRotation(
                floor, Direction * Quaternion.Euler(90f, 0f, 0f));
            warningObject.transform.localScale = Vector3.one;
            warningProjector.renderingLayerMask = warningRenderingLayers;
            warningProjector.size = new Vector3(
                shape.y * 2f, shape.y * 2f, Mathf.Max(0.1f, projectionDepth));
            warningObject.SetActive(true);
            return true;
        }

        protected override void StartActive()
        {
            if (warningObject != null) warningObject.SetActive(false);
            if (hitboxRoot != null) hitboxRoot.SetActive(true);
        }

        protected override void TickActive(float deltaTime)
        {
            if (!CanHitCollider || hitboxRoot == null || !hitboxRoot.activeInHierarchy) return;

            foreach (Collider playerCollider in playerColliders)
            {
                if (playerCollider == null || !playerCollider.enabled ||
                    !playerCollider.gameObject.activeInHierarchy ||
                    (playerCollider.transform != Target &&
                     !playerCollider.transform.IsChildOf(Target))) continue;

                foreach (MeshCollider hitbox in hitboxes)
                {
                    // 플레이어 기준점이 아닌 실제 Collider끼리의 겹침을 검사한다.
                    bool overlaps = Physics.ComputePenetration(
                        hitbox, hitbox.transform.position, hitbox.transform.rotation,
                        playerCollider, playerCollider.transform.position,
                        playerCollider.transform.rotation, out _, out _);

                    if (!overlaps) continue;
                    // 높이도 Collider로 판정했으므로 피벗 높이로 다시 제한하지 않는다.
                    HitOnce(checkTargetHeight: false);
                    if (hitboxRoot != null) hitboxRoot.SetActive(false);
                    return;
                }
            }
        }

        protected override void EndAttack()
        {
            if (warningObject != null) warningObject.SetActive(false);
            if (hitboxRoot != null) hitboxRoot.SetActive(false);
        }

        private Vector4 GetShape()
        {
            float inner = Mathf.Max(0.01f, innerRadius);
            return new Vector4(inner, Mathf.Max(inner + 0.01f, outerRadius),
                Mathf.Clamp(breathAngle, 1f, 180f), Mathf.Clamp(segmentAngle, 1f, 60f));
        }

        private static bool IsShapeValid(Vector4 shape)
        {
            float half = Mathf.Min(shape.z, shape.w) * 0.5f * Mathf.Deg2Rad;
            return shape.x / Mathf.Cos(half) < shape.y;
        }

        private static Vector3 PointOnRay(float angle, float radius)
        {
            float radians = angle * Mathf.Deg2Rad;
            return new Vector3(Mathf.Sin(radians), 0f, Mathf.Cos(radians)) * radius;
        }

        private static Vector3[] GetPrismVertices(Vector4 shape, int index, float height)
        {
            float start = -shape.z * 0.5f + index * shape.w;
            float end = Mathf.Min(start + shape.w, shape.z * 0.5f);

            // 안쪽 변이 반지름 inner인 원에 접하도록 보정해 안전지대를 침범하지 않는다.
            float innerCorner = shape.x / Mathf.Cos((end - start) * 0.5f * Mathf.Deg2Rad);
            Vector3[] vertices = new Vector3[8];
            vertices[0] = PointOnRay(start, innerCorner);
            vertices[1] = PointOnRay(start, shape.y);
            vertices[2] = PointOnRay(end, shape.y);
            vertices[3] = PointOnRay(end, innerCorner);
            for (int i = 0; i < 4; i++) vertices[i + 4] = vertices[i] + Vector3.up * height;
            return vertices;
        }

        private void EnsureHitboxes(Vector4 shape, float height)
        {
            if (hitboxRoot != null && builtShape == shape && builtHeight == height) return;
            DestroyHitboxes();

            // 부모 없음 / Scale 1로 만들어 보스의 크기 변경에 영향을 받지 않는다.
            hitboxRoot = new GameObject(name + "_BreathHitboxes");
            hitboxRoot.SetActive(false);
            int count = Mathf.CeilToInt(shape.z / shape.w);
            hitboxes = new MeshCollider[count];
            hitboxMeshes = new Mesh[count];
            footprints = new Vector3[count][];

            for (int i = 0; i < count; i++)
            {
                Vector3[] vertices = GetPrismVertices(shape, i, height);
                footprints[i] = new Vector3[] { vertices[0], vertices[1], vertices[2], vertices[3] };

                Mesh mesh = new Mesh { name = "BreathPrism_" + (i + 1) };
                mesh.vertices = vertices;
                mesh.triangles = PrismTriangles;
                mesh.RecalculateNormals();
                mesh.RecalculateBounds();
                hitboxMeshes[i] = mesh;

                GameObject piece = new GameObject("Piece_" + (i + 1));
                piece.transform.SetParent(hitboxRoot.transform, false);
                MeshCollider collider = piece.AddComponent<MeshCollider>();
                collider.convex = true;
                collider.isTrigger = true; // 밀어내는 벽이 되지 않게 한다.
                collider.sharedMesh = mesh;
                hitboxes[i] = collider;
            }
            builtShape = shape;
            builtHeight = height;
            warningInitialized = false;
        }

        private bool IsInsideFootprints(Vector2 point)
        {
            foreach (Vector3[] polygon in footprints)
            {
                bool inside = true;
                for (int i = 0; i < 4; i++)
                {
                    Vector3 a = polygon[i];
                    Vector3 b = polygon[(i + 1) % 4];
                    float cross = (b.x - a.x) * (point.y - a.z) -
                        (b.z - a.z) * (point.x - a.x);
                    if (cross > 0.000001f) { inside = false; break; }
                }
                if (inside) return true;
            }
            return false;
        }

        private void BuildWarningTexture()
        {
            if (warningInitialized && warningColor == previousColor) return;
            Color32[] pixels = new Color32[TextureSize * TextureSize];
            Color32 inside = warningColor;
            Color32 outside = new Color32(inside.r, inside.g, inside.b, 0);
            float diameter = builtShape.y * 2f;

            for (int y = 0; y < TextureSize; y++)
            {
                float z = ((y + 0.5f) / TextureSize - 0.5f) * diameter;
                for (int x = 0; x < TextureSize; x++)
                {
                    float localX = ((x + 0.5f) / TextureSize - 0.5f) * diameter;
                    pixels[y * TextureSize + x] =
                        IsInsideFootprints(new Vector2(localX, z)) ? inside : outside;
                }
            }
            warningTexture.SetPixels32(pixels);
            warningTexture.Apply(false, false);
            previousColor = warningColor;
            warningInitialized = true;
        }

        private bool EnsureWarningResources()
        {
            if (warningProjector != null) return true;
            if (warningMaterial == null)
            {
                Debug.LogError("BossBreathAttack의 Warning Material에 브레스 머티리얼을 연결하세요.", this);
                return false;
            }

            string baseMapProperty = FindBaseMapProperty(warningMaterial);
            if (string.IsNullOrEmpty(baseMapProperty))
            {
                Debug.LogError("연결된 머티리얼에 Base Map 텍스처 속성이 없습니다.", this);
                return false;
            }

            // 부모를 지정하지 않아 보스 Scale에 따라 데칼 크기가 변하지 않는다.
            warningObject = new GameObject(name + "_BreathWarning");
            warningObject.SetActive(false);
            runtimeWarningMaterial = new Material(warningMaterial);
            warningTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            warningTexture.name = "BreathWarningTexture";
            warningTexture.wrapMode = TextureWrapMode.Clamp;
            warningTexture.filterMode = FilterMode.Bilinear;

            runtimeWarningMaterial.SetTexture(baseMapProperty, warningTexture);
            runtimeWarningMaterial.SetTextureScale(baseMapProperty, Vector2.one);
            runtimeWarningMaterial.SetTextureOffset(baseMapProperty, Vector2.zero);
            if (runtimeWarningMaterial.HasProperty("_BaseColor"))
                runtimeWarningMaterial.SetColor("_BaseColor", Color.white);

            warningProjector = warningObject.AddComponent<DecalProjector>();
            warningProjector.material = runtimeWarningMaterial;
            warningProjector.pivot = Vector3.zero;
            warningProjector.uvScale = Vector2.one;
            warningProjector.uvBias = Vector2.zero;
            warningProjector.fadeFactor = 1f;
            warningProjector.drawDistance = 100f;

            if (!warningProjector.IsValid())
            {
                Debug.LogError("URP Decal Projector에서 사용할 수 없는 머티리얼입니다.", this);
                DestroyWarningResources();
                return false;
            }
            return true;
        }

        private string FindBaseMapProperty(Material material)
        {
            Shader shader = material.shader;
            if (shader == null) return null;

            string fallback = null;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture)
                    continue;

                string propertyName = shader.GetPropertyName(i);
                string displayName = shader.GetPropertyDescription(i);

                // Inspector에 보이는 Base Map의 실제 내부 이름을 찾는다.
                if (string.Equals(displayName, "Base Map", System.StringComparison.OrdinalIgnoreCase))
                    return propertyName;

                if (propertyName == "_BaseMap" || propertyName == "_Base_Map")
                    fallback = propertyName;
            }

            return fallback;
        }

        private void DestroyWarningResources()
        {
            if (warningObject != null) Destroy(warningObject);
            if (runtimeWarningMaterial != null) Destroy(runtimeWarningMaterial);
            if (warningTexture != null) Destroy(warningTexture);
            warningObject = null;
            warningProjector = null;
            runtimeWarningMaterial = null;
            warningTexture = null;
            warningInitialized = false;
        }

        private void DestroyHitboxes()
        {
            if (hitboxRoot != null)
            {
                hitboxRoot.SetActive(false);
                Destroy(hitboxRoot);
            }
            if (hitboxMeshes != null)
                foreach (Mesh mesh in hitboxMeshes)
                    if (mesh != null) Destroy(mesh);
            hitboxRoot = null;
            hitboxes = null;
            hitboxMeshes = null;
            footprints = null;
        }

        private void OnDestroy()
        {
            DestroyHitboxes();
            DestroyWarningResources();
        }

        private void OnDrawGizmosSelected()
        {
            if (!showHitboxGizmos) return;
            Matrix4x4 previousMatrix = Gizmos.matrix;
            Color previousGizmoColor = Gizmos.color;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 1f);

            if (IsRunning && hitboxRoot != null && hitboxMeshes != null)
            {
                Gizmos.matrix = hitboxRoot.transform.localToWorldMatrix;
                foreach (Mesh mesh in hitboxMeshes)
                    if (mesh != null) Gizmos.DrawWireMesh(mesh);
            }
            else
            {
                Vector4 shape = GetShape();
                if (IsShapeValid(shape))
                {
                    Vector3 floor = new Vector3(transform.position.x,
                        groundY + hitboxBottomOffset, transform.position.z);
                    Gizmos.matrix = Matrix4x4.TRS(floor,
                        Quaternion.Euler(0f, transform.eulerAngles.y, 0f), Vector3.one);
                    int count = Mathf.CeilToInt(shape.z / shape.w);
                    for (int i = 0; i < count; i++)
                    {
                        Vector3[] v = GetPrismVertices(shape, i, Mathf.Max(0.01f, hitboxHeight));
                        for (int j = 0; j < 4; j++)
                        {
                            int next = (j + 1) % 4;
                            Gizmos.DrawLine(v[j], v[next]);
                            Gizmos.DrawLine(v[j + 4], v[next + 4]);
                            Gizmos.DrawLine(v[j], v[j + 4]);
                        }
                    }
                }
            }
            Gizmos.matrix = previousMatrix;
            Gizmos.color = previousGizmoColor;
        }
    }
}
