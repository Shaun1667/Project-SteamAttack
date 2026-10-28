using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

[DisallowMultipleComponent]
public sealed class BossCoreController : MonoBehaviour
{
    [Tooltip("서로 다른 서브코어 4개를 지정하세요.")]
    [SerializeField] private BossCoreHealth[] subCores = new BossCoreHealth[4];
    [SerializeField] private BossCoreHealth mainCore;
    [Tooltip("연결하면 서브코어 4개 파괴 시 껍질 연출 후 메인코어 피격을 자동으로 켭니다.")]
    [SerializeField] private BossShellBurst shellBurst;
    [Tooltip("연출 없이 즉시 노출할 때만 체크하세요.")]
    [SerializeField] private bool exposeImmediately;
    [Tooltip("등껍질 파괴 또는 코어 상승 연출을 연결하세요.")]
    [SerializeField] private UnityEvent onExposureStarted = new UnityEvent();
    [SerializeField] private UnityEvent onMainCoreExposed = new UnityEvent();
    [SerializeField] private UnityEvent onBossDied = new UnityEvent();

    public int DestroyedSubCoreCount => brokenCores.Count;
    public bool IsMainCoreExposed { get; private set; }
    public bool IsDead { get; private set; }

    private readonly HashSet<BossCoreHealth> brokenCores = new HashSet<BossCoreHealth>();
    private bool exposureStarted;
    private bool ready;

    private void Start()
    {
        var unique = new HashSet<BossCoreHealth>();
        bool valid = mainCore != null && subCores != null && subCores.Length == 4;
        if (subCores != null)
        {
            foreach (BossCoreHealth core in subCores)
            {
                if (core == null || core == mainCore || !unique.Add(core)) valid = false;
            }
        }

        if (!valid)
        {
            Debug.LogError("메인코어 1개와 서로 다른 서브코어 4개를 지정하세요.", this);
            return;
        }

        mainCore.Prepare(false);
        if (shellBurst != null) shellBurst.BurstFinished += CompleteMainCoreExposure;
        mainCore.Destroyed += HandleMainCoreDestroyed;
        foreach (BossCoreHealth core in subCores)
        {
            core.Prepare(true);
            core.Destroyed += HandleSubCoreDestroyed;
        }
        ready = true;
    }

    private void HandleSubCoreDestroyed(BossCoreHealth core)
    {
        if (!ready || IsDead || !brokenCores.Add(core)) return;
        if (brokenCores.Count != 4 || exposureStarted) return;

        exposureStarted = true;
        onExposureStarted.Invoke();
        if (shellBurst != null) shellBurst.BurstShell();
        else if (exposeImmediately) CompleteMainCoreExposure();
    }

    // 노출 애니메이션이 끝났을 때 호출하세요.
    public void CompleteMainCoreExposure()
    {
        if (!ready || !exposureStarted || IsMainCoreExposed || IsDead) return;
        IsMainCoreExposed = true;
        NGH.ReviewedAudio.Play("Revision/48_CoreExpose_v02");
        mainCore.SetDamageEnabled(true);
        onMainCoreExposed.Invoke();
    }

    private void HandleMainCoreDestroyed(BossCoreHealth core)
    {
        if (IsDead) return;
        IsDead = true;
        NGH.ReviewedAudio.Play("Initial/08_Boss/BossCollapse_v01");
        onBossDied.Invoke();
    }

    private void OnDestroy()
    {
        if (shellBurst != null) shellBurst.BurstFinished -= CompleteMainCoreExposure;
        if (mainCore != null) mainCore.Destroyed -= HandleMainCoreDestroyed;
        if (subCores == null) return;
        foreach (BossCoreHealth core in subCores)
            if (core != null) core.Destroyed -= HandleSubCoreDestroyed;
    }
}
