# NGH 적 AI — 다른 파트 요청 사항

- 작성: 남귀훈 (NGH)
- 최종 수정: 2026-10-01
- 관련 파일: `Assets/Scripts/NGH/CHG_EnemyAI.cs`, `Assets/Scripts/NGH/CHG_EnemyAttackHitbox.cs`, `Assets/Scripts/NGH/CHG_ObjectPool.cs`, `Assets/Prefabs/NGH/CHG_EnemyAttackHitbox.prefab`
- 테스트 씬: `Assets/Scenes/NGH/EnemyTest.unity`

---

## 1. [플레이어 담당] EnemyAttack 피격 처리 요청

적의 근접 공격은 **공격하는 순간에만** `EnemyAttack` 태그를 가진 트리거 콜라이더(`CHG_EnemyAttackHitbox`)를 꺼내서 판정하는 방식입니다. 히트박스는 약 0.15초 유지된 뒤 사라집니다. 공격할 때 적이 살짝 앞으로 전진하며, 히트박스도 적을 따라 움직입니다.

Player는 NGH 영역이 아니라서 직접 수정하지 않았습니다. 플레이어 쪽에서 아래 처리를 부탁드립니다.

- Player 스크립트에서 `OnTriggerEnter(Collider other)`로 `other.CompareTag("EnemyAttack")`를 검사해 피격 처리
- 데미지: 회의 기준 일반몹 1. 히트박스의 `Damage` 값으로 읽을 수 있습니다 (선택 사항)
- 히트박스에 Kinematic Rigidbody가 붙어 있어서, Player에 Rigidbody가 없어도 트리거 이벤트가 발생합니다. Player 콜라이더는 지금처럼 isTrigger 꺼진 상태로 두면 됩니다
- 구르기 무적 / 피격 후 무적 시간은 플레이어 쪽에서 처리 부탁드립니다
- **히트박스는 오브젝트 풀로 재사용됩니다.** 플레이어 쪽에서 히트박스를 `Destroy` 하거나 참조를 오래 들고 있지 말아 주세요 (반납 후 다른 공격에 재사용됩니다)

예시 코드:

```csharp
private void OnTriggerEnter(Collider other)
{
    if (!other.CompareTag("EnemyAttack"))
    {
        return;
    }

    int damage = 1;
    CHG_EnemyAttackHitbox hitbox = other.GetComponent<CHG_EnemyAttackHitbox>();
    if (hitbox != null)
    {
        damage = hitbox.Damage;
    }

    // TODO: 체력 감소, 무적 처리
}
```

## 2. [공통] Player 태그 사용 규칙

적은 `GameObject.FindGameObjectWithTag("Player")`로 플레이어를 찾습니다. 씬에서 **Player 태그는 플레이어 본체 하나에만** 붙여 주세요. (무기, 자식 오브젝트 등에 중복으로 붙이면 엉뚱한 대상을 쫓을 수 있습니다)

또한 적은 공격 전진 시 Player의 (트리거가 아닌) 콜라이더 표면 앞에서 멈춥니다. Player에는 isTrigger가 꺼진 콜라이더가 최소 하나 있어야 합니다.

## 3. [공통] 오브젝트 풀 사용 안내 (선택)

`CHG_ObjectPool`은 누구나 가져다 쓸 수 있습니다. 씬에 따로 배치할 필요 없이 처음 호출할 때 자동으로 생성됩니다.

```csharp
Bullet bullet = CHG_ObjectPool.Spawn(bulletPrefab, position, rotation); // Instantiate 대신
CHG_ObjectPool.Despawn(bullet.gameObject);       // Destroy 대신
CHG_ObjectPool.Despawn(bullet.gameObject, 2f);   // 2초 뒤 반납
```

풀링되는 오브젝트는 Awake/Start가 처음 한 번만 호출되므로, 꺼낼 때마다 초기화가 필요하면 `CHG_IPoolable`을 구현해서 `OnSpawned()`에서 처리하면 됩니다.

## 4. 참고: 사용 중인 태그

| 태그 | 용도 | 상태 |
| --- | --- | --- |
| Player | 적이 인지/추적할 대상 | 등록됨 |
| Enemy | 적 오브젝트 | 등록됨 |
| EnemyAttack | 적 공격 판정 히트박스 | 등록됨 |

태그는 모두 이미 등록되어 있어 추가 요청은 없습니다.
