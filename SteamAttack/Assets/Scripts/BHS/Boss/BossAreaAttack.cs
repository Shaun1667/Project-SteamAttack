using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BHS
{
    public class BossAreaAttack : BossAttack
    {
        [Header("근거리 360도 공격 - 월드 거리")]
        [Min(0.01f)] public float radius = 3f;
        [Min(0.01f)] public float hitboxHeight = 2f;
        public float hitboxBottomOffset = 0f;

        [Header("바닥 원형 데칼")]
        public Material warningMaterial;
        public RenderingLayerMask warningRenderingLayers;
        public Color warningColor = new Color(1f, 0.5f, 0.05f, 0.5f);
        public float groundY = 0f;
        [Min(0.1f)] public float projectionDepth = 2f;

        private const int Sides = 48;
        private const int TextureSize = 256;
        private Collider[] playerColliders;
        private GameObject hitboxObject;
        private MeshCollider hitbox;
        private Mesh hitboxMesh;
        private Vector2 builtSize;
        private GameObject warningObject;
        private DecalProjector projector;
        private Material runtimeMaterial;
        private Texture2D warningTexture;
        private Color previousColor;
        private bool textureReady;

        // 컴포넌트를 처음 붙일 때의 기본값.
        private void Reset()
        {
            minUseDistance = 0f;
            maxUseDistance = 3f;
            readyTime = 1f;
            attackTime = 0.3f;
            recoveryTime = 1f;
        }

        public override bool CanUse(float distance)
        {
            return base.CanUse(distance) && distance <= Mathf.Max(0.01f, radius);
        }

        protected override bool PrepareAttack()
        {
            if (Target == null || !Target.CompareTag("Player")) return false;

            // FSM이 찾은 플레이어와 자식의 콜라이더를 자동으로 가져온다.
            playerColliders = Target.GetComponentsInChildren<Collider>(true);
            if (playerColliders.Length == 0)
            {
                Debug.LogWarning("Player 또는 그 자식에 3D Collider가 필요합니다.", this);
                return false;
            }
            if (!CreateWarning()) return false;

            CreateHitbox();
            UpdateWarningTexture();
            Vector3 floor = new Vector3(Origin.x, groundY, Origin.z);
            hitboxObject.transform.SetPositionAndRotation(
                floor + Vector3.up * hitboxBottomOffset, Quaternion.identity);
            hitboxObject.SetActive(false);
            warningObject.transform.SetPositionAndRotation(floor, Quaternion.Euler(90f, 0f, 0f));
            projector.size = new Vector3(builtSize.x * 2f, builtSize.x * 2f,
                Mathf.Max(0.1f, projectionDepth));
            projector.renderingLayerMask = warningRenderingLayers;
            warningObject.SetActive(true);
            return true;
        }

        protected override void StartActive()
        {
            warningObject.SetActive(false);
            hitboxObject.SetActive(true);
        }

        protected override void TickActive(float deltaTime)
        {
            if (!CanHitCollider || hitbox == null || !hitboxObject.activeInHierarchy) return;
            foreach (Collider targetCollider in playerColliders)
            {
                if (targetCollider == null || !targetCollider.enabled ||
                    !targetCollider.gameObject.activeInHierarchy ||
                    (targetCollider.transform != Target &&
                     !targetCollider.transform.IsChildOf(Target))) continue;

                if (!Physics.ComputePenetration(
                    hitbox, hitbox.transform.position, hitbox.transform.rotation,
                    targetCollider, targetCollider.transform.position,
                    targetCollider.transform.rotation, out _, out _)) continue;

                HitOnce(checkTargetHeight: false);
                hitboxObject.SetActive(false);
                return;
            }
        }

        protected override void EndAttack()
        {
            if (warningObject != null) warningObject.SetActive(false);
            if (hitboxObject != null) hitboxObject.SetActive(false);
        }

        private void CreateHitbox()
        {
            Vector2 size = new Vector2(Mathf.Max(0.01f, radius), Mathf.Max(0.01f, hitboxHeight));
            if (hitbox != null && builtSize == size) return;
            if (hitboxObject == null)
            {
                // 부모 없음 / Scale 1이므로 보스의 크기 변경에 영향받지 않는다.
                hitboxObject = new GameObject(name + "_AreaHitbox");
                hitboxObject.SetActive(false);
                hitbox = hitboxObject.AddComponent<MeshCollider>();
                hitbox.convex = true;
                hitbox.isTrigger = true;
            }
            hitbox.sharedMesh = null;
            if (hitboxMesh != null) Destroy(hitboxMesh);

            // 바닥이 48각형인 기둥. 가운데가 채워지고 높이에 따라 반경이 줄지 않는다.
            Vector3[] vertices = new Vector3[Sides * 2];
            var triangles = new System.Collections.Generic.List<int>();
            for (int i = 0; i < Sides; i++)
            {
                float angle = i * Mathf.PI * 2f / Sides;
                vertices[i] = new Vector3(Mathf.Sin(angle) * size.x, 0f, Mathf.Cos(angle) * size.x);
                vertices[i + Sides] = vertices[i] + Vector3.up * size.y;
                int next = (i + 1) % Sides;
                triangles.AddRange(new int[] { i, next, i + Sides, next, next + Sides, i + Sides });
            }
            for (int i = 1; i < Sides - 1; i++)
                triangles.AddRange(new int[] { 0, i + 1, i, Sides, Sides + i, Sides + i + 1 });

            hitboxMesh = new Mesh { name = "AreaCylinder" };
            hitboxMesh.vertices = vertices;
            hitboxMesh.SetTriangles(triangles, 0);
            hitboxMesh.RecalculateBounds();
            hitbox.sharedMesh = hitboxMesh;
            builtSize = size;
        }

        private bool CreateWarning()
        {
            if (projector != null) return true;
            string map = warningMaterial != null ? FindBaseMap(warningMaterial) : null;
            if (string.IsNullOrEmpty(map))
            {
                Debug.LogError("Warning Material에 브레스에서 사용하는 Decal 머티리얼을 넣어 주세요.", this);
                return false;
            }
            warningObject = new GameObject(name + "_AreaWarning");
            warningObject.SetActive(false);
            runtimeMaterial = new Material(warningMaterial);
            warningTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            warningTexture.wrapMode = TextureWrapMode.Clamp;
            warningTexture.filterMode = FilterMode.Bilinear;
            runtimeMaterial.SetTexture(map, warningTexture);
            runtimeMaterial.SetTextureScale(map, Vector2.one);
            runtimeMaterial.SetTextureOffset(map, Vector2.zero);
            if (runtimeMaterial.HasProperty("_BaseColor"))
                runtimeMaterial.SetColor("_BaseColor", Color.white);
            projector = warningObject.AddComponent<DecalProjector>();
            projector.material = runtimeMaterial;
            projector.pivot = Vector3.zero;
            projector.uvScale = Vector2.one;
            projector.uvBias = Vector2.zero;
            projector.fadeFactor = 1f;
            projector.drawDistance = 100f;
            if (projector.IsValid()) return true;
            Debug.LogError("URP Decal Projector용 머티리얼이 필요합니다.", this);
            DestroyWarning();
            return false;
        }

        private static string FindBaseMap(Material material)
        {
            Shader shader = material.shader;
            if (shader == null) return null;
            string fallback = null;
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                string property = shader.GetPropertyName(i);
                if (string.Equals(shader.GetPropertyDescription(i), "Base Map",
                    System.StringComparison.OrdinalIgnoreCase)) return property;
                if (property == "_BaseMap" || property == "_Base_Map") fallback = property;
            }
            return fallback;
        }

        private void UpdateWarningTexture()
        {
            if (textureReady && previousColor == warningColor) return;
            Color32[] pixels = new Color32[TextureSize * TextureSize];
            Color32 inside = warningColor;
            Color32 outside = new Color32(inside.r, inside.g, inside.b, 0);
            float step = Mathf.PI * 2f / Sides;
            float apothem = Mathf.Cos(step * 0.5f);
            for (int y = 0; y < TextureSize; y++)
            {
                for (int x = 0; x < TextureSize; x++)
                {
                    Vector2 p = new Vector2((x + 0.5f) / TextureSize * 2f - 1f,
                        (y + 0.5f) / TextureSize * 2f - 1f);
                    // 실제 콜라이더의 48각형 외곽과 같은 모양으로 표시.
                    float offset = Mathf.Repeat(Mathf.Atan2(p.x, p.y), step) - step * 0.5f;
                    float edge = apothem / Mathf.Cos(offset);
                    pixels[y * TextureSize + x] = p.sqrMagnitude <= edge * edge ? inside : outside;
                }
            }
            warningTexture.SetPixels32(pixels);
            warningTexture.Apply(false, false);
            previousColor = warningColor;
            textureReady = true;
        }

        private void DestroyWarning()
        {
            if (warningObject != null) Destroy(warningObject);
            if (runtimeMaterial != null) Destroy(runtimeMaterial);
            if (warningTexture != null) Destroy(warningTexture);
            warningObject = null;
            projector = null;
            runtimeMaterial = null;
            warningTexture = null;
            textureReady = false;
        }

        private void OnDestroy()
        {
            EndAttack();
            if (hitboxObject != null) Destroy(hitboxObject);
            if (hitboxMesh != null) Destroy(hitboxMesh);
            DestroyWarning();
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 center = IsRunning && hitboxObject != null ? hitboxObject.transform.position :
                new Vector3(transform.position.x, groundY + hitboxBottomOffset, transform.position.z);
            float r = IsRunning ? builtSize.x : Mathf.Max(0.01f, radius);
            float height = IsRunning ? builtSize.y : Mathf.Max(0.01f, hitboxHeight);
            Color oldColor = Gizmos.color;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.identity;
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            for (int i = 0; i < Sides; i++)
            {
                float a = i * Mathf.PI * 2f / Sides;
                float b = (i + 1) * Mathf.PI * 2f / Sides;
                Vector3 p = center + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                Vector3 q = center + new Vector3(Mathf.Sin(b) * r, 0f, Mathf.Cos(b) * r);
                Gizmos.DrawLine(p, q);
                Gizmos.DrawLine(p + Vector3.up * height, q + Vector3.up * height);
                if (i % 12 == 0) Gizmos.DrawLine(p, p + Vector3.up * height);
            }
            Gizmos.color = oldColor;
            Gizmos.matrix = oldMatrix;
        }
    }
}
