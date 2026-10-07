using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>쓰러지는 동작이 끝나면 Game Over 이미지를 보여 주고, 아무 키나 클릭하면 그 자리에서 일어나 다시 시작합니다.</summary>
public class NGH_GameOverScreen : MonoBehaviour
{
    public NGH_PlayerController player;
    public Texture2D image;
    [Tooltip("쓰러지는 동작이 끝난 뒤 화면이 뜰 때까지 추가 시간(초)")]
    public float showDelay = 0.3f;
    [Tooltip("화면이 뜬 뒤 입력을 받기 시작할 때까지 시간(초) — 연타로 바로 넘어가는 것 방지")]
    public float inputDelay = 0.5f;
    [Range(0.3f, 1f)] public float maxScreenFraction = 0.6f;

    public static bool IsShowing { get; private set; }
    float _shownAt, _finishedAt = -1f;

    void Awake()
    {
        if (!player) player = GetComponentInParent<NGH_PlayerController>();
        IsShowing = false;
    }

    void Update()
    {
        if (!player) return;
        if (!player.IsDead) { IsShowing = false; _finishedAt = -1f; return; }

        // 쓰러지는 동작이 끝나면 Game Over 표시
        if (player.IsDeathAnimFinished && _finishedAt < 0f) _finishedAt = Time.time;
        if (!IsShowing && _finishedAt >= 0f && Time.time - _finishedAt >= showDelay)
        {
            IsShowing = true; _shownAt = Time.unscaledTime;
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        if (IsShowing && Time.unscaledTime - _shownAt >= inputDelay && AnyInput())
        {
            IsShowing = false;
            Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false;
            player.Respawn();
        }
    }

    static bool AnyInput()
    {
        var kb = Keyboard.current; var m = Mouse.current;
        return (kb != null && kb.anyKey.wasPressedThisFrame)
            || (m != null && (m.leftButton.wasPressedThisFrame || m.rightButton.wasPressedThisFrame || m.middleButton.wasPressedThisFrame));
    }

    void OnGUI()
    {
        if (!IsShowing) return;
        var prev = GUI.color;
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(0, 0, Screen.width, Screen.height), Texture2D.whiteTexture);
        GUI.color = prev;
        if (!image) return;
        float s = Mathf.Min(Screen.width * maxScreenFraction / image.width, Screen.height * maxScreenFraction / image.height);
        float w = image.width * s, h = image.height * s;
        GUI.DrawTexture(new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h), image, ScaleMode.ScaleToFit, true);
    }
}
