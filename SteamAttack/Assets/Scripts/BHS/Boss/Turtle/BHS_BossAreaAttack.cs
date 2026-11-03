using UnityEngine;
using UnityEngine.Rendering.Universal;

public class BossAreaAttack : BossAttack
{
    [Header("현무 Animator - Bool 이름은 IsStamping")]
    public Animator animator;

    [Header("점프슬램 착지음")]
    public AudioSource slamImpactAudio;
    [Range(0f, 1f)] public float slamImpactVolume = .9f;
    [Tooltip("착지 판정보다 소리를 몇 초 먼저 재생할지 지정합니다.")]
    [Min(0f)] public float slamImpactLeadTime = .2f;
    private bool slamImpactPlayed, slamImpactLanded;
    private bool slamImpactAudioPaused;
    private Vector3 slamImpactAudioPosition;

    private bool controlsAnimation;
    private float animationWaitTime;
    private static readonly int StampStateHash = Animator.StringToHash("Base Layer.BHS_StampBe");
    private static readonly int StandStateHash = Animator.StringToHash("Base Layer.BHS_StampAf");
    private bool standAnimationComplete;
    private BossFSM controllingFSM;
    public override bool IsActionComplete => !IsRunning || standAnimationComplete;
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
    [Tooltip("미리 준비할 파동 수입니다. 동시에 더 필요하면 늘리고, 종료된 파동은 재사용합니다.")]
    [Min(1)] public int prewarmWaveCount = 2;

    // A slot owns one launched wave; no attack/FSM state is shared between waves.
    private sealed class WaveInstance
    {
        public bool active, hasHit;
        public GameObject sourcePrefab, root;
        public HyunMuEffects.ReusableGroundShockwave effect;
        public Material sourceMaterial, material;
        public Texture2D texture;
        public DecalProjector decal;
        public Transform target;
        public Collider[] colliders;
        public System.Action<float> reportHit;
        public Vector3 position;
        public Vector2 radii;
        public float launchedAt, elapsed, delay, duration, maxRadius, width, speed, height, damage, rim;
        public Color color, shockColor;
    }

    private readonly System.Collections.Generic.List<WaveInstance> wavePool =
        new System.Collections.Generic.List<WaveInstance>();
    private GameObject wavePoolRoot;
    private WaveInstance currentWave;
    public int WavePoolCount => wavePool.Count;
    public int ActiveWaveCount
    {
        get { int count = 0; foreach (WaveInstance wave in wavePool) if (wave.active) count++; return count; }
    }

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

    // The FSM releases this action when StampAf finishes. Pooled waves run separately.
    protected override float ActiveDuration => float.PositiveInfinity;
    protected override bool ContinueWithoutTarget => true;
    private float elapsed;
    private float stompDuration;
    private float waveStartTime;
    private float waveDuration;
    private float lockedWaveMaxRadius;
    private float lockedWaveWidth;
    private float lockedWaveSpeed;
    private float lockedWaveHeight;
    private float lockedWaveDamage;
    private Color lockedWaveColor;
    private bool waveEnabled;
    private Vector3 wavePosition;

    private GameObject waveQueryObject;
    private MeshCollider waveQueryCollider;
    private Mesh waveQueryMesh;
    private readonly Vector3[] waveVertices = new Vector3[8];
    private readonly Quaternion[] waveRotations = new Quaternion[Sides];
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
        slamImpactPlayed = slamImpactLanded = false;
        standAnimationComplete = false;
        controllingFSM = Owner != null ? Owner.GetComponent<BossFSM>() : null;

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
        elapsed = 0f;
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
        {
            float remaining = Mathf.Max(0f, 1f - state.normalizedTime) * state.length /
                Mathf.Max(.001f, Mathf.Abs(animator.speed));
            if (Time.timeScale > 0f && animator.speed != 0f &&
                remaining <= Mathf.Max(0f, slamImpactLeadTime))
                PlaySlamImpactAudio();
            return state.normalizedTime < 1f;
        }

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
        slamImpactLanded = true;
        if (waveEnabled) StartPooledWave();
        PlaySlamImpactAudio();
    }

    protected override void TickActive(float deltaTime)
    {
        float previousTime = elapsed;
        elapsed += deltaTime;
        if (previousTime < stompDuration)
        {
            TickStomp();
            if (!IsRunning) return;
        }
        if (elapsed >= stompDuration)
        {
            StopStampAnimation();
            if (hitboxObject != null) hitboxObject.SetActive(false);
            CheckStandAnimationComplete();
        }
    }

    private void CheckStandAnimationComplete()
    {
        if (standAnimationComplete) return;
        if (animator == null || !animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null)
        {
            CancelAttack();
            return;
        }

        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (animator.IsInTransition(0)) return;
        if (state.fullPathHash == StandStateHash)
            standAnimationComplete = state.normalizedTime >= 1f;
        else if (state.fullPathHash != StampStateHash)
            // A zero-duration exit transition can skip the final Af sample.
            standAnimationComplete = true;
    }

    public override void ReleaseControl()
    {
        // Normal completion detaches this cast. IsRunning becomes false immediately,
        // so the same area attack can be selected while older waves are still alive.
        if (standAnimationComplete) currentWave = null;
        CancelAttack();
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
        if (currentWave != null) ReturnWave(currentWave);
        currentWave = null;
        // If the wind-up was cancelled after the early cue, stop that cue too.
        if (!slamImpactLanded && slamImpactPlayed && slamImpactAudio != null)
        {
            slamImpactAudio.Stop();
            slamImpactAudioPaused = false;
        }
        StopStampAnimation();
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

    private bool CreateWaveResources()
    {
        if (waveShockwavePrefab != null)
        {
            if (waveShockwavePrefab.GetComponent<HyunMuEffects.ReusableGroundShockwave>() == null)
            {
                Debug.LogError("파동 프리팹에 ReusableGroundShockwave가 필요합니다.", this);
                return false;
            }
        }
        else if (warningMaterial == null || string.IsNullOrEmpty(FindBaseMap(warningMaterial)))
        {
            Debug.LogError("파동 프리팹 또는 유효한 Decal 머티리얼이 필요합니다.", this);
            return false;
        }

        if (wavePoolRoot == null)
        {
            wavePoolRoot = new GameObject(name + "_WavePool");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(wavePoolRoot, gameObject.scene);
        }
        if (waveQueryCollider == null)
        {
            waveQueryObject = new GameObject(name + "_WaveQuery");
            waveQueryObject.transform.SetParent(wavePoolRoot.transform, false);
            waveQueryCollider = waveQueryObject.AddComponent<MeshCollider>();
            waveQueryCollider.enabled = false;
            waveQueryCollider.convex = true;
            waveQueryCollider.isTrigger = true;
            waveQueryMesh = new Mesh { name = "WavePrism" };
            for (int i = 0; i < Sides; i++)
                waveRotations[i] = Quaternion.Euler(0f, i * 360f / Sides, 0f);
        }
        if (waveShockwavePrefab == null && wavePixels == null)
        {
            wavePixels = new Color32[TextureSize * TextureSize];
            polygonDistances = new float[wavePixels.Length];
            float step = Mathf.PI * 2f / Sides;
            float apothem = Mathf.Cos(step * .5f);
            for (int y = 0; y < TextureSize; y++)
                for (int x = 0; x < TextureSize; x++)
                {
                    Vector2 p = new Vector2((x + .5f) / TextureSize * 2f - 1f,
                        (y + .5f) / TextureSize * 2f - 1f);
                    float offset = Mathf.Repeat(Mathf.Atan2(p.x, p.y), step) - step * .5f;
                    polygonDistances[y * TextureSize + x] = p.magnitude / (apothem / Mathf.Cos(offset));
                }
        }
        int matching = 0;
        foreach (WaveInstance wave in wavePool) if (MatchesWaveSource(wave)) matching++;
        while (matching < Mathf.Max(1, prewarmWaveCount))
        {
            if (CreateWaveSlot() == null) return false;
            matching++;
        }
        return true;
    }

    private bool MatchesWaveSource(WaveInstance wave)
    {
        return wave.root != null && wave.sourcePrefab == waveShockwavePrefab &&
            (waveShockwavePrefab != null || wave.sourceMaterial == warningMaterial);
    }

    private WaveInstance CreateWaveSlot()
    {
        WaveInstance wave = new WaveInstance();
        wave.sourcePrefab = waveShockwavePrefab;
        wave.sourceMaterial = warningMaterial;
        if (waveShockwavePrefab != null)
        {
            wave.root = Instantiate(waveShockwavePrefab, wavePoolRoot.transform);
            wave.root.SetActive(false);
            wave.root.name = name + "_GroundShockwave_" + wavePool.Count;
            wave.effect = wave.root.GetComponent<HyunMuEffects.ReusableGroundShockwave>();
            wave.effect.IgnoreRoot = transform.root;
        }
        else
        {
            wave.root = new GameObject(name + "_WaveDecal_" + wavePool.Count);
            wave.root.transform.SetParent(wavePoolRoot.transform, false);
            wave.root.SetActive(false);
            wave.material = new Material(warningMaterial);
            wave.texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
            wave.texture.name = "PooledWaveRingTexture";
            wave.texture.wrapMode = TextureWrapMode.Clamp;
            wave.texture.filterMode = FilterMode.Bilinear;
            string map = FindBaseMap(warningMaterial);
            wave.material.SetTexture(map, wave.texture);
            wave.material.SetTextureScale(map, Vector2.one);
            wave.material.SetTextureOffset(map, Vector2.zero);
            if (wave.material.HasProperty("_BaseColor"))
                wave.material.SetColor("_BaseColor", Color.white);
            wave.decal = wave.root.AddComponent<DecalProjector>();
            wave.decal.material = wave.material;
            wave.decal.pivot = Vector3.zero;
            wave.decal.uvScale = Vector2.one;
            wave.decal.uvBias = Vector2.zero;
            wave.decal.fadeFactor = 1f;
            wave.decal.drawDistance = 100f;
            if (!wave.decal.IsValid())
            {
                Debug.LogError("파동에 사용할 URP Decal 머티리얼을 확인하세요.", this);
                Destroy(wave.root);
                Destroy(wave.material);
                Destroy(wave.texture);
                return null;
            }
        }
        wavePool.Add(wave);
        return wave;
    }

    private void StartPooledWave()
    {
        WaveInstance wave = null;
        foreach (WaveInstance candidate in wavePool)
            if (!candidate.active && MatchesWaveSource(candidate)) { wave = candidate; break; }
        if (wave == null) wave = CreateWaveSlot();
        if (wave == null) return;

        currentWave = wave;
        wave.active = true;
        wave.hasHit = false;
        wave.target = Target;
        wave.colliders = playerColliders;
        wave.reportHit = DamageReporter;
        wave.position = wavePosition;
        wave.radii = Vector2.zero;
        wave.launchedAt = Time.time;
        wave.elapsed = 0f;
        wave.delay = waveStartTime;
        wave.duration = waveDuration;
        wave.maxRadius = lockedWaveMaxRadius;
        wave.width = lockedWaveWidth;
        wave.speed = lockedWaveSpeed;
        wave.height = lockedWaveHeight;
        wave.damage = lockedWaveDamage;
        wave.color = lockedWaveColor;
        wave.shockColor = shockwaveColor;
        wave.rim = shockwaveRimWidth;
        wave.root.transform.localScale = Vector3.one;
        wave.root.transform.SetPositionAndRotation(wave.position,
            wave.effect != null ? Quaternion.identity : Quaternion.Euler(90f, 0f, 0f));
        if (wave.decal != null)
        {
            wave.decal.size = new Vector3(wave.maxRadius * 2f, wave.maxRadius * 2f,
                Mathf.Max(.1f, projectionDepth));
            wave.decal.renderingLayerMask = warningRenderingLayers;
        }
    }

    private void TickPooledWaves()
    {
        if (controllingFSM != null && !controllingFSM.isActiveAndEnabled)
        {
            StopAllWaves();
            return;
        }
        for (int i = 0; i < wavePool.Count; i++)
        {
            WaveInstance wave = wavePool[i];
            if (!wave.active) continue;
            float previousTime = wave.elapsed;
            wave.elapsed = Mathf.Max(0f, Time.time - wave.launchedAt);
            if (wave.elapsed < wave.delay) continue;
            float previousAge = Mathf.Clamp(previousTime - wave.delay, 0f, wave.duration);
            float age = Mathf.Clamp(wave.elapsed - wave.delay, 0f, wave.duration);
            float front = age * wave.speed;
            wave.radii = new Vector2(Mathf.Max(0f, front - wave.width), Mathf.Min(front, wave.maxRadius));
            UpdateWaveVisual(wave);

            float sweptInner = Mathf.Max(0f, previousAge * wave.speed - wave.width);
            float sweptOuter = Mathf.Min(front, wave.maxRadius);
            if (!wave.hasHit && wave.target != null && wave.target.gameObject.activeInHierarchy &&
                sweptOuter > sweptInner && sweptOuter >= .01f)
                CheckWaveHit(wave, sweptInner, sweptOuter);
            // A hit callback may disable/destroy the boss and return the pool.
            if (wave.active && wave.elapsed >= wave.delay + wave.duration) ReturnWave(wave);
        }
    }

    private void UpdateWaveVisual(WaveInstance wave)
    {
        float inner = wave.radii.x, outer = wave.radii.y;
        if (wave.root == null) return;
        if (outer <= inner)
        {
            if (wave.root.activeSelf) wave.root.SetActive(false);
            return;
        }
        if (!wave.root.activeSelf) wave.root.SetActive(true);
        if (wave.effect != null)
        {
            wave.effect.RenderWave(inner, outer, wave.maxRadius, wave.width, wave.shockColor, wave.rim);
            return;
        }
        Color32 inside = wave.color;
        Color32 outside = new Color32(inside.r, inside.g, inside.b, 0);
        float normalizedInner = inner / wave.maxRadius;
        float normalizedOuter = outer / wave.maxRadius;
        for (int i = 0; i < wavePixels.Length; i++)
        {
            float distance = polygonDistances[i];
            wavePixels[i] = distance >= normalizedInner && distance <= normalizedOuter ? inside : outside;
        }
        wave.texture.SetPixels32(wavePixels);
        wave.texture.Apply(false, false);
    }

    private void CheckWaveHit(WaveInstance wave, float inner, float outer)
    {
        float step = Mathf.PI * 2f / Sides;
        Vector3 start = Vector3.forward;
        Vector3 end = new Vector3(Mathf.Sin(step), 0f, Mathf.Cos(step));
        waveVertices[0] = start * inner;
        waveVertices[1] = start * outer;
        waveVertices[2] = end * outer;
        waveVertices[3] = end * inner;
        for (int i = 0; i < 4; i++)
            waveVertices[i + 4] = waveVertices[i] + Vector3.up * wave.height;

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
            foreach (Collider targetCollider in wave.colliders)
            {
                if (targetCollider == null || !targetCollider.enabled ||
                    !targetCollider.gameObject.activeInHierarchy || wave.target == null ||
                    (targetCollider.transform != wave.target && !targetCollider.transform.IsChildOf(wave.target))) continue;
                for (int i = 0; i < Sides; i++)
                {
                    if (!Physics.ComputePenetration(
                        waveQueryCollider, wave.position, waveRotations[i],
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
            if (waveQueryCollider != null) waveQueryCollider.enabled = false;
        }
        if (hit && wave.active && !wave.hasHit)
        {
            wave.hasHit = true;
            wave.reportHit?.Invoke(Mathf.Max(0f, wave.damage));
        }
    }

    private void ReturnWave(WaveInstance wave)
    {
        wave.active = false;
        if (wave.root != null) wave.root.SetActive(false);
        wave.target = null;
        wave.colliders = null;
        wave.reportHit = null;
        wave.radii = Vector2.zero;
        if (currentWave == wave) currentWave = null;
    }

    private void StopAllWaves()
    {
        foreach (WaveInstance wave in wavePool) ReturnWave(wave);
        if (waveQueryCollider != null) waveQueryCollider.enabled = false;
    }

    private void DestroyWaveResources()
    {
        StopAllWaves();
        foreach (WaveInstance wave in wavePool)
        {
            if (wave.material != null) Destroy(wave.material);
            if (wave.texture != null) Destroy(wave.texture);
        }
        wavePool.Clear();
        if (wavePoolRoot != null) Destroy(wavePoolRoot);
        if (waveQueryMesh != null) Destroy(waveQueryMesh);
        wavePoolRoot = null;
        waveQueryObject = null;
        waveQueryCollider = null;
        waveQueryMesh = null;
        wavePixels = null;
        polygonDistances = null;
    }

    private void PlaySlamImpactAudio()
    {
        if (slamImpactPlayed || slamImpactAudio == null || slamImpactAudio.clip == null) return;
        slamImpactPlayed = true;
        slamImpactAudioPosition = new Vector3(Origin.x, groundY + .15f, Origin.z);
        slamImpactAudio.transform.position = slamImpactAudioPosition;
        slamImpactAudio.Stop();
        slamImpactAudioPaused = false;
        slamImpactAudio.volume = slamImpactVolume;
        slamImpactAudio.Play();
    }

    // Audio follows the existing impact timing; it never drives the attack or Animator.
    private void LateUpdate()
    {
        TickPooledWaves();
        if (slamImpactAudio == null) return;
        if (slamImpactAudio.isPlaying || slamImpactAudioPaused)
            slamImpactAudio.transform.position = slamImpactAudioPosition;
        if (Time.timeScale <= 0f)
        {
            if (slamImpactAudio.isPlaying)
            {
                slamImpactAudio.Pause();
                slamImpactAudioPaused = true;
            }
        }
        else if (slamImpactAudioPaused)
        {
            slamImpactAudio.UnPause();
            slamImpactAudioPaused = false;
        }
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        StopAllWaves();
        if (slamImpactAudio != null) slamImpactAudio.Stop();
        slamImpactAudioPaused = false;
    }

    private void OnDestroy()
    {
        EndAttack();
        if (hitboxObject != null) Destroy(hitboxObject);
        if (hitboxMesh != null) Destroy(hitboxMesh);
        DestroyWarning();
        DestroyWaveResources();
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
        if (showWaveGizmos)
        {
            if (enableWave && !Application.isPlaying)
            {
                Gizmos.color = new Color(.2f, .8f, 1f, .65f);
                DrawWaveOutline(new Vector3(transform.position.x, groundY + waveBottomOffset, transform.position.z),
                    Mathf.Max(.1f, waveMaxRadius), Mathf.Max(.01f, waveHeight));
            }
            foreach (WaveInstance wave in wavePool)
            {
                if (!wave.active) continue;
                Gizmos.color = new Color(.2f, .8f, 1f, .65f);
                DrawWaveOutline(wave.position, wave.maxRadius, wave.height);
                if (wave.radii.y <= wave.radii.x) continue;
                Gizmos.color = new Color(1f, .55f, .05f);
                DrawWaveOutline(wave.position, wave.radii.y, wave.height);
                if (wave.radii.x > 0f) DrawWaveOutline(wave.position, wave.radii.x, wave.height);
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
