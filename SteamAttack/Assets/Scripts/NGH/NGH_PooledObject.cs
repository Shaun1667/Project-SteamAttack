using UnityEngine;

/// <summary>
/// NGH_ObjectPool 이 만든 오브젝트에 자동으로 붙는 표식입니다. 직접 붙일 필요 없습니다.
/// 어느 풀 출신인지, 지금 풀 안에 있는지를 기억합니다.
/// </summary>
[DisallowMultipleComponent]
public class NGH_PooledObject : MonoBehaviour
{
    internal object poolKey;
    internal bool inPool;
    internal int spawnVersion;   // 꺼낼 때마다 증가 → 예약된 지연 반납이 다음 사용 때 잘못 실행되지 않게

    /// <summary>지금 풀 안에서 쉬고 있는지</summary>
    public bool InPool => inPool;

    /// <summary>자기 자신을 풀로 반납 (delay 초 뒤)</summary>
    public void Despawn(float delay = 0f) => NGH_ObjectPool.Despawn(gameObject, delay);
}
