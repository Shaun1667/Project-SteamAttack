using SteamAttack.InventorySystem;
using SteamAttack.Items;
using UnityEngine;
using UnityEngine.InputSystem;

// 인스펙터에서만 채우는 직렬화 필드라 "할당된 적 없음" 경고는 의미가 없다.
#pragma warning disable CS0649

namespace SteamAttack.Quests
{
    /// <summary>
    /// 플레이어 컨트롤러 없이도 메인 퀘스트 흐름을 확인하기 위한 개발용 테스터.
    /// 실제 빌드에 올리는 물건이 아니다. 화면 안내는 폰트 문제를 피하려고 영문으로 둔다.
    /// </summary>
    public class QuestDebugTester : MonoBehaviour
    {
        [SerializeField] private ItemData firewaterCore;
        [SerializeField] private ItemData armillaryRing;
        [SerializeField] private ItemData hyeonmuScale;

        [SerializeField] private bool showOnScreenGuide = true;

        private void Update()
        {
            var keyboard = Keyboard.current;
            var quests = QuestManager.Instance;
            var player = PlayerInventory.Instance;
            if (keyboard == null || quests == null) return;

            if (keyboard.f1Key.wasPressedThisFrame) quests.ReportTalk("npc_gojong");
            if (keyboard.f2Key.wasPressedThisFrame) quests.ReportTalk("npc_gigichang_gisulja");
            if (keyboard.f3Key.wasPressedThisFrame) quests.ReportTalk("npc_baekdongsu");
            if (keyboard.f4Key.wasPressedThisFrame) quests.ReportKill("enemy_heuksu_fanatic");
            if (keyboard.f5Key.wasPressedThisFrame) quests.ReportKill("enemy_byeonibyeong");
            if (keyboard.f6Key.wasPressedThisFrame && player != null) player.AddPartial(firewaterCore, 1);
            if (keyboard.f7Key.wasPressedThisFrame && player != null) player.AddPartial(armillaryRing, 1);
            if (keyboard.f8Key.wasPressedThisFrame) quests.ReportReach("loc_gigichang");
            if (keyboard.f9Key.wasPressedThisFrame) quests.ReportReach("loc_heuksu_altar");

            if (keyboard.f10Key.wasPressedThisFrame)
            {
                quests.ReportKill("boss_hyeonmu");
                if (player != null) player.AddPartial(hyeonmuScale, 1);
            }

            if (keyboard.f11Key.wasPressedThisFrame) CompleteReadyQuest();
        }

        private void CompleteReadyQuest()
        {
            var quests = QuestManager.Instance;
            foreach (var progress in quests.All)
            {
                if (progress.Status != QuestStatus.ReadyToComplete) continue;

                bool ok = quests.Complete(progress.Data.QuestId);
                Debug.Log($"[QuestDebugTester] '{progress.Data.Title}' 보고 -> {ok}");
                return;
            }

            Debug.Log("[QuestDebugTester] 보고할 수 있는 퀘스트가 없다.");
        }

        private void OnGUI()
        {
            if (!showOnScreenGuide) return;

            GUILayout.BeginArea(new Rect(10, 10, 330, 250), GUI.skin.box);
            GUILayout.Label("MAIN QUEST DEBUG KEYS");
            GUILayout.Label("F1   Talk Gojong (Geoncheonggung)");
            GUILayout.Label("F2   Talk Gigichang engineer");
            GUILayout.Label("F3   Talk Baek Dongsu");
            GUILayout.Label("F4   Kill Heuksu fanatic x1");
            GUILayout.Label("F5   Kill Byeonibyeong x1");
            GUILayout.Label("F6   Get Firewater Core x1");
            GUILayout.Label("F7   Get Armillary Ring x1");
            GUILayout.Label("F8   Reach Gigichang");
            GUILayout.Label("F9   Reach Heuksu Altar");
            GUILayout.Label("F10 Kill Hyeonmu + Scale");
            GUILayout.Label("F11 Turn in ready quest");
            GUILayout.Label("I     Toggle inventory");
            GUILayout.EndArea();
        }
    }
}
