using System;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class BossCoreHealth : MonoBehaviour
{
    [SerializeField, Min(1f)] private float maxHp = 100f;
    [Tooltip("이 코어의 피격용 콜라이더만 지정하세요. 몸체 충돌용은 제외합니다.")]
    [SerializeField] private Collider[] hitColliders;
    [Header("Mid ~ Tip 자동 캡슐 (선택)")]
    [SerializeField] private Transform mid;
    [SerializeField] private Transform tip;
    [Tooltip("월드 단위 반지름. Mid와 Tip은 양 끝 반구의 중심입니다.")]
    [SerializeField, Min(0.001f)] private float capsuleRadius = 0.2f;
    [SerializeField] private bool capsuleIsTrigger;
    [SerializeField, HideInInspector] private CapsuleCollider generatedCapsule;
    [SerializeField] private GameObject objectA;
    [SerializeField] private GameObject objectB;
    [SerializeField] private UnityEvent onDestroyed = new UnityEvent();

    public float MaxHp => maxHp;
    public float CurrentHp { get; private set; }
    public bool IsAlive => CurrentHp > 0f && !destroyed && isActiveAndEnabled;
    public bool CanTakeDamage => IsAlive && damageEnabled;
    public event Action<BossCoreHealth> Destroyed;

    private bool damageEnabled;
    private bool destroyed;
    private bool initialized;

    private void Awake() => Initialize();

    private void Initialize()
    {
        if (initialized) return;
        initialized = true;
        CurrentHp = maxHp;
        if (objectA != null) objectA.SetActive(true);
        if (objectB != null) objectB.SetActive(false);
        CreateOrUpdateCapsule();
        // 보스 관리자가 전투 시작 시 서브코어만 열어 줍니다.
        SetDamageEnabled(false);
    }

    public void SetDamageEnabled(bool enabled)
    {
        damageEnabled = enabled && !destroyed;
        if (generatedCapsule != null) generatedCapsule.enabled = damageEnabled;
        if (hitColliders == null) return;
        foreach (Collider hitCollider in hitColliders)
            if (hitCollider != null) hitCollider.enabled = damageEnabled;
    }

    [ContextMenu("Mid ~ Tip 캡슐 생성 / 갱신")]
    private void CreateOrUpdateCapsule()
    {
        if (mid == null || tip == null) return;
        if (generatedCapsule == null)
        {
            var capsuleObject = new GameObject("CoreHitCapsule");
            capsuleObject.SetActive(false);
            capsuleObject.transform.SetParent(transform, false);
            capsuleObject.layer = gameObject.layer;
            generatedCapsule = capsuleObject.AddComponent<CapsuleCollider>();
        }

        // 공격 쪽에서 GetComponentInParent<BossCoreHealth>()로 찾을 수 있습니다.
        try
        {
            generatedCapsule.gameObject.tag = "Boss";
        }
        catch (UnityException)
        {
            Debug.LogError("Tags and Layers에 Boss 태그를 추가하세요.", this);
        }
        generatedCapsule.direction = 1; // 로컬 Y축
        generatedCapsule.center = Vector3.zero;
        generatedCapsule.isTrigger = capsuleIsTrigger;
        UpdateCapsulePose();
        generatedCapsule.enabled = Application.isPlaying && damageEnabled && !destroyed;
        generatedCapsule.gameObject.SetActive(true);
    }

    private void FixedUpdate() => UpdateCapsulePose();
    private void LateUpdate() => UpdateCapsulePose();

    private void UpdateCapsulePose()
    {
        if (generatedCapsule == null || mid == null || tip == null) return;
        Transform capsuleTransform = generatedCapsule.transform;
        Vector3 delta = tip.position - mid.position;
        capsuleTransform.position = (mid.position + tip.position) * 0.5f;
        capsuleTransform.rotation = delta.sqrMagnitude > 0.000001f
            ? Quaternion.FromToRotation(Vector3.up, delta.normalized)
            : Quaternion.identity;

        // 부모의 균일 스케일을 보정해 Inspector 반지름을 월드 단위로 유지합니다.
        capsuleTransform.localScale = Vector3.one;
        Vector3 scale = capsuleTransform.lossyScale;
        capsuleTransform.localScale = new Vector3(
            1f / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            1f / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            1f / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));
        float radius = Mathf.Max(0.001f, capsuleRadius);
        generatedCapsule.radius = radius;
        generatedCapsule.height = delta.magnitude + radius * 2f;
    }

    public void Prepare(bool vulnerable)
    {
        Initialize();
        SetDamageEnabled(vulnerable);
    }

    // 기존 EnemyHealth와 같은 호출 형태. 공격 쪽에서 이 컴포넌트를 찾아 호출합니다.
    public void TakeDamage(float amount, Vector3 hitFrom)
    {
        if (!CanTakeDamage || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
        NGH.ReviewedAudio.Play("Initial/08_Boss/BossCoreHit_v01", 0.7f);
        NGH.ReviewedAudio.Play("Revision/49_BossHurt_v01", 0.7f);
        CurrentHp = Mathf.Max(0f, CurrentHp - amount);
        if (CurrentHp > 0f) return;

        destroyed = true;
        SetDamageEnabled(false);
        if (objectA != null) objectA.SetActive(false);
        if (objectB != null) objectB.SetActive(true);
        Destroyed?.Invoke(this);
        onDestroyed.Invoke();
    }
}
