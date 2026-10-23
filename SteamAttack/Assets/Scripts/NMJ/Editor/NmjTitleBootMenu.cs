using UnityEditor;

namespace NMJ.EditorTools
{
    /// <summary>
    /// 에디터에서 Main을 열고 Play를 누르면 원래는 타이틀부터 시작한다 (NmjBootToTitle).
    /// Main만 빨리 시험하고 싶을 때 이 메뉴에 체크하면 타이틀을 건너뛴다. 내 컴퓨터에만 저장되는 개인 설정.
    /// </summary>
    public static class NmjTitleBootMenu
    {
        private const string MenuPath = "SteamAttack/NMJ/에디터에서 타이틀 건너뛰기";

        [MenuItem(MenuPath)]
        private static void Toggle()
        {
            bool on = !EditorPrefs.GetBool(NmjBootToTitle.SkipInEditorKey, false);
            EditorPrefs.SetBool(NmjBootToTitle.SkipInEditorKey, on);
            UnityEngine.Debug.Log(on ? "[NMJ] 에디터 Play: 타이틀을 건너뛰고 바로 시작" : "[NMJ] 에디터 Play: 타이틀부터 시작");
        }

        [MenuItem(MenuPath, true)]
        private static bool ToggleCheck()
        {
            Menu.SetChecked(MenuPath, EditorPrefs.GetBool(NmjBootToTitle.SkipInEditorKey, false));
            return true;
        }
    }
}
