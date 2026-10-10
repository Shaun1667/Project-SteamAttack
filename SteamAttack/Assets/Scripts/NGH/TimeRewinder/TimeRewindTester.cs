using UnityEngine;
using UnityEngine.InputSystem;
using YPH;

namespace NGH
{
    /// <summary>
    /// TimeTest 씬 전용 테스트 입력 (NGH)
    /// 아직 증기를 쓰는 공격이 없어서, 키로 증기를 소비/회복해 시간 역행이 증기 게이지를 되돌리는지 확인한다.
    /// (체력 테스트는 NGH_PlayerController의 디버그 키 1·2 = 자신에게 데미지 1·2)
    /// </summary>
    public class TimeRewindTester : MonoBehaviour
    {
        [Tooltip("비우면 씬에서 찾음")]
        [SerializeField] private SteamTank steamTank;
        [SerializeField] private Key consumeKey = Key.Digit3;
        [SerializeField, Min(0f)] private float consumeAmount = 20f;
        [SerializeField] private Key refillKey = Key.Digit4;
        [SerializeField, Min(0f)] private float refillAmount = 20f;

        private void Start()
        {
            if (!steamTank) steamTank = FindAnyObjectByType<SteamTank>();
            if (!steamTank)
            {
                Debug.LogWarning("[NGH_TimeRewindTester] YPH_SteamTank를 찾지 못했습니다.", this);
                enabled = false;
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[consumeKey].wasPressedThisFrame)
            {
                bool ok = steamTank.TryConsume(consumeAmount);
                Debug.Log($"[NGH_TimeRewindTester] 증기 소비 {consumeAmount} → {(ok ? "성공" : "부족")}, {steamTank.CurrentPressure:0.#}/{steamTank.MaxPressure:0}", this);
            }
            if (keyboard[refillKey].wasPressedThisFrame)
            {
                steamTank.Refill(refillAmount);
                Debug.Log($"[NGH_TimeRewindTester] 증기 회복 {refillAmount} → {steamTank.CurrentPressure:0.#}/{steamTank.MaxPressure:0}", this);
            }
        }
    }
}
