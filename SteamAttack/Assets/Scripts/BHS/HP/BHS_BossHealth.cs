using UnityEngine;

public class BHS_BossHealth : MonoBehaviour
{
    [Header("보스 체력")]
    public float maxHp = 100f;

    [Header("가운데 코어의 Collider")]
    public Collider coreCollider;

    public float CurrentHp { get; private set; }
    public bool IsCoreExposed { get; private set; }
    public bool IsAlive => CurrentHp > 0f;

    private int brokenWallCount;
    private const int WallCount = 4;

    private void Awake()
    {
        CurrentHp = maxHp;
        brokenWallCount = 0;
        IsCoreExposed = false;

        // 코어의 외형과 충돌 판정을 함께 꺼둠
        if (coreCollider != null)
            coreCollider.gameObject.SetActive(false);
    }

    public void OnWallDestroyed()
    {
        if (!IsAlive || IsCoreExposed)
            return;

        brokenWallCount++;

        Debug.Log($"벽 파괴: {brokenWallCount}/{WallCount}");

        if (brokenWallCount < WallCount)
            return;

        // 벽 4개가 모두 파괴되면 코어 노출
        IsCoreExposed = true;

        if (coreCollider != null)
            coreCollider.gameObject.SetActive(true);

        Debug.Log("코어 노출! 보스에게 데미지를 줄 수 있습니다.");
    }

    public void TakeDamage(float amount, Vector3 from)
    {
        // 코어가 노출되기 전에는 보스 체력이 줄지 않음
        if (!IsAlive || !IsCoreExposed || amount <= 0f)
            return;

        CurrentHp = Mathf.Max(0f, CurrentHp - amount);

        Debug.Log($"보스 피격: {CurrentHp}/{maxHp}");

        if (!IsAlive)
        {
            Debug.Log("보스 사망!");

            // 현재는 보스 전체를 비활성화
            gameObject.SetActive(false);
        }
    }
}