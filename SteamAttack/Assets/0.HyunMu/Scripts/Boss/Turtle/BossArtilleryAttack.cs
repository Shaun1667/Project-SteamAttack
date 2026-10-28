using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// 거북이 등의 발사구에서 발사. 각 데칼을 표시한 순간 착탄 위치를 고정한다.
// 피해는 포탄이 떨어질 때만 1회. 비행 중에는 포탄 모델로 충돌 피해를 주지 않는다.
[DisallowMultipleComponent]
public class BossArtilleryAttack : BossAttack
{
    [Header("현무 Animator - Bool 이름은 IsShooting")]
    public Animator animator;
    private bool controlsAnimation;
    private bool waitingForShoot;
    private bool waitingForIdle;
    private float shootEntryWait;
    private static readonly int ShootStateHash = Animator.StringToHash("Base Layer.BHS_ShootBe");
    private static readonly int IdleStateHash = Animator.StringToHash("Base Layer.idle");
    private static readonly int IsShootingHash = Animator.StringToHash("IsShooting");

    [Header("포격 발동 조건")]
    [Tooltip("거북이 BossFSM. 비우면 자신/부모에서 찾습니다.")]
    public BossFSM bodyFSM;
    [Tooltip("Min/Max Use Distance 안에 연속으로 머물러야 하는 시간. 벗어나면 초기화. 0이면 즉시 선택 가능.")]
    [Min(0f)] public float requiredStayTime = 3f;

    [Header("밸런스 - 포탄 수 / 예고 시간 / 간격")]
    [Tooltip("포격 한 번에 떨어지는 포탄과 데칼의 총 개수입니다.")]
    [Min(1)] public int shellCount = 5;
    [Tooltip("각 포탄의 데칼을 표시하고 발사한 뒤, 그 포탄이 착탄하기까지의 시간입니다.")]
    [Min(0.05f)] public float warningToImpactTime = 1.2f;
    [Tooltip("발사 후 다음 발의 준비 애니메이션을 시작하기까지의 최소 간격. 후속 동작과 idle 복귀도 기다립니다. 0이어도 애니메이션마다 한 발씩 발사합니다.")]
    [Min(0f)] public float shellInterval = 0.4f;

    [Header("데칼 크기 / 착탄 판정 - 월드 크기")]
    [Tooltip("착탄 데칼과 실제 피해 판정이 함께 사용하는 반경입니다. 1.2이면 지름 2.4m, 2이면 지름 4m입니다.")]
    [Min(0.05f)] public float explosionRadius = 1.2f;
    [Min(0.01f)] public float explosionHeight = 2f;
    [Tooltip("Ground Y에서 실제 판정 바닥까지의 높이 차이입니다.")]
    public float explosionBottomOffset = 0f;

    [Header("등의 발사구 / 포탄 표시")]
    [Tooltip("등의 빈 오브젝트를 연결하면 순서대로 사용합니다. 비우면 거북이 중심 위에서 발사합니다.")]
    public Transform[] firePoints = new Transform[0];
    [Min(0f)] public float fallbackLaunchHeight = 3f;
    [Tooltip("외형만 있는 프리팹을 연결하세요. 비우면 테스트용 구를 만듭니다. 별도 피해/이동 스크립트는 넣지 마세요.")]
    public GameObject shellVisualPrefab;
    [Tooltip("프리팹 원래 크기에 곱할 값. 테스트 구는 지름입니다.")]
    [Min(0.01f)] public float shellScale = 0.4f;
    [Tooltip("출발점과 착탄점을 잇는 직선보다 궤적 중간을 얼마나 높일지 지정합니다.")]
    [Min(0f)] public float arcHeight = 5f;
    [Tooltip("있으면 착탄 시 재생합니다. 피해는 이 이펙트와 별개로 포탄당 1회입니다.")]
    public ParticleSystem impactEffectPrefab;
    [Tooltip("착탄 후 표시를 유지하는 시간. 이 시간이 지나면 해당 데칼과 이펙트를 정리합니다.")]
    [Min(0f)] public float impactDisplayTime = 0.25f;

    [Header("URP 착탄 예고 데칼")]
    public Material warningMaterial;
    [Tooltip("기존 브레스/파동과 같은 바닥 전용 Rendering Layer를 선택하세요.")]
    public RenderingLayerMask warningRenderingLayers;
    public Color warningColor = new Color(1f, 0.2f, 0.05f, 0.5f);
    public float groundY = 0f;
    [Min(0.1f)] public float projectionDepth = 2f;

    [Header("기즈모")]
    public bool showArtilleryGizmos = true;
    [Tooltip("선택 사항. 연결하면 이 위치에 착탄 반경을 미리 표시합니다.")]
    public Transform previewTarget;

    [Header("플레이 중 상태 - 확인용")]
    [SerializeField] private float accumulatedStayTime;
    [SerializeField] private int launchedShells;
    [SerializeField] private int landedShells;

    private const int Sides = 48;
    private const int TextureSize = 256;
    private Transform trackedPlayer;
    private float elapsed;
    private float nextLaunchTime;
    private int lockedCount;
    private float lockedWarningTime;
    private float lockedInterval;
    private float lockedRadius;
    private float lockedHeight;
    private float lockedBottom;
    private float lockedGroundY;
    private float lockedArcHeight;
    private float lockedShellScale;
    private float lockedFallbackHeight;
    private float lockedDisplayTime;
    private float lockedDamage;
    private float lockedDepth;
    private uint lockedRenderingLayers;
    private Transform[] lockedFirePoints;
    private GameObject lockedVisualPrefab;
    private ParticleSystem lockedImpactPrefab;

    private GameObject runtimeRoot;
    private Material runtimeWarningMaterial;
    private Texture2D warningTexture;
    private Mesh explosionMesh;
    private MeshCollider explosionQuery;
    private readonly List<Shell> shells = new List<Shell>();
    private readonly List<Shell> shellPool = new List<Shell>();
    private GameObject pooledVisual;
    private ParticleSystem pooledImpact;
    private Material pooledWarning;
    private Color pooledColor;
    private Vector2 pooledShape;
    public int PooledShellCount => shellPool.Count;

    private class Shell
    {
        public Vector3 start;
        public Vector3 destination;
        public float launchTime;
        public float impactTime;
        public bool landed;
        public GameObject root;
        public Transform visual;
        public DecalProjector projector;
        public ParticleSystem impact;
        public ParticleSystem[] particles;
        public Vector3 baseScale;
    }

    // 마지막 착탄과 표시 정리가 끝날 때 직접 종료한다.
    // 프레임이 오래 걸려도 새 데칼의 예고 시간을 생략하고 즉시 착탄시키지 않는다.
    protected override float ActiveDuration => float.PositiveInfinity;
    protected override bool ContinueWithoutTarget => true;

    private void Reset()
    {
        minUseDistance = 4f;
        maxUseDistance = 8f;
        readyTime = 0.5f;
        recoveryTime = 1f;
        damage = 2f;
        bodyFSM = GetComponentInParent<BossFSM>();
    }

    private void LateUpdate()
    {
        if (bodyFSM == null) bodyFSM = GetComponentInParent<BossFSM>();
        Transform candidate = bodyFSM != null ? bodyFSM.Player : null;
        if (IsRunning || bodyFSM == null || !bodyFSM.isActiveAndEnabled ||
            bodyFSM.patterns == null || System.Array.IndexOf(bodyFSM.patterns, this) < 0 ||
            candidate == null || !candidate.gameObject.activeInHierarchy)
        {
            ResetStayTimer();
            return;
        }

        Vector3 offset = candidate.position - bodyFSM.transform.position;
        float height = Mathf.Abs(offset.y);
        offset.y = 0f;
        float distance = offset.magnitude;
        if (!CanUse(distance) || distance > bodyFSM.detectRange || height > bodyFSM.heightTolerance)
        {
            ResetStayTimer();
            return;
        }
        if (trackedPlayer != candidate)
        {
            trackedPlayer = candidate;
            accumulatedStayTime = 0f;
        }
        accumulatedStayTime = Mathf.Min(Mathf.Max(0f, requiredStayTime),
            accumulatedStayTime + Time.deltaTime);
    }

    // 근거리의 끝(4m)과 돌진의 시작(8m)을 제외한 중거리만 사용한다.
    // 실제 선택, 체류 시간 누적, 기즈모가 같은 거리 조건을 읽는다.
    public override bool CanUse(float distance)
    {
        float minimum = Mathf.Max(0f, minUseDistance);
        float maximum = Mathf.Max(minimum, maxUseDistance);
        return base.CanUse(distance) && distance > minimum && distance < maximum;
    }

    // 시간 조건은 실제 선택에만 적용한다. 기즈모 조회로 타이머가 변하지 않는다.
    public override bool CanUse(float distance, Transform target)
    {
        if (!base.CanUse(distance, target) || target == null || !target.gameObject.activeInHierarchy)
            return false;
        return requiredStayTime <= 0f ||
            (trackedPlayer == target && accumulatedStayTime >= requiredStayTime);
    }

    private void ResetStayTimer()
    {
        accumulatedStayTime = 0f;
        trackedPlayer = null;
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
                    !candidate.HasState(0, ShootStateHash)) continue;
                animator = candidate;
                break;
            }
        }
        if (animator == null || !animator.isActiveAndEnabled ||
            animator.runtimeAnimatorController == null ||
            !animator.HasState(0, ShootStateHash) || !animator.HasState(0, IdleStateHash))
        {
            Debug.LogWarning("포격에 BHS_ShootBe와 idle 상태가 있는 활성 Animator를 연결해 주세요.", this);
            return false;
        }
        bool hasShooting = false;
        foreach (AnimatorControllerParameter parameter in animator.parameters)
            if (parameter.nameHash == IsShootingHash && parameter.type == AnimatorControllerParameterType.Bool)
                hasShooting = true;
        if (!hasShooting)
        {
            Debug.LogWarning("Animator에 Bool 파라미터 IsShooting을 추가해 주세요.", this);
            return false;
        }
        bool hasCollider = false;
        foreach (Collider collider in Target.GetComponentsInChildren<Collider>(true))
            if (collider.enabled && collider.gameObject.activeInHierarchy) { hasCollider = true; break; }
        if (!hasCollider)
        {
            Debug.LogWarning("BossArtilleryAttack: Player 또는 그 자식에 활성 3D Collider가 필요합니다.", this);
            return false;
        }

        // 한 포격 도중 설정을 바꾸면 다음 포격부터 반영한다.
        lockedCount = Mathf.Max(1, shellCount);
        lockedWarningTime = Mathf.Max(0.05f, warningToImpactTime);
        lockedInterval = Mathf.Max(0f, shellInterval);
        lockedRadius = Mathf.Max(0.05f, explosionRadius);
        lockedHeight = Mathf.Max(0.01f, explosionHeight);
        lockedBottom = explosionBottomOffset;
        lockedGroundY = groundY;
        lockedArcHeight = Mathf.Max(0f, arcHeight);
        lockedShellScale = Mathf.Max(0.01f, shellScale);
        lockedFallbackHeight = Mathf.Max(0f, fallbackLaunchHeight);
        lockedDisplayTime = Mathf.Max(0f, impactDisplayTime);
        lockedDamage = Mathf.Max(0f, damage);
        lockedDepth = Mathf.Max(0.1f, projectionDepth);
        lockedRenderingLayers = warningRenderingLayers;
        lockedFirePoints = firePoints != null ? (Transform[])firePoints.Clone() : new Transform[0];
        lockedVisualPrefab = shellVisualPrefab;
        lockedImpactPrefab = impactEffectPrefab;
        elapsed = 0f;
        nextLaunchTime = 0f;
        launchedShells = 0;
        landedShells = 0;
        ResetStayTimer();

        // 부모 없음 / Scale 1. 보스의 크기나 이후 이동이 데칼과 착탄 좌표를 바꾸지 않는다.
        if (runtimeRoot != null && (pooledVisual != lockedVisualPrefab || pooledImpact != lockedImpactPrefab ||
            pooledWarning != warningMaterial || pooledColor != warningColor || pooledShape != new Vector2(lockedRadius,lockedHeight)))
            DisposePool();
        if (runtimeRoot == null)
        {
            runtimeRoot = new GameObject(name + "_ArtilleryPool");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(runtimeRoot, gameObject.scene);
            if (!CreateDecalResources()) { DisposePool(); return false; }
            CreateExplosionQuery();
            pooledVisual=lockedVisualPrefab; pooledImpact=lockedImpactPrefab;
            pooledWarning=warningMaterial; pooledColor=warningColor; pooledShape=new Vector2(lockedRadius,lockedHeight);
        }
        while (shellPool.Count < lockedCount) shellPool.Add(CreatePooledShell());
        runtimeRoot.SetActive(true);
        waitingForShoot = false;
        waitingForIdle = false;
        return true;
    }

    protected override void StartActive()
    {
        LaunchDueShells();
    }

    protected override void TickActive(float deltaTime)
    {
        elapsed += deltaTime;
        // 먼저 기존 포탄을 진행한다. 피격으로 플레이어가 이동하면 다음 발은 그 새 위치를 조준한다.
        for (int i = 0; i < shells.Count; i++)
        {
            Shell shell = shells[i];
            if (!shell.landed)
            {
                float age = elapsed - shell.launchTime;
                MoveShell(shell, Mathf.Clamp01(age / lockedWarningTime));
                if (age >= lockedWarningTime)
                {
                    Impact(shell);
                    // 피해 콜백에서 보스가 비활성화되거나 공격이 취소될 수 있다.
                    if (!IsRunning) return;
                }
            }
            if (shell.landed && elapsed - shell.impactTime >= lockedDisplayTime && shell.root != null)
            {
                shell.root.SetActive(false);
            }
        }
        LaunchDueShells();
        if (!IsRunning) return;

        if (launchedShells < lockedCount || waitingForIdle) return;
        foreach (Shell shell in shells)
            if (!shell.landed || (shell.root != null && shell.root.activeSelf)) return;
        CancelAttack();
    }

    // 한 번의 준비 동작 완료당 정확히 한 발만 발사한다.
    private void LaunchDueShells()
    {
        if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
        {
            CancelAttack();
            return;
        }
        AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
        if (waitingForIdle)
        {
            if (animator.IsInTransition(0) || state.fullPathHash != IdleStateHash) return;
            waitingForIdle = false;
        }
        if (launchedShells >= lockedCount) return;
        if (Target == null || !Target.gameObject.activeInHierarchy)
        {
            lockedCount = launchedShells;
            waitingForShoot = false;
            StopShootingAnimation();
            return;
        }
        if (!waitingForShoot)
        {
            if (elapsed < nextLaunchTime) return;
            controlsAnimation = true;
            waitingForShoot = true;
            shootEntryWait = 0f;
            animator.SetBool(IsShootingHash, true);
            return;
        }
        if (animator.IsInTransition(0) || state.fullPathHash != ShootStateHash)
        {
            shootEntryWait += Time.deltaTime;
            if (shootEntryWait >= 5f)
            {
                Debug.LogWarning("IsShooting으로 BHS_ShootBe에 진입하지 못했습니다. Animator 전이를 확인하세요.", this);
                CancelAttack();
            }
            return;
        }
        if (state.normalizedTime < 1f) return;

        LaunchShell();
        if (!IsRunning) return;
        launchedShells++;
        waitingForShoot = false;
        waitingForIdle = true;
        nextLaunchTime = elapsed + lockedInterval;
        StopShootingAnimation();
    }

private void LaunchShell()
    {
        Vector3 destination=Target.position; destination.y=lockedGroundY;
        Vector3 start=Owner.position+Vector3.up*lockedFallbackHeight;
        if(lockedFirePoints.Length>0) {var point=lockedFirePoints[launchedShells%lockedFirePoints.Length];if(point!=null)start=point.position;}
        Shell shell=shellPool[launchedShells];
        shell.start=start; shell.destination=destination; shell.launchTime=elapsed; shell.impactTime=0; shell.landed=false;
        foreach(var ps in shell.particles) if(ps!=null) ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
        if(shell.impact!=null) shell.impact.gameObject.SetActive(false);
        shell.projector.transform.SetPositionAndRotation(destination,Quaternion.Euler(90,0,0));
        ConfigureProjector(shell.projector);
        shell.visual.localScale=shell.baseScale*lockedShellScale;
        MoveShell(shell,0);
        shell.visual.gameObject.SetActive(true); shell.root.SetActive(true);
        foreach(var ps in shell.visual.GetComponentsInChildren<ParticleSystem>(true)) ps.Play(false);
        shells.Add(shell);
    }

    private void MoveShell(Shell shell, float t)
    {
        if (shell.visual == null) return;
        Vector3 position = Vector3.Lerp(shell.start, shell.destination, t);
        position.y += 4f * lockedArcHeight * t * (1f - t);
        Vector3 tangent = shell.destination - shell.start +
            Vector3.up * (4f * lockedArcHeight * (1f - 2f * t));
        Vector3 up = tangent.sqrMagnitude > 0.0001f &&
            Mathf.Abs(Vector3.Dot(tangent.normalized, Vector3.up)) > 0.999f
            ? Vector3.forward : Vector3.up;
        Quaternion rotation = tangent.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(tangent, up) : Quaternion.identity;
        shell.visual.SetPositionAndRotation(position, rotation);
    }

    private void Impact(Shell shell)
    {
        shell.landed = true;
        shell.impactTime = elapsed;
        landedShells++;
        if (shell.visual != null) shell.visual.gameObject.SetActive(false);
        // 남아 있는 데칼은 착탄 잔상이다. 지속 피해는 없다.
        if (shell.projector != null) shell.projector.fadeFactor = 0.3f;
        if (shell.impact != null)
        {
            shell.impact.transform.SetPositionAndRotation(shell.destination,Quaternion.identity);
            shell.impact.gameObject.SetActive(true); shell.impact.Play(true);
        }

        // 각 포탄은 순간 판정 한 번으로 완결된다. 동시 착탄도 포탄별 최대 1회다.
        BeginNextHitPhase();
        if (CanHitCollider && OverlapsPlayer(shell.destination))
            HitOnce(lockedDamage, checkTargetHeight: false);
    }

    private bool OverlapsPlayer(Vector3 destination)
    {
        if (explosionQuery == null || Target == null) return false;
        Vector3 position = destination + Vector3.up * lockedBottom;
        explosionQuery.transform.SetPositionAndRotation(position, Quaternion.identity);
        explosionQuery.enabled = true;
        bool overlaps = false;
        try
        {
            Physics.SyncTransforms();
            // 테스트용 순간이동/콜라이더 재활성화 이후의 실제 위치를 검사한다.
            foreach (Collider targetCollider in Target.GetComponentsInChildren<Collider>(true))
            {
                if (targetCollider == null || !targetCollider.enabled ||
                    !targetCollider.gameObject.activeInHierarchy) continue;
                if (!Physics.ComputePenetration(explosionQuery, position, Quaternion.identity,
                    targetCollider, targetCollider.transform.position, targetCollider.transform.rotation,
                    out _, out _)) continue;
                overlaps = true;
                break;
            }
        }
        finally
        {
            // 물리 벽/상시 트리거가 되지 않도록 피해 콜백 전에 끈다.
            if (explosionQuery != null) explosionQuery.enabled = false;
        }
        return overlaps;
    }

    private void CreateExplosionQuery()
    {
        GameObject query = new GameObject("ExplosionQuery");
        query.transform.SetParent(runtimeRoot.transform, false);
        explosionQuery = query.AddComponent<MeshCollider>();
        explosionQuery.enabled = false;
        explosionQuery.convex = true;
        explosionQuery.isTrigger = true;
        Vector3[] vertices = new Vector3[Sides * 2];
        var triangles = new List<int>();
        for (int i = 0; i < Sides; i++)
        {
            float angle = i * Mathf.PI * 2f / Sides;
            vertices[i] = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle)) * lockedRadius;
            vertices[i + Sides] = vertices[i] + Vector3.up * lockedHeight;
            int next = (i + 1) % Sides;
            triangles.AddRange(new int[] { i, next, i + Sides, next, next + Sides, i + Sides });
        }
        for (int i = 1; i < Sides - 1; i++)
            triangles.AddRange(new int[] { 0, i + 1, i, Sides, Sides + i, Sides + i + 1 });
        explosionMesh = new Mesh { name = "ArtilleryExplosionCylinder" };
        explosionMesh.vertices = vertices;
        explosionMesh.SetTriangles(triangles, 0);
        explosionMesh.RecalculateBounds();
        explosionQuery.sharedMesh = explosionMesh;
    }

    private bool CreateDecalResources()
    {
        string map = warningMaterial != null ? FindBaseMap(warningMaterial) : null;
        if (string.IsNullOrEmpty(map))
        {
            Debug.LogWarning("BossArtilleryAttack: Warning Material에 기존 브레스/파동의 URP Decal 머티리얼을 연결하세요.", this);
            return false;
        }
        runtimeWarningMaterial = new Material(warningMaterial);
        warningTexture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false);
        warningTexture.name = "ArtilleryWarningTexture";
        warningTexture.wrapMode = TextureWrapMode.Clamp;
        warningTexture.filterMode = FilterMode.Bilinear;
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
                float offset = Mathf.Repeat(Mathf.Atan2(p.x, p.y), step) - step * 0.5f;
                float edge = apothem / Mathf.Cos(offset);
                pixels[y * TextureSize + x] = p.sqrMagnitude <= edge * edge ? inside : outside;
            }
        }
        warningTexture.SetPixels32(pixels);
        warningTexture.Apply(false, false);
        runtimeWarningMaterial.SetTexture(map, warningTexture);
        runtimeWarningMaterial.SetTextureScale(map, Vector2.one);
        runtimeWarningMaterial.SetTextureOffset(map, Vector2.zero);
        if (runtimeWarningMaterial.HasProperty("_BaseColor"))
            runtimeWarningMaterial.SetColor("_BaseColor", Color.white);

        GameObject check = new GameObject("DecalMaterialCheck");
        check.transform.SetParent(runtimeRoot.transform, false);
        check.SetActive(false);
        DecalProjector projector = check.AddComponent<DecalProjector>();
        ConfigureProjector(projector);
        bool valid = projector.IsValid();
        Destroy(check);
        if (!valid) Debug.LogWarning("BossArtilleryAttack: URP Decal Projector용 머티리얼이 아닙니다.", this);
        return valid;
    }

    private void ConfigureProjector(DecalProjector projector)
    {
        projector.material = runtimeWarningMaterial;
        projector.pivot = Vector3.zero;
        projector.uvScale = Vector2.one;
        projector.uvBias = Vector2.zero;
        projector.fadeFactor = 1f;
        projector.drawDistance = 100f;
        projector.renderingLayerMask = lockedRenderingLayers;
        projector.size = new Vector3(lockedRadius * 2f, lockedRadius * 2f, lockedDepth);
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

    private void StopShootingAnimation()
    {
        if (!controlsAnimation) return;
        controlsAnimation = false;
        if (animator != null) animator.SetBool(IsShootingHash, false);
    }

protected override void EndAttack()
    {
        StopShootingAnimation();waitingForShoot=false;waitingForIdle=false;ResetStayTimer();
        foreach(var shell in shellPool)
        {
            if(shell.root==null)continue;
            foreach(var ps in shell.particles)if(ps!=null)ps.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);
            shell.root.SetActive(false);
        }
        if(explosionQuery!=null)explosionQuery.enabled=false;
        if(runtimeRoot!=null)runtimeRoot.SetActive(false);
        shells.Clear();
    }

private void OnDestroy() { EndAttack(); DisposePool(); }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (!showArtilleryGizmos) return;
        Matrix4x4 previousMatrix = UnityEditor.Handles.matrix;
        Color previousColor = UnityEditor.Handles.color;
        try
        {
            UnityEditor.Handles.matrix = Matrix4x4.identity;
            if (IsRunning)
            {
                foreach (Shell shell in shells)
                {
                    if (shell.root == null) continue;
                    UnityEditor.Handles.color = shell.landed ? Color.red : Color.yellow;
                    DrawExplosionGizmo(shell.destination, lockedRadius, lockedHeight, lockedBottom);
                    UnityEditor.Handles.Label(shell.destination + Vector3.up * 0.1f,
                        shell.landed ? "착탄 · 판정 완료" : $"착탄까지 {Mathf.Max(0f, lockedWarningTime - (elapsed - shell.launchTime)):0.00}초");
                }
            }
            else
            {
                Vector3 center = previewTarget != null ? previewTarget.position : transform.position + transform.forward * 4f;
                center.y = groundY;
                UnityEditor.Handles.color = Color.yellow;
                DrawExplosionGizmo(center, Mathf.Max(0.05f, explosionRadius),
                    Mathf.Max(0.01f, explosionHeight), explosionBottomOffset);
                UnityEditor.Handles.Label(center, "포탄 1발의 착탄 범위 · 미리보기");
            }
        }
        finally
        {
            UnityEditor.Handles.matrix = previousMatrix;
            UnityEditor.Handles.color = previousColor;
        }
    }

    private static void DrawExplosionGizmo(Vector3 floor, float radius, float height, float bottom)
    {
        Vector3 lower = floor + Vector3.up * bottom;
        Vector3 upper = lower + Vector3.up * height;
        UnityEditor.Handles.DrawWireDisc(lower, Vector3.up, radius);
        UnityEditor.Handles.DrawWireDisc(upper, Vector3.up, radius);
        for (int i = 0; i < 4; i++)
        {
            Vector3 edge = Quaternion.Euler(0f, i * 90f, 0f) * Vector3.forward * radius;
            UnityEditor.Handles.DrawLine(lower + edge, upper + edge);
        }
    }
#endif


private void DisposePool()
    {
        if(runtimeRoot!=null)Destroy(runtimeRoot);
        if(explosionMesh!=null)Destroy(explosionMesh);
        if(runtimeWarningMaterial!=null)Destroy(runtimeWarningMaterial);
        if(warningTexture!=null)Destroy(warningTexture);
        runtimeRoot=null;explosionQuery=null;explosionMesh=null;runtimeWarningMaterial=null;warningTexture=null;
        shellPool.Clear();shells.Clear();
    }


private Shell CreatePooledShell()
    {
        var shell=new Shell();
        shell.root=new GameObject("PooledShell_"+shellPool.Count);
        shell.root.transform.SetParent(runtimeRoot.transform,false);shell.root.SetActive(false);
        var warning=new GameObject("LandingWarning");warning.transform.SetParent(shell.root.transform,false);
        shell.projector=warning.AddComponent<DecalProjector>();ConfigureProjector(shell.projector);
        var visual=lockedVisualPrefab!=null ? Instantiate(lockedVisualPrefab,shell.root.transform) : GameObject.CreatePrimitive(PrimitiveType.Sphere);
        visual.transform.SetParent(shell.root.transform,false);visual.name="ShellVisual";
        shell.visual=visual.transform;shell.baseScale=visual.transform.localScale;
        foreach(var c in visual.GetComponentsInChildren<Collider>(true))c.enabled=false;
        foreach(var rb in visual.GetComponentsInChildren<Rigidbody>(true)){rb.isKinematic=true;rb.detectCollisions=false;}
        foreach(var ps in visual.GetComponentsInChildren<ParticleSystem>(true)){var main=ps.main;main.scalingMode=ParticleSystemScalingMode.Hierarchy;main.stopAction=ParticleSystemStopAction.None;}
        if(lockedImpactPrefab!=null){shell.impact=Instantiate(lockedImpactPrefab,shell.root.transform);shell.impact.gameObject.SetActive(false);foreach(var ps in shell.impact.GetComponentsInChildren<ParticleSystem>(true)){var main=ps.main;main.stopAction=ParticleSystemStopAction.None;}}
        shell.particles=shell.root.GetComponentsInChildren<ParticleSystem>(true);
        return shell;
    }
}
