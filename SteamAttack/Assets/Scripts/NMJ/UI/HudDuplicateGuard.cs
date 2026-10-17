using NGH;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 따로 놓는 HUD 프리팹(예: YPH_TemporaryPlayerHUD)에 붙인다.
    /// 씬의 플레이어(NGH_Player)에 이미 NMJ HUD가 붙어 있으면 이 오브젝트를 꺼서 HUD가 두 개 겹치지 않게 한다.
    /// 플레이어에 HUD가 없는 씬(다른 플레이어를 쓰는 시험 씬 등)에서는 그대로 켜 둔다.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class HudDuplicateGuard : MonoBehaviour
    {
        private void Awake()
        {
            PlayerController player = FindAnyObjectByType<PlayerController>();
            if (player == null) return;
            bool hasOwnHud = player.GetComponentInChildren<InkHealthBar>(true) != null
                             || player.GetComponentInChildren<SteamCylinderGauge>(true) != null;
            if (hasOwnHud && !transform.IsChildOf(player.transform))
            {
                Debug.Log($"[NMJ] '{name}': 플레이어에 HUD가 이미 있어 이 HUD는 끕니다.", this);
                gameObject.SetActive(false);
            }
        }
    }
}
