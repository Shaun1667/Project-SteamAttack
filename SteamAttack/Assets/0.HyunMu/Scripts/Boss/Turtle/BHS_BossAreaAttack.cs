using UnityEngine;
using UnityEngine.Rendering.Universal;

public class BossAreaAttack : BossAttack
{
    [Header("현무 Animator - Bool 이름은 IsStamping")]
    public Animator animator;

    private bool controlsAnimation;
    private float animationWaitTime;
    private static readonly int StampStateHash = Animator.StringToHash("Base Layer.BHS_StampBe");
    private static readonly int IsStampingHash = Animator.StringToHash("IsStamping");

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

    [Header("본체 공격 후 확장 파동")]
    public bool enableWave = true;
    [Tooltip("Attack Time으로 지정한 본체 판정이 끝난 뒤 파동이 나오기까지의 시간입니다.")]
    [Min(0f)] public float waveDelay = 0.3f;
    [Tooltip("파동이 도달하는 최대 월드 반경입니다. 공격 발동 거리는 Max Use Distance로 별도 지정합니다.")]
    [Min(0.1f)] public float waveMaxRadius = 8f;
    [Tooltip("가운데가 빈 고리의 두께입니다. 반경이 커져도 이 폭을 유지합니다.")]
    [Min(0.05f)] public float waveWidth = 1f;
    [Min(0.1f)] public float waveSpeed = 5f;
    [Tooltip("파동의 실제 충돌 높이입니다. 플레이어 콜라이더가 이 높이 위로 올라가면 피할 수 있습니다.")]
    [Min(0.01f)] public float waveHeight = 0.5f;
    public float waveBottomOffset = 0f;
    [Min(0f)] public float waveDamage = 2f;
    public Color waveColor = new Color(1f, 0.55f, 0.05f, 0.65f);
    public bool showWaveGizmos = true;

    [Header("지면 충격파 VFX")]
    public GameObject waveShockwavePrefab;
    [Tooltip("충격파 앞쪽 테두리의 월드 두께입니다. 피해 범위는 Wave Width를 따릅니다.")]
    [Range(.04f, .4f)] public float shockwaveRimWidth = .14f;
    public Color shockwaveColor = new Color(.78f, .91f, 1f, .9f);
    private GameObject shockwave;
    private HyunMuEffects.ReusableGroundShockwave shockwaveEffect;

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

    // Attack Time은 본체 판정 시간. 전체 공격은 파동의 뒤쪽 경계가 빠져나갈 때 끝난다.
    protected override float ActiveDuration => lockedDuration;
    protected override bool ContinueWithoutTarget => true;
    private float elapsed;
    private float stompDuration;
    private float waveStartTime;
    private float waveDuration;
    private float lockedDuration;
    private float lockedWaveMaxRadius;
    private float lockedWaveWidth;
    private float lockedWaveSpeed;
    private float lockedWaveHeight;
    private float lockedWaveDamage;
    private Color lockedWaveColor;
    private bool waveEnabled;
    private bool waveStarted;
    private Vector3 wavePosition;
    private Vector2 visibleWaveRadii;

    private GameObject waveQueryObject;
    private MeshCollider waveQueryCollider;
    private Mesh waveQueryMesh;
    private readonly Vector3[] waveVertices = new Vector3[8];
    private readonly Quaternion[] waveRotations = new Quaternion[Sides];
    private GameObject waveVisualObject;
    private DecalProjector waveProjector;
    private Material waveMaterial;
    private Texture2D waveTexture;
    private Color32[] wavePixels;
    private float[] polygonDistances;

    // 한 조각을 가상의 48개 회전 위치에서 검사한다. 가운데를 채운 볼록 원기둥을 쓰지 않는다.
    private static readonly int[] WavePrismTriangles =
    {
        0, 2, 1, 0, 3, 2,
        4, 5, 6, 4, 6, 7,
        0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5,
        2, 3, 7, 2, 7, 6,
        3, 0, 4, 3, 4, 7
    };

    // 파동 발생 직후 안쪽 반경이 0일 때는 삼각 기둥이다.
    private static readonly int[] WaveCenterTriangles =
    {
        0, 2, 1, 4, 5, 6,
        0, 1, 5, 0, 5, 4,
        1, 2, 6, 1, 6, 5,
        2, 0, 4, 2, 4, 6
    };

    // 컴포넌트를 처음 붙일 때의 기본값.
    private void Reset()
    {
        minUseDistance = 0f;
        maxUseDistance = 8f;
        readyTime = 1f;
        attackTime = 0.3f;
        recoveryTime = 1f;
    }

    public override bool CanUse(float distance)
    {
        float reach = Mathf.Max(0.01f, radius);
        if (enableWave) reach = Mathf.Max(reach, Mathf.Max(0.1f, waveMaxRadius));
        return base.CanUse(distance) && distance <= reach;
    }

    protected override bool PrepareAttack()
    {
        if (Target == null || !Target.CompareTag("Player")) return false;

        // 새 Inspector 연결이 비어 있어도 기존 거북이 Animator를 자동으로 사용한다.
        if (animator == null && Owner != null)
        {
            foreach (Animator candidate in Owner.GetComponentsInChildren<Animator>(true))
            {
                if (!candidate.isActiveAndEnabled || candidate.runtimeAnimatorController == null ||
                    !candidate.HasState(0, StampStateHash)) continue;
                animator = candidate;
                break;
            }
        }
        if (animator == null || !animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null || !animator.HasState(0, StampStateHash))
        {
            Debug.LogWarning("Area 공격에 BHS_StampBe 상태가 있는 활성 Animator를 연결해 주세요.", this);
            return false;
        }
        bool hasStampParameter = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == IsStampingHash && parameter.type == AnimatorControllerParameterType.Bool)
                hasStampParameter = true;
        if (!hasStampParameter)
        {
            Debug.LogWarning("Animator에 Bool 파라미터 IsStamping이 필요합니다.", this);
            return false;
        }
        animationWaitTime = 0f;

        // FSM이 찾은 플레이어와 자식의 콜라이더를 자동으로 가져온다.
        playerColliders = Target.GetComponentsInChildren<Collider>(true);
        if (playerColliders.Length == 0)
        {
            Debug.LogWarning("Player 또는 그 자식에 3D Collider가 필요합니다.", this);
            return false;
        }
        if (!CreateWarning()) return false;

        // 예고 시작 때 설정을 고정하여 표시·판정·종료 시점을 일치시킨다.
        waveEnabled = enableWave;
        stompDuration = Mathf.Max(0.01f, attackTime);
        waveStartTime = stompDuration + Mathf.Max(0f, waveDelay);
        lockedWaveMaxRadius = Mathf.Max(0.1f, waveMaxRadius);
        lockedWaveWidth = Mathf.Clamp(waveWidth, 0.05f, lockedWaveMaxRadius);
        lockedWaveSpeed = Mathf.Max(0.1f, waveSpeed);
        lockedWaveHeight = Mathf.Max(0.01f, waveHeight);
        lockedWaveDamage = Mathf.Max(0f, waveDamage);
        lockedWaveColor = waveColor;
        waveDuration = (lockedWaveMaxRadius + lockedWaveWidth) / lockedWaveSpeed;
        lockedDuration = waveEnabled ? waveStartTime + waveDuration : stompDuration;
        elapsed = 0f;
        waveStarted = false;
        visibleWaveRadii = Vector2.zero;
        if (waveEnabled && !CreateWaveResources()) return false;

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
        wavePosition = floor + Vector3.up * waveBottomOffset;
        if (waveEnabled)
        {
            waveQueryObject.transform.SetPositionAndRotation(wavePosition, Quaternion.identity);
            waveQueryObject.SetActive(true);
            waveVisualObject.transform.SetPositionAndRotation(floor, Quaternion.Euler(90f, 0f, 0f));
            waveProjector.size = new Vector3(lockedWaveMaxRadius * 2f,
                lockedWaveMaxRadius * 2f, Mathf.Max(0.1f, projectionDepth));
            waveProjector.renderingLayerMask = warningRenderingLayers;
            waveVisualObject.SetActive(false);
        }
        return true;
    }

    // Ready Time 이후 준비 동작이 실제로 완료되어야 Attack Time을 시작한다.
    protected override bool WaitBeforeActive(float deltaTime)
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
        {
            CancelAttack();
            return true;
        }
        if (!controlsAnimation)
        {
            controlsAnimation = true;
            animationWaitTime = 0f;
            animator.SetBool(IsStampingHash, true);
            return true;
        }
        animationWaitTime += deltaTime;
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (!animator.IsInTransition(0) && state.fullPathHash == StampStateHash)
            return state.normalizedTime < 1f;

        // 잘못된 전이 연결로 보스가 준비 상태에 영구히 멈추는 것을 방지한다.
        if (animationWaitTime >= 5f)
        {
            Debug.LogWarning("IsStamping으로 BHS_StampBe에 진입하지 못했습니다. Animator 전이를 확인하세요.", this);
            CancelAttack();
        }
        return true;
    }

    protected override void StartActive()
    {
        warningObject.SetActive(false);
        hitboxObject.SetActive(true);
    }

    protected override void TickActive(float deltaTime)
    {
        float previousTime = elapsed;
        elapsed += deltaTime;
        if (previousTime < stompDuration)
        {
            TickStomp();
            // 피격 이벤트가 보스 또는 공격을 중단했을 수 있다.
            if (!IsRunning) return;
        }
        if (elapsed >= stompDuration)
        {
            StopStampAnimation();
            if (hitboxObject != null) hitboxObject.SetActive(false);
        }
        if (!waveEnabled || elapsed < waveStartTime) return;

        if (!waveStarted)
        {
            waveStarted = true;
            // 본체 판정이 끝난 뒤 딱 한 번 초기화하므로 본체 1회 + 파동 1회가 가능하다.
            BeginNextHitPhase();
        }
        float previousAge = Mathf.Clamp(previousTime - waveStartTime, 0f, waveDuration);
        float age = Mathf.Clamp(elapsed - waveStartTime, 0f, waveDuration);
        float previousFront = previousAge * lockedWaveSpeed;
        float front = age * lockedWaveSpeed;
        visibleWaveRadii = new Vector2(Mathf.Max(0f, front - lockedWaveWidth),
            Mathf.Min(front, lockedWaveMaxRadius));
        UpdateWaveVisual();
        UpdateShockwave();

        // 이번 프레임에 파동이 지나간 구간까지 검사하여 빠른 파동의 누락을 줄인다.
        float sweptInner = Mathf.Max(0f, previousFront - lockedWaveWidth);
        float sweptOuter = Mathf.Min(front, lockedWaveMaxRadius);
        if (CanHitCollider && sweptOuter > sweptInner && sweptOuter >= 0.01f)
            CheckWaveHit(sweptInner, sweptOuter);
    }

    private void TickStomp()
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
            if (hitboxObject != null) hitboxObject.SetActive(false);
            return;
        }
    }

    private void StopStampAnimation()
    {
        if (!controlsAnimation) return;
        controlsAnimation = false;
        if (animator != null) animator.SetBool(IsStampingHash, false);
    }

    protected override void EndAttack()
    {
        StopStampAnimation();
        if (warningObject != null) warningObject.SetActive(false);
        if (hitboxObject != null) hitboxObject.SetActive(false);
        if (waveVisualObject != null) waveVisualObject.SetActive(false);
        if (waveQueryObject != null) waveQueryObject.SetActive(false);
        waveStarted = false;
        if (shockwave != null) shockwave.SetActive(false);
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

    private bool CreateWaveResources()
    {
        if (waveProjector != null && waveQueryCollider != null && waveQueryMesh != null) return true;
        DestroyWaveResources();
        string map = warningMaterial != null ? FindBaseMap(warningMaterial) : null;
        if (string.IsNullOrEmpty(map))
        {
            Debug.LogError("파동에도 Warning Material의 Decal 머티리얼을 사용합니다. Base Map을 확인하세요.", this);
            return false;
        }

        // 조회 전용 모양 하나를 가상 위치에서 반복 사용한다. 평소에는 비활성 상태다.
        waveQueryObject = new GameObject(name + "_WaveQuery");
        waveQueryCollider = waveQueryObject.AddComponent<MeshCollider>();
        waveQueryCollider.enabled = false;
        waveQueryCollider.convex = true;
        waveQueryCollider.isTrigger = true;
        waveQueryMesh = new Mesh { name = "WavePrism" };
        for (int i = 0; i < Sides; i++)
            waveRotations[i] = Quaternion.Euler(0f, i * 360f / Sides, 0f);

        waveVisualObject = new GameObject(name + "_WaveDecal");
        waveVisualObject.SetActive(false);
        waveMaterial = new Material(warningMaterial);
        waveTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        waveTexture.name = "WaveRingTexture";
        waveTexture.wrapMode = TextureWrapMode.Clamp;
        waveTexture.filterMode = FilterMode.Bilinear;
        waveMaterial.SetTexture(map, waveTexture);
        waveMaterial.SetTextureScale(map, Vector2.one);
        waveMaterial.SetTextureOffset(map, Vector2.zero);
        if (waveMaterial.HasProperty("_BaseColor")) waveMaterial.SetColor("_BaseColor", Color.white);
        waveProjector = waveVisualObject.AddComponent<DecalProjector>();
        waveProjector.material = waveMaterial;
        waveProjector.pivot = Vector3.zero;
        waveProjector.uvScale = Vector2.one;
        waveProjector.uvBias = Vector2.zero;
        waveProjector.fadeFactor = 1f;
        waveProjector.drawDistance = 100f;
        if (!waveProjector.IsValid())
        {
            Debug.LogError("파동에 사용할 URP Decal 머티리얼을 확인하세요.", this);
            DestroyWaveResources();
            return false;
        }

        wavePixels = new Color32[TextureSize * TextureSize];
        polygonDistances = new float[wavePixels.Length];
        float step = Mathf.PI * 2f / Sides;
        float apothem = Mathf.Cos(step * 0.5f);
        for (int y = 0; y < TextureSize; y++)
        {
            for (int x = 0; x < TextureSize; x++)
            {
                Vector2 p = new Vector2((x + 0.5f) / TextureSize * 2f - 1f,
                    (y + 0.5f) / TextureSize * 2f - 1f);
                float offset = Mathf.Repeat(Mathf.Atan2(p.x, p.y), step) - step * 0.5f;
                float edge = apothem / Mathf.Cos(offset);
                // 같은 48각형 기준의 거리표를 한 번만 만든다. 매 프레임 삼각함수를 계산하지 않는다.
                polygonDistances[y * TextureSize + x] = p.magnitude / edge;
            }
        }
        return true;
    }

    // Only the VFX adapter changes: the collision radii and attack timing remain authoritative.
    private void UpdateShockwave()
    {
        if (waveShockwavePrefab == null) return;
        float inner = visibleWaveRadii.x, outer = visibleWaveRadii.y;
        if (outer <= inner)
        {
            if (shockwave != null) shockwave.SetActive(false);
            return;
        }
        if (shockwave == null)
        {
            shockwave = Instantiate(waveShockwavePrefab);
            shockwave.name = name + "_GroundShockwave";
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(shockwave, gameObject.scene);
            shockwaveEffect = shockwave.GetComponent<HyunMuEffects.ReusableGroundShockwave>();
            if (shockwaveEffect != null) shockwaveEffect.IgnoreRoot = transform.root;
        }
        if (shockwaveEffect == null) return;
        shockwave.transform.SetPositionAndRotation(wavePosition, Quaternion.identity);
        shockwave.transform.localScale = Vector3.one;
        if (!shockwave.activeSelf) shockwave.SetActive(true);
        shockwaveEffect.RenderWave(inner, outer, lockedWaveMaxRadius, lockedWaveWidth,
            shockwaveColor, shockwaveRimWidth);
    }

    private void UpdateWaveVisual()
    {
        if (waveShockwavePrefab != null)
        {
            waveVisualObject.SetActive(false);
            return;
        }
        float inner = visibleWaveRadii.x;
        float outer = visibleWaveRadii.y;
        if (outer <= inner)
        {
            waveVisualObject.SetActive(false);
            return;
        }
        Color32 inside = lockedWaveColor;
        Color32 outside = new Color32(inside.r, inside.g, inside.b, 0);
        float normalizedInner = inner / lockedWaveMaxRadius;
        float normalizedOuter = outer / lockedWaveMaxRadius;
        for (int i = 0; i < wavePixels.Length; i++)
        {
            float distance = polygonDistances[i];
            wavePixels[i] = distance >= normalizedInner && distance <= normalizedOuter ? inside : outside;
        }
        waveTexture.SetPixels32(wavePixels);
        waveTexture.Apply(false, false);
        waveVisualObject.SetActive(true);
    }

    private void CheckWaveHit(float inner, float outer)
    {
        float step = Mathf.PI * 2f / Sides;
        Vector3 start = Vector3.forward;
        Vector3 end = new Vector3(Mathf.Sin(step), 0f, Mathf.Cos(step));
        waveVertices[0] = start * inner;
        waveVertices[1] = start * outer;
        waveVertices[2] = end * outer;
        waveVertices[3] = end * inner;
        for (int i = 0; i < 4; i++)
            waveVertices[i + 4] = waveVertices[i] + Vector3.up * lockedWaveHeight;

        waveQueryCollider.sharedMesh = null;
        waveQueryMesh.Clear();
        waveQueryMesh.vertices = waveVertices;
        waveQueryMesh.triangles = inner <= 0f ? WaveCenterTriangles : WavePrismTriangles;
        waveQueryMesh.RecalculateBounds();
        waveQueryCollider.sharedMesh = waveQueryMesh;
        bool hit = false;
        waveQueryCollider.enabled = true;
        try
        {
            Physics.SyncTransforms();
            foreach (Collider targetCollider in playerColliders)
            {
                if (targetCollider == null || !targetCollider.enabled ||
                    !targetCollider.gameObject.activeInHierarchy || Target == null ||
                    (targetCollider.transform != Target && !targetCollider.transform.IsChildOf(Target))) continue;
                for (int i = 0; i < Sides; i++)
                {
                    if (!Physics.ComputePenetration(waveQueryCollider, wavePosition, waveRotations[i],
                        targetCollider, targetCollider.transform.position, targetCollider.transform.rotation,
                        out _, out _)) continue;
                    hit = true;
                    break;
                }
                if (hit) break;
            }
        }
        finally
        {
            // 물리 시뮬레이션과 피격 콜백 전에 끈다. 별도의 OnTrigger 피해를 만들지 않는다.
            if (waveQueryCollider != null) waveQueryCollider.enabled = false;
        }
        if (hit) HitOnce(lockedWaveDamage, checkTargetHeight: false);
    }

    private void DestroyWaveResources()
    {
        if (waveQueryObject != null)
        {
            waveQueryObject.SetActive(false);
            Destroy(waveQueryObject);
        }
        if (waveVisualObject != null)
        {
            waveVisualObject.SetActive(false);
            Destroy(waveVisualObject);
        }
        if (waveQueryMesh != null) Destroy(waveQueryMesh);
        if (waveMaterial != null) Destroy(waveMaterial);
        if (waveTexture != null) Destroy(waveTexture);
        waveQueryObject = null;
        waveQueryCollider = null;
        waveQueryMesh = null;
        waveVisualObject = null;
        waveProjector = null;
        waveMaterial = null;
        waveTexture = null;
        wavePixels = null;
        polygonDistances = null;
    }

    private void OnDestroy()
    {
        EndAttack();
        if (hitboxObject != null) Destroy(hitboxObject);
        if (hitboxMesh != null) Destroy(hitboxMesh);
        DestroyWarning();
        DestroyWaveResources();
        if (shockwave != null) Destroy(shockwave);
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
        bool drawWave = IsRunning ? waveEnabled : enableWave;
        if (showWaveGizmos && drawWave)
        {
            Vector3 waveCenter = IsRunning ? wavePosition :
                new Vector3(transform.position.x, groundY + waveBottomOffset, transform.position.z);
            float maxRadius = IsRunning ? lockedWaveMaxRadius : Mathf.Max(0.1f, waveMaxRadius);
            float waveH = IsRunning ? lockedWaveHeight : Mathf.Max(0.01f, waveHeight);
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.65f);
            DrawWaveOutline(waveCenter, maxRadius, waveH);
            if (IsRunning && waveStarted && visibleWaveRadii.y > visibleWaveRadii.x)
            {
                Gizmos.color = new Color(1f, 0.55f, 0.05f);
                DrawWaveOutline(waveCenter, visibleWaveRadii.y, waveH);
                if (visibleWaveRadii.x > 0f) DrawWaveOutline(waveCenter, visibleWaveRadii.x, waveH);
            }
        }
        Gizmos.color = oldColor;
        Gizmos.matrix = oldMatrix;
    }

    private static void DrawWaveOutline(Vector3 center, float r, float height)
    {
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
    }
}
