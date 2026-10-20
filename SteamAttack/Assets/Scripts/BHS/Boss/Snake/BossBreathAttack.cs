using UnityEngine;
using UnityEngine.Rendering.Universal;

public class BossBreathAttack : BossAttack
{
    [Header("뱀 FSM - 공격 범위 기즈모 기준")]
    [Tooltip("비워두면 부모의 SnakeFSM 중 이 브레스가 Patterns에 등록된 것을 찾습니다. 실제 공격 실행은 FSM의 Patterns 등록으로 정합니다.")]
    public SnakeFSM snakeFSM;

    [Header("브레스 판정 - 예고 시작 시 플레이어 거리 기준")]
    [Tooltip("플레이어 거리의 앞뒤 폭. 1이면 거리 6에서 5~7m에 판정이 생깁니다. 예고 시작 후에는 고정됩니다.")]
    [Min(0.01f)] public float halfWidth = 1f;
    [Range(1f, 180f)] public float breathAngle = 60f;

    [Header("공격 전 기즈모 미리보기")]
    [Tooltip("플레이 전에도 범위를 확인할 대상. 실행 중에는 SnakeFSM의 실제 플레이어를 우선합니다. 실제 공격 대상에는 영향을 주지 않습니다.")]
    public Transform previewTarget;
    [Tooltip("미리보기 대상이 없을 때만 사용하는 거리입니다. 실제 공격 사거리는 아닙니다.")]
    [Min(0f)] public float previewDistance = 4f;

    [Header("사다리꼴 기둥 콜라이더")]
    [Tooltip("기둥 하나의 최대 각도. 60도 / 20도 = 3개. 먼 거리에서 폭이 좁으면 자동으로 더 잘게 나눕니다.")]
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

        // 실제 공격 발생 위치에서 잰 수평 거리입니다. 높이 차이는 사거리에 더하지 않습니다.
        Vector3 toPlayer = Target.position - Origin;
        toPlayer.y = 0f;
        Vector4 shape = GetShape(toPlayer.magnitude);
        float height = Mathf.Max(0.01f, hitboxHeight);
        if (!IsShapeValid(shape))
        {
            Debug.LogError("브레스 범위를 만들 수 없습니다. Half Width와 거리 설정을 확인하세요.", this);
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

    private Vector4 GetShape(float targetDistance)
    {
        float distance = Mathf.Max(0f, targetDistance);
        float width = Mathf.Max(0.01f, halfWidth);
        float inner = Mathf.Max(0.01f, distance - width);
        float outer = Mathf.Max(inner + 0.01f, distance + width);

        // 사다리꼴의 바깥 변은 원의 현입니다. 멀어질수록 더 잘게 나누어
        // 폭이 좁아도 구간 중앙까지 실제 콜라이더가 포함하도록 합니다.
        float middle = (inner + outer) * 0.5f;
        float safeAngle = 2f * Mathf.Acos(Mathf.Clamp01(middle / outer)) * Mathf.Rad2Deg;
        float pieceAngle = Mathf.Min(Mathf.Clamp(segmentAngle, 1f, 60f),
            Mathf.Max(0.01f, safeAngle * 0.95f));
        return new Vector4(inner, outer, Mathf.Clamp(breathAngle, 1f, 180f), pieceAngle);
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

    // 실행 중인 공격은 실제 hitboxRoot를 그린다.
    // 아래 함수는 공격 전 미리보기의 위치/방향/거리를 계산한다.
    private void GetGizmoPose(out Vector3 floor, out Quaternion facing, out float distance)
    {
        SnakeFSM source = snakeFSM;
        if (source == null)
        {
            SnakeFSM candidate = GetComponentInParent<SnakeFSM>();
            if (candidate != null && candidate.ContainsPattern(this)) source = candidate;
        }

        Transform positionOrigin = transform;
        Transform directionOrigin = transform;
        if (source != null)
        {
            Transform range = source.rangeOrigin != null ? source.rangeOrigin : source.transform;
            positionOrigin = source.attackOrigin != null ? source.attackOrigin : range;
            directionOrigin = range;
        }

        floor = new Vector3(positionOrigin.position.x,
            groundY + hitboxBottomOffset, positionOrigin.position.z);
        facing = Quaternion.Euler(0f, directionOrigin.eulerAngles.y, 0f);
        distance = Mathf.Max(0f, previewDistance);
        Transform target = source != null ? source.Player : null;
        if (target == null || !target.gameObject.activeInHierarchy) target = previewTarget;
        // 플레이어가 지정되어 있으면 SnakeFSM과 동일하게 발생 위치에서 수평 조준한다.
        if (target != null && target.gameObject.activeInHierarchy)
        {
            Vector3 direction = target.position - positionOrigin.position;
            direction.y = 0f;
            distance = direction.magnitude;
            if (direction.sqrMagnitude >= 0.0001f)
                facing = Quaternion.LookRotation(direction, Vector3.up);
        }
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
            GetGizmoPose(out Vector3 floor, out Quaternion facing, out float distance);
            Vector4 shape = GetShape(distance);
            if (IsShapeValid(shape))
            {
                Gizmos.matrix = Matrix4x4.TRS(floor, facing, Vector3.one);
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
