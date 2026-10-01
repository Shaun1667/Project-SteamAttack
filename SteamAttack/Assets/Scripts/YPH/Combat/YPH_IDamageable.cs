using UnityEngine;

/// <summary>총과 수류탄이 피해를 전달할 대상의 최소 약속입니다. 실제 HP 구현은 대상이 맡습니다.</summary>
public interface YPH_IDamageable
{
    /// <summary>공격 직전에 확인합니다. 쓰러진 대상을 때려 증기를 회복하지 못하게 합니다.</summary>
    bool IsAlive { get; }

    /// <summary>피해량과 공격이 시작된 위치를 받습니다. 위치는 밀려나는 방향 등에 사용할 수 있습니다.</summary>
    void TakeDamage(float amount, Vector3 hitFrom);

    /// <summary>피해 컴포넌트가 붙은 오브젝트의 태그입니다. MonoBehaviour의 기존 메서드를 그대로 씁니다.</summary>
    bool CompareTag(string tag);
}
