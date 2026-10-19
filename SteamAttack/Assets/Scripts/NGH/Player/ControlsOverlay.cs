using UnityEngine;
using UnityEngine.InputSystem;

namespace NGH
{
    /// <summary>ESC로 조작키 안내 이미지를 열고 닫습니다. 열려 있는 동안 게임은 일시정지됩니다.</summary>
    public class ControlsOverlay : MonoBehaviour
    {
        public Texture2D guide;
        [Tooltip("안내가 열려 있는 동안 게임 일시정지")]
        public bool pauseWhileOpen = true;
        [Range(0.3f, 1f)] public float maxScreenFraction = 0.85f;

        /// <summary>조작 안내나 ESC 메뉴가 열려 있음 — 플레이어 입력·카메라를 멈추는 기준</summary>
        public static bool IsOpen => GuideOpen || MenuOpen;
        /// <summary>이 조작 안내가 열려 있음</summary>
        public static bool GuideOpen { get; private set; }
        /// <summary>ESC 메뉴(NMJ PauseMenu)가 열려 있음. 메뉴가 직접 설정 (병합 — NMJ ESC 메뉴)</summary>
        public static bool MenuOpen { get; set; }
        /// <summary>ESC 메뉴가 ESC 키를 쓰는 중. 이때 이 안내는 ESC로 열리지 않고 메뉴의 '조작법'에서 본다 (병합 — NMJ ESC 메뉴)</summary>
        public static bool EscapeHandledByMenu { get; set; }

        float _prevTimeScale = 1f;

        // 플레이 시작 때 도메인을 다시 불러오지 않는 설정에서도 지난 플레이 값이 남지 않게
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { GuideOpen = false; MenuOpen = false; EscapeHandledByMenu = false; }

        void Awake() { GuideOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && !EscapeHandledByMenu && kb.escapeKey.wasPressedThisFrame) SetOpen(!GuideOpen);
        }

        void SetOpen(bool open)
        {
            GuideOpen = open;
            if (open)
            {
                if (pauseWhileOpen) { _prevTimeScale = Time.timeScale; Time.timeScale = 0f; }
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else
            {
                if (pauseWhileOpen) Time.timeScale = _prevTimeScale <= 0f ? 1f : _prevTimeScale;
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        void OnDisable()
        {
            if (GuideOpen) SetOpen(false);
        }

        void OnGUI()
        {
            if (!GuideOpen || guide == null) return;

            // 뒤쪽 화면 어둡게
            var prev = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, 0.45f);
            GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
            GUI.color = prev;

            float s = Mathf.Min(Screen.width * maxScreenFraction / guide.width,
                                Screen.height * maxScreenFraction / guide.height);
            float w = guide.width * s, h = guide.height * s;
            GUI.DrawTexture(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h),
                            guide, ScaleMode.ScaleToFit, true);
        }
    }
}
