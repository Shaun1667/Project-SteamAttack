
namespace NGH
{
    /// <summary>
    /// 풀에서 꺼내질 때 / 풀로 돌아갈 때 상태를 초기화하고 싶은 컴포넌트가 구현합니다.
    /// 오브젝트(자식 포함)에 붙은 모든 IPoolable 이 호출됩니다.
    ///   OnSpawned   : ObjectPool.Spawn 으로 꺼내져 활성화된 직후 (HP·이펙트·타이머 리셋 등)
    ///   OnDespawned : ObjectPool.Despawn 으로 반납되어 비활성화되기 직전 (락온 해제·사운드 정지 등)
    /// Awake/Start 는 처음 만들어질 때 한 번만 실행되므로, 재사용 시 초기화는 여기서 하세요.
    /// </summary>
    public interface IPoolable
    {
        void OnSpawned();
        void OnDespawned();
    }
}
