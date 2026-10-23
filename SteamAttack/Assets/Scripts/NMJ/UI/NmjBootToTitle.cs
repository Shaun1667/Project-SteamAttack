using UnityEngine;
using UnityEngine.SceneManagement;

namespace NMJ
{
    /// <summary>
    /// 게임은 언제나 타이틀부터: 타이틀 → (게임 시작) → 오프닝 → 육조거리.
    ///
    /// - 게임 씬(오프닝이 있는 Main)이 타이틀을 거치지 않고 처음 열리면 (에디터에서 Main을 열고 Play 등) 타이틀로 보낸다.
    ///   빌드는 타이틀이 첫 씬이라 원래 그렇게 돈다. 개발자 창의 구간 이동 같은 '처음 연 뒤'의 씬 이동은 건드리지 않는다.
    /// - 에디터에서 바로 Main을 시험하고 싶으면 메뉴 SteamAttack ▸ NMJ ▸ 에디터에서 타이틀 건너뛰기 (개인 설정)
    /// - 엔딩·ESC 메뉴의 "타이틀로"가 쓸 씬 이름을 고른다: 적힌 이름이 빌드에 없으면 Title → NMJ_Title 순으로 찾는다.
    /// </summary>
    public static class NmjBootToTitle
    {
        public const string SkipInEditorKey = "NMJ_SkipTitleInEditor";
        private static readonly string[] Fallbacks = { "Title", "NMJ_Title" };

        /// <summary>이번 실행에서 타이틀 화면을 한 번이라도 열었음 (TitleScreen이 켠다)</summary>
        public static bool TitleVisited { get; set; }

        /// <summary>지금 타이틀로 보내는 중 — 이 프레임의 게임 씬 Start(오프닝 등)는 아무것도 하지 않는다</summary>
        public static bool Redirecting { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            TitleVisited = false;
            Redirecting = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            if (m == LoadSceneMode.Single) Redirecting = false;
        }

        // 첫 씬의 Awake 뒤, Start 전에 한 번만 불린다
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CheckFirstScene()
        {
            if (TitleVisited || Object.FindAnyObjectByType<TitleScreen>() != null) return;
            if (Object.FindAnyObjectByType<NmjOpening>() == null) return;   // 게임 씬이 아니면 (시험 씬 등) 그대로
#if UNITY_EDITOR
            if (UnityEditor.EditorPrefs.GetBool(SkipInEditorKey, false)) return;
#endif
            string active = SceneManager.GetActiveScene().name;
            string title = Resolve(active.StartsWith("NMJ_") ? "NMJ_Title" : "Title");
            if (title == null) { Debug.LogWarning("[NMJ] 빌드 설정에 타이틀 씬이 없어 바로 시작합니다"); return; }

            Redirecting = true;
            Debug.Log($"[NMJ] {active}을(를) 바로 열어 타이틀({title})부터 시작합니다");
            SceneManager.LoadScene(title);
        }

        /// <summary>불러올 수 있는 타이틀 씬 이름 (적힌 이름 → Title → NMJ_Title). 없으면 null</summary>
        public static string Resolve(string preferred)
        {
            if (!string.IsNullOrEmpty(preferred) && Application.CanStreamedLevelBeLoaded(preferred)) return preferred;
            foreach (string f in Fallbacks)
                if (Application.CanStreamedLevelBeLoaded(f)) return f;
            return null;
        }
    }
}
