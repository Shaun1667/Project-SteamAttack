using UnityEngine;

namespace NGH
{
    /// <summary>
    /// 빌드 첫 실행 때 화면을 1920×1080 전체 화면(FullScreenWindow)으로 맞춘다.
    /// 유니티는 마지막 창 크기·모드를 레지스트리에 기억해 다음 실행에 되살리므로,
    /// 예전 빌드에서 1024×768 창 모드로 남은 값이 있으면 플레이어 설정의 기본값이 무시된다.
    /// DisplayVersion을 올리면 한 번 더 기본값으로 되돌린다. 이후 설정 화면에서 바꾼 값은 그대로 유지된다.
    /// </summary>
    public static class DisplayDefaults
    {
        private const string KeyVersion = "NGH.Display.Version";
        private const int DisplayVersion = 1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Apply()
        {
            if (Application.isEditor) return;
            if (PlayerPrefs.GetInt(KeyVersion, 0) >= DisplayVersion) return;

            Screen.SetResolution(1920, 1080, FullScreenMode.FullScreenWindow);
            PlayerPrefs.SetInt(KeyVersion, DisplayVersion);
            PlayerPrefs.Save();
        }
    }
}
