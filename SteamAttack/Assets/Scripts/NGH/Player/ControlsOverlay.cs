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

        public static bool IsOpen { get; private set; }

        float _prevTimeScale = 1f;

        void Awake() { IsOpen = false; }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame) SetOpen(!IsOpen);
        }

        void SetOpen(bool open)
        {
            IsOpen = open;
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
            if (IsOpen) SetOpen(false);
        }

        void OnGUI()
        {
            if (!IsOpen || guide == null) return;

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
