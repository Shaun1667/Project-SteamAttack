using System.IO;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace NMJ.EditorTools
{
    /// <summary>
    /// HUD 한자 전용 고정(Static) 폰트 에셋을 만든다.
    ///
    /// NGH의 조선 폰트(ChosunCentennial_otf SDF)는 고정 글자 목록에 한자가 7개뿐이라,
    /// 刀 같은 글자는 NGH의 동적 폰트가 실행 중에 채우면서 그 에셋 파일이 계속 바뀐다.
    /// HUD에 필요한 글자만 담은 고정 폰트를 NMJ 폴더에 따로 두면 실행 중에 바뀌는 파일이 없다.
    ///
    /// 글자를 더하려면 Characters에 넣고 메뉴를 다시 실행하면 된다 (같은 자리에 파일 내용만 덮어써 guid 유지).
    /// </summary>
    public static class NmjHudFontBuilder
    {
        public const string AssetPath = "Assets/Fonts/NMJ/NMJ_HudGlyphs SDF.asset";

        // 무기 패(刀 銃), 증기 행동 글자(強 銃 逆), 낙관(命), 보스 이름(玄武), 숫자·기호,
        // 화승총 안내("증기 부족 · 칼로 베어 채우기")
        private const string Characters = "刀銃強逆命玄武討御×0123456789 ·/+증기부족칼로베어채우";

        // 앞의 폰트에 글자가 없으면 다음 폰트에서 찾는다
        private static readonly string[] SourceFonts =
        {
            "Assets/Fonts/NGH/ChosunCentennial_otf.otf",
            "Assets/Fonts/NGH/NotoSerifKR-Regular.otf",
        };

        [MenuItem("SteamAttack/NMJ/HUD 한자 폰트 만들기")]
        public static void Build()
        {
            TMP_FontAsset built = null;
            foreach (string src in SourceFonts)
            {
                var font = AssetDatabase.LoadAssetAtPath<Font>(src);
                if (font == null) continue;

                var fa = TMP_FontAsset.CreateFontAsset(font, 72, 9, GlyphRenderMode.SDFAA, 512, 512,
                    AtlasPopulationMode.Dynamic, false);
                fa.TryAddCharacters(Characters, out string missing);
                if (!string.IsNullOrEmpty(missing))
                {
                    Debug.Log($"[NMJ] {Path.GetFileName(src)}에 없는 글자: {missing} — 다음 폰트로 시도");
                    Object.DestroyImmediate(fa.material);
                    Object.DestroyImmediate(fa.atlasTexture);
                    Object.DestroyImmediate(fa);
                    continue;
                }
                built = fa;
                Debug.Log($"[NMJ] HUD 한자 폰트 원본: {Path.GetFileName(src)}");
                break;
            }

            if (built == null)
            {
                Debug.LogError("[NMJ] HUD 한자 폰트: 모든 글자를 가진 원본 폰트가 없습니다.");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(AssetPath));
            built.name = Path.GetFileNameWithoutExtension(AssetPath);
            built.atlasPopulationMode = AtlasPopulationMode.Static;
            built.atlasTexture.name = built.name + " Atlas";
            built.material.name = built.name + " Material";
            // 면 텍스처(마른 붓)·밑깔림을 쓰려고 Mobile이 아닌 기본 SDF 셰이더를 쓴다
            built.material.shader = Shader.Find("TextMeshPro/Distance Field");

            // 임시 폴더에 같은 파일 이름으로 새로 저장한다 (에셋 이름이 파일 이름을 따라가므로)
            string dir = Path.GetDirectoryName(AssetPath).Replace('\\', '/');
            string tempDir = dir + "/_Temp";
            AssetDatabase.DeleteAsset(tempDir);
            AssetDatabase.CreateFolder(dir, "_Temp");
            string tempPath = tempDir + "/" + Path.GetFileName(AssetPath);
            AssetDatabase.CreateAsset(built, tempPath);
            AssetDatabase.AddObjectToAsset(built.atlasTexture, built);
            AssetDatabase.AddObjectToAsset(built.material, built);
            EditorUtility.SetDirty(built);
            AssetDatabase.SaveAssets();

            if (File.Exists(AssetPath))
            {
                // 이미 있으면 파일 내용만 바꿔 guid(.meta)를 지킨다 — 프리팹 등에서 건 참조가 그대로 유지됨
                File.Copy(tempPath, AssetPath, true);
                AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            }
            else
            {
                AssetDatabase.MoveAsset(tempPath, AssetPath);
            }
            AssetDatabase.DeleteAsset(tempDir);
            AssetDatabase.SaveAssets();
            Debug.Log($"[NMJ] HUD 한자 폰트 저장: {AssetPath} ({Characters.Length}자)");
        }
    }
}
