using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SteamAttack.EditorTools
{
    /// <summary>
    /// 《현무: 역류의 어명》 세계관 메인 씬 생성기.
    /// 고종 24년(1887) 정월, 건청궁에 조선 최초의 전등이 켜지던 밤 — 향원정 물로 돌리던 증기 발전기가
    /// 신맥을 뚫고, 깨어난 현무가 열기와 '흐르는 시간'을 삼켜 도성이 멈춰 얼어붙은 순간을 수묵화풍으로 세운다.
    ///
    /// 메뉴: SteamAttack ▸ NMJ ▸ 현무 세계관 메인 씬 생성
    /// 결과물은 모두 NMJ 폴더에 만든다(씬 / 머티리얼 / 메시 / 텍스처 / 셰이더).
    /// </summary>
    public static class HyeonmuWorldBuilder
    {
        public const string ScenePath = "Assets/Scenes/NMJ/HyeonmuWorld_Main.unity";
        private const string MatDir = "Assets/Materials/NMJ/HyeonmuWorld";
        private const string MeshDir = "Assets/Arts/Models/NMJ/HyeonmuWorld";
        private const string TexDir = "Assets/Arts/Textures/NMJ/HyeonmuWorld";
        private const string ShaderDir = "Assets/Shaders/NMJ/HyeonmuWorld";
        private const string ShaderSourceDir = "Assets/Scripts/NMJ/HyeonmuWorld/Shaders";

        // 오방색
        private static readonly Color Cheong = new Color(0.12f, 0.46f, 0.70f);
        private static readonly Color Jeok = new Color(0.82f, 0.18f, 0.10f);
        private static readonly Color Hwang = new Color(0.94f, 0.73f, 0.16f);
        private static readonly Color Baek = new Color(0.96f, 0.95f, 0.92f);
        private static readonly Color Heuk = new Color(0.06f, 0.06f, 0.07f);
        private static readonly Color FogFrozen = new Color(0.78f, 0.80f, 0.80f);

        private static Shader inkToon, inkOutline, inkFx;
        private static readonly Dictionary<string, Material> mats = new Dictionary<string, Material>();
        private static readonly Dictionary<string, Mesh> meshes = new Dictionary<string, Mesh>();
        private static Material O, OThin, OThick, OMountain;
        /// <summary>외곽선을 붙이지 않을 때 넘기는 표식.</summary>
        private static Material NoLine;
        private static System.Random rng;

        [MenuItem("SteamAttack/NMJ/현무 세계관 메인 씬 생성", false, 20)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[SteamAttack] 플레이 모드에서는 씬을 만들 수 없다. 플레이를 멈추고 다시 실행해라.");
                return;
            }

            rng = new System.Random(1887);
            mats.Clear();
            meshes.Clear();

            EnsureFolders();
            if (!PrepareShaders()) return;
            CreateMaterials();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var root = new GameObject("현무_세계관 (1887 정월, 멈춘 도성)").transform;
            BuildAtmosphere(root);
            BuildGround(root);
            BuildPalaceWalls(Group("01_경복궁_궁성", root));
            BuildGwanghwamun(Group("02_광화문", root));
            BuildGeunjeongjeon(Group("03_근정전", root));
            BuildYukjo(Group("04_육조거리", root));
            BuildUnjongga(Group("05_운종가_저잣거리", root));
            BuildGeoncheonggung(Group("06_건청궁_전등과_물불_발전기", root));
            BuildHyangwonjeong(Group("07_향원정_얼어붙은_연못", root));
            BuildGigichang(Group("08_삼청동_기기창", root));
            BuildHyeonmu(Group("09_현무_玄武", root));
            BuildMountains(Group("10_북악산_원경", root));
            BuildPines(Group("11_설송", root));
            BuildObangFlags(Group("12_오방기", root));
            BuildFigures(Group("13_인물", root));
            BuildCamera(root);

            MarkStatic(root);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log($"[SteamAttack] 현무 세계관 메인 씬을 만들었다: {ScenePath}");
        }

        // ── 준비 ───────────────────────────────────────────────

        private static void EnsureFolders()
        {
            foreach (var dir in new[] { MatDir, MeshDir, TexDir, ShaderDir, Path.GetDirectoryName(ScenePath).Replace('\\', '/') })
                CreateFolderRecursive(dir);
        }

        private static void CreateFolderRecursive(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = Path.GetDirectoryName(path).Replace('\\', '/');
            CreateFolderRecursive(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        /// <summary>스크립트 폴더로 들어온 셰이더를 Shaders/NMJ 로 옮기고 불러온다.</summary>
        private static bool PrepareShaders()
        {
            foreach (var name in new[] { "InkToon", "InkOutline", "InkFX" })
            {
                string dst = $"{ShaderDir}/{name}.shader";
                string src = $"{ShaderSourceDir}/{name}.shader";
                if (AssetDatabase.LoadAssetAtPath<Shader>(dst) == null && AssetDatabase.LoadAssetAtPath<Shader>(src) != null)
                {
                    string err = AssetDatabase.MoveAsset(src, dst);
                    if (!string.IsNullOrEmpty(err)) Debug.LogWarning($"[SteamAttack] 셰이더 이동 실패 {src}: {err}");
                }
            }

            if (AssetDatabase.IsValidFolder(ShaderSourceDir) && AssetDatabase.FindAssets("", new[] { ShaderSourceDir }).Length == 0)
                AssetDatabase.DeleteAsset(ShaderSourceDir);

            inkToon = AssetDatabase.LoadAssetAtPath<Shader>($"{ShaderDir}/InkToon.shader") ?? Shader.Find("SteamAttack/InkToon");
            inkOutline = AssetDatabase.LoadAssetAtPath<Shader>($"{ShaderDir}/InkOutline.shader") ?? Shader.Find("SteamAttack/InkOutline");
            inkFx = AssetDatabase.LoadAssetAtPath<Shader>($"{ShaderDir}/InkFX.shader") ?? Shader.Find("SteamAttack/InkFX");

            if (inkToon == null || inkOutline == null || inkFx == null)
            {
                Debug.LogError("[SteamAttack] 수묵 셰이더(InkToon/InkOutline/InkFX)를 찾지 못했다.");
                return false;
            }

            return true;
        }

        // ── 머티리얼 ──────────────────────────────────────────

        private static Material Toon(string name, Color color, Color emission = default, float rim = 0.55f, float grain = 0.12f)
        {
            string path = $"{MatDir}/M_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(inkToon);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = inkToon;
            m.SetColor("_BaseColor", color);
            m.SetColor("_ShadowColor", new Color(0.60f, 0.58f, 0.57f));
            m.SetColor("_InkColor", new Color(0.07f, 0.07f, 0.08f));
            m.SetColor("_EmissionColor", emission);
            m.SetFloat("_RimInk", rim);
            m.SetFloat("_Grain", grain);
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        private static Material Outline(string name, float width, Color color)
        {
            string path = $"{MatDir}/M_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(inkOutline);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = inkOutline;
            m.SetFloat("_Width", width);
            m.SetColor("_OutlineColor", color);
            EditorUtility.SetDirty(m);
            return m;
        }

        private static Material Fx(string name, Color color, Texture2D tex = null, bool vertexColor = false, bool additive = false)
        {
            string path = $"{MatDir}/M_{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null)
            {
                m = new Material(inkFx);
                AssetDatabase.CreateAsset(m, path);
            }

            m.shader = inkFx;
            m.SetColor("_BaseColor", color);
            m.SetTexture("_MainTex", tex);
            m.SetFloat("_UseVertexColor", vertexColor ? 1f : 0f);
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            m.SetFloat("_DstBlend", additive ? (float)BlendMode.One : (float)BlendMode.OneMinusSrcAlpha);
            m.SetFloat("_Cull", (float)CullMode.Off);
            m.renderQueue = 3000;
            EditorUtility.SetDirty(m);
            mats[name] = m;
            return m;
        }

        private static Material Mat(string name) => mats[name];

        private static void CreateMaterials()
        {
            if (NoLine == null) NoLine = new Material(inkOutline) { name = "NoLine (표식)", hideFlags = HideFlags.HideAndDontSave };
            O = Outline("Outline_Ink", 0.045f, new Color(0.06f, 0.06f, 0.07f));
            OThin = Outline("Outline_InkThin", 0.018f, new Color(0.06f, 0.06f, 0.07f));
            OThick = Outline("Outline_InkThick", 0.14f, new Color(0.05f, 0.05f, 0.06f));
            OMountain = Outline("Outline_Mountain", 0.9f, new Color(0.10f, 0.11f, 0.12f));

            Toon("Ink", new Color(0.08f, 0.08f, 0.09f));
            Toon("SnowGround", new Color(0.87f, 0.87f, 0.85f), default, 0.12f, 0.08f);
            Toon("Paving", new Color(0.73f, 0.72f, 0.69f), default, 0.1f, 0.18f);
            Toon("Road", new Color(0.67f, 0.66f, 0.63f), default, 0.1f, 0.2f);
            Toon("Stone", new Color(0.63f, 0.61f, 0.57f));
            Toon("StoneDark", new Color(0.42f, 0.41f, 0.40f));
            Toon("Plaster", new Color(0.91f, 0.88f, 0.81f), default, 0.45f);
            Toon("EarthWall", new Color(0.60f, 0.50f, 0.38f));
            Toon("Timber", new Color(0.30f, 0.22f, 0.16f));
            Toon("PillarRed", new Color(0.60f, 0.16f, 0.11f));
            Toon("Dancheong", new Color(0.16f, 0.48f, 0.44f));
            Toon("RoofTile", new Color(0.17f, 0.18f, 0.20f), default, 0.35f, 0.2f);
            Toon("Ridge", new Color(0.74f, 0.74f, 0.72f));
            Toon("Thatch", new Color(0.66f, 0.56f, 0.35f), default, 0.5f, 0.3f);
            Toon("Charred", new Color(0.12f, 0.10f, 0.09f), default, 0.5f, 0.3f);
            Toon("Brick", new Color(0.50f, 0.31f, 0.26f), default, 0.5f, 0.25f);
            Toon("Iron", new Color(0.15f, 0.16f, 0.17f));
            Toon("Brass", new Color(0.68f, 0.53f, 0.26f));
            Toon("Ice", new Color(0.74f, 0.83f, 0.88f), new Color(0.04f, 0.07f, 0.09f), 0.2f, 0.05f);
            Toon("IceCrystal", new Color(0.83f, 0.93f, 0.99f), new Color(0.12f, 0.22f, 0.32f), 0.3f, 0.02f);
            Toon("Pine", new Color(0.14f, 0.18f, 0.15f), default, 0.5f, 0.25f);
            Toon("PineTrunk", new Color(0.26f, 0.21f, 0.17f));
            Toon("Paper", new Color(0.96f, 0.95f, 0.91f), default, 0.2f, 0.05f);
            Toon("MountainNear", new Color(0.22f, 0.24f, 0.27f), default, 0.3f, 0.15f);
            Toon("MountainMid", new Color(0.33f, 0.36f, 0.39f), default, 0.3f, 0.15f);
            Toon("MountainFar", new Color(0.47f, 0.50f, 0.54f), default, 0.3f, 0.15f);
            Toon("Skin", new Color(0.93f, 0.84f, 0.72f), default, 0.35f, 0.04f);
            Toon("RobeInk", new Color(0.13f, 0.14f, 0.19f));
            Toon("RobeYellow", new Color(0.92f, 0.71f, 0.16f), new Color(0.05f, 0.03f, 0f));
            Toon("RobeWhite", Baek);
            Toon("RobeBlack", new Color(0.06f, 0.06f, 0.07f));
            Toon("RobeGrey", new Color(0.46f, 0.49f, 0.53f));
            Toon("VillagerBrown", new Color(0.52f, 0.41f, 0.30f));
            Toon("VillagerWhite", new Color(0.88f, 0.86f, 0.80f));
            Toon("VillagerIndigo", new Color(0.26f, 0.31f, 0.43f));
            Toon("Frozen", new Color(0.76f, 0.85f, 0.91f), new Color(0.05f, 0.08f, 0.11f), 0.35f, 0.04f);
            Toon("Hyeonmu", new Color(0.07f, 0.08f, 0.09f), default, 0.4f, 0.25f);
            Toon("HyeonmuPlate", new Color(0.12f, 0.13f, 0.14f), default, 0.6f, 0.3f);

            Toon("GlowYellow", Hwang, Hwang * 3f, 0.1f, 0f);
            Toon("GlowBlue", Cheong, Cheong * 3.5f, 0.1f, 0f);
            Toon("GlowRed", Jeok, Jeok * 3f, 0.1f, 0f);
            Toon("GlowFire", new Color(1f, 0.5f, 0.15f), new Color(1f, 0.45f, 0.1f) * 2.5f, 0.1f, 0f);
            Toon("GlowLamp", new Color(1f, 0.88f, 0.6f), new Color(1f, 0.8f, 0.45f) * 4f, 0f, 0f);

            Toon("FlagCheong", Cheong, Cheong * 0.25f, 0.3f);
            Toon("FlagJeok", Jeok, Jeok * 0.25f, 0.3f);
            Toon("FlagHwang", Hwang, Hwang * 0.25f, 0.3f);
            Toon("FlagBaek", Baek, Baek * 0.1f, 0.3f);
            Toon("FlagHeuk", Heuk, default, 0.3f);

            Toon("AwningIndigo", new Color(0.23f, 0.30f, 0.45f));
            Toon("AwningOchre", new Color(0.72f, 0.56f, 0.28f));
            Toon("AwningRust", new Color(0.55f, 0.24f, 0.18f));
            Toon("AwningHemp", new Color(0.78f, 0.74f, 0.62f));

            var dot = SoftDotTexture();
            Fx("FX_Snow", Color.white, dot, true);
            Fx("FX_Mist", new Color(0.88f, 0.88f, 0.86f, 0.24f), dot);
            Fx("FX_MistInk", new Color(0.25f, 0.27f, 0.3f, 0.25f), dot);
            Fx("FX_TimeRing", Cheong.WithA(0.55f));
            Fx("FX_TimeRingFaint", Cheong.WithA(0.22f));
            Fx("FX_FrozenBreath", new Color(0.9f, 0.96f, 1f, 0.3f), dot);
            Fx("FX_Sinmaek", Cheong.WithA(0.8f), dot, false, true);
        }

        private static Texture2D SoftDotTexture()
        {
            string path = $"{TexDir}/T_SoftDot.png";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(path) == null)
            {
                const int Size = 128;
                var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
                for (int y = 0; y < Size; y++)
                {
                    for (int x = 0; x < Size; x++)
                    {
                        float dx = (x + 0.5f) / Size * 2f - 1f;
                        float dy = (y + 0.5f) / Size * 2f - 1f;
                        float d = Mathf.Sqrt(dx * dx + dy * dy);
                        float a = Mathf.Clamp01(1f - d);
                        a = a * a * (3f - 2f * a);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }

                File.WriteAllBytes(path, tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
                AssetDatabase.ImportAsset(path);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.alphaIsTransparency = true;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }

            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        // ── 메시 캐시 ─────────────────────────────────────────

        private static Mesh SaveMesh(string key, System.Func<Mesh> make)
        {
            if (meshes.TryGetValue(key, out var cached)) return cached;

            string path = $"{MeshDir}/{key}.asset";
            var mesh = make();
            mesh.name = key;
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null)
            {
                EditorUtility.CopySerialized(mesh, existing);
                Object.DestroyImmediate(mesh);
                mesh = existing;
            }
            else
            {
                AssetDatabase.CreateAsset(mesh, path);
            }

            meshes[key] = mesh;
            return mesh;
        }

        private static Mesh Roof(float w, float d, float h, float lift)
        {
            string key = $"Roof_{w:0.#}x{d:0.#}x{h:0.#}_{lift:0.##}".Replace('.', 'p');
            return SaveMesh(key, () => WorldMeshFactory.HanokRoof(w, d, h, lift));
        }

        private static Mesh Hex => SaveMesh("HexPrism", () => WorldMeshFactory.Prism(6, 1f, 1f));
        private static Mesh Disc => SaveMesh("Disc", () => WorldMeshFactory.Ring(0f, 1f, 64));
        private static Mesh ThinRing => SaveMesh("RingThin", () => WorldMeshFactory.Ring(0.975f, 1f, 128));
        private static Mesh Spike => SaveMesh("Spike", () => WorldMeshFactory.Mountain(5, 1f, 1f, 0.3f, 7));

        // ── 생성 도우미 ───────────────────────────────────────

        private static Transform Group(string name, Transform parent)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        private static Transform Node(string name, Transform parent, Vector3 pos, float yaw = 0f)
        {
            var t = Group(name, parent);
            t.localPosition = pos;
            t.localRotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        private static GameObject P(PrimitiveType type, string name, Transform parent, Vector3 pos, Vector3 scale,
            string mat, Vector3 euler = default, Material outline = null, bool collider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;

            if (outline == NoLine) outline = null;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterials = outline != null ? new[] { Mat(mat), outline } : new[] { Mat(mat) };
            if (!collider) Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        private static GameObject Box(string name, Transform parent, Vector3 pos, Vector3 scale, string mat,
            Vector3 euler = default, Material outline = null, bool collider = false)
            => P(PrimitiveType.Cube, name, parent, pos, scale, mat, euler, outline ?? O, collider);

        private static GameObject Cyl(string name, Transform parent, Vector3 pos, Vector3 scale, string mat,
            Vector3 euler = default, Material outline = null)
            => P(PrimitiveType.Cylinder, name, parent, pos, scale, mat, euler, outline ?? O);

        private static GameObject Ball(string name, Transform parent, Vector3 pos, Vector3 scale, string mat,
            Vector3 euler = default, Material outline = null)
            => P(PrimitiveType.Sphere, name, parent, pos, scale, mat, euler, outline ?? O);

        private static GameObject MeshObj(string name, Transform parent, Mesh mesh, Vector3 pos, Vector3 euler, Vector3 scale,
            string mat, Material outline = null, bool shadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = outline != null ? new[] { Mat(mat), outline } : new[] { Mat(mat) };
            if (!shadows) r.shadowCastingMode = ShadowCastingMode.Off;
            return go;
        }

        /// <summary>두 점 사이를 잇는 원기둥(배관, 전선, 창대).</summary>
        private static GameObject Pipe(string name, Transform parent, Vector3 a, Vector3 b, float radius, string mat, Material outline = null)
        {
            var go = P(PrimitiveType.Cylinder, name, parent, (a + b) * 0.5f, new Vector3(radius * 2f, Vector3.Distance(a, b) * 0.5f, radius * 2f), mat, default, outline);
            go.transform.localRotation = Quaternion.FromToRotation(Vector3.up, (b - a).normalized);
            return go;
        }

        private static float R(float min, float max) => min + (float)rng.NextDouble() * (max - min);

        private static void MarkStatic(Transform root)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.GetComponent<ParticleSystem>() != null || t.GetComponent<Camera>() != null || t.GetComponent<Light>() != null) continue;
                GameObjectUtility.SetStaticEditorFlags(t.gameObject, StaticEditorFlags.BatchingStatic);
            }
        }

        // ── 한옥 ───────────────────────────────────────────────

        private enum HouseStyle { Palace, Office, Shop, Thatch, Burnt }

        /// <summary>기단 + 기둥 + 벽 + (단청) + 처마 지붕 + 용마루. 원점은 기단 바닥 중앙, +Z 가 정면.</summary>
        private static Transform Hanok(string name, Transform parent, Vector3 pos, float yaw, float w, float d, float h,
            HouseStyle style, bool doubleRoof = false, float baseHeight = 0.7f)
        {
            var t = Node(name, parent, pos, yaw);
            bool palace = style == HouseStyle.Palace;
            bool thatch = style == HouseStyle.Thatch || style == HouseStyle.Burnt;
            string wallMat = style == HouseStyle.Burnt ? "Charred" : thatch ? "EarthWall" : "Plaster";
            string pillarMat = palace ? "PillarRed" : style == HouseStyle.Burnt ? "Charred" : "Timber";

            if (baseHeight > 0f)
                Box("기단", t, new Vector3(0f, baseHeight * 0.5f, 0f), new Vector3(w + 1.4f, baseHeight, d + 1.4f), palace ? "Stone" : "StoneDark", default, O, true);

            float y0 = baseHeight;
            Box("벽", t, new Vector3(0f, y0 + h * 0.5f, 0f), new Vector3(w - 0.3f, h, d - 0.3f), wallMat, default, O, true);

            int cols = Mathf.Max(2, Mathf.RoundToInt(w / 3f) + 1);
            for (int i = 0; i < cols; i++)
            {
                float x = Mathf.Lerp(-w * 0.5f, w * 0.5f, i / (float)(cols - 1));
                Cyl("기둥", t, new Vector3(x, y0 + h * 0.5f, d * 0.5f), new Vector3(0.36f, h * 0.5f, 0.36f), pillarMat, default, OThin);
                Cyl("기둥", t, new Vector3(x, y0 + h * 0.5f, -d * 0.5f), new Vector3(0.36f, h * 0.5f, 0.36f), pillarMat, default, OThin);

                if (i < cols - 1 && !thatch)
                {
                    float cx = Mathf.Lerp(-w * 0.5f, w * 0.5f, (i + 0.5f) / (cols - 1));
                    float span = w / (cols - 1) - 0.8f;
                    Box("창호", t, new Vector3(cx, y0 + h * 0.45f, d * 0.5f - 0.1f), new Vector3(span, h * 0.72f, 0.08f), "Timber", default, OThin);
                    Box("창호지", t, new Vector3(cx, y0 + h * 0.45f, d * 0.5f - 0.05f), new Vector3(span * 0.86f, h * 0.62f, 0.04f), "Paper", default, NoLine);
                }
            }

            if (palace)
                Box("단청", t, new Vector3(0f, y0 + h + 0.18f, 0f), new Vector3(w + 0.5f, 0.36f, d + 0.5f), "Dancheong", default, OThin);

            float roofY = y0 + h + (palace ? 0.36f : 0.1f);

            if (thatch)
            {
                Ball("초가지붕", t, new Vector3(0f, roofY + 0.25f, 0f), new Vector3(w + 0.9f, 1.7f, d + 0.9f), style == HouseStyle.Burnt ? "Charred" : "Thatch", default, O);
                return t;
            }

            float lift = palace ? 1.1f : style == HouseStyle.Office ? 0.7f : 0.45f;
            float roofH = palace ? h * 0.75f : h * 0.6f;
            float over = palace ? 3.4f : style == HouseStyle.Office ? 2.8f : 1.9f;
            MeshObj("지붕", t, Roof(w + over, d + over, roofH, lift), new Vector3(0f, roofY, 0f), Vector3.zero, Vector3.one, "RoofTile", O);
            Box("용마루", t, new Vector3(0f, roofY + roofH - 0.05f, 0f), new Vector3(Mathf.Max(1f, w - d * 0.8f), 0.38f, 0.5f), "Ridge", default, OThin);

            if (doubleRoof)
            {
                float y2 = roofY + roofH * 0.55f;
                Box("상층벽", t, new Vector3(0f, y2 + 1.1f, 0f), new Vector3(w * 0.72f, 2.2f, d * 0.62f), "Plaster", default, O);
                Box("상층단청", t, new Vector3(0f, y2 + 2.35f, 0f), new Vector3(w * 0.74f, 0.3f, d * 0.64f), "Dancheong", default, OThin);
                float h2 = roofH * 0.85f;
                MeshObj("상층지붕", t, Roof(w * 0.72f + 3.4f, d * 0.62f + 3.2f, h2, lift * 1.1f), new Vector3(0f, y2 + 2.5f, 0f), Vector3.zero, Vector3.one, "RoofTile", O);
                Box("상층용마루", t, new Vector3(0f, y2 + 2.5f + h2 - 0.05f, 0f), new Vector3(Mathf.Max(1f, w * 0.72f - d * 0.5f), 0.42f, 0.55f), "Ridge", default, OThin);
            }

            return t;
        }

        /// <summary>궁장(담장): 석축 + 회벽 + 기와 한 줄.</summary>
        private static void Wall(Transform parent, Vector3 a, Vector3 b, float height = 5f)
        {
            var dir = b - a;
            float len = dir.magnitude;
            var t = Node("담장", parent, (a + b) * 0.5f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg);

            Box("석축", t, new Vector3(0f, 0.8f, 0f), new Vector3(1.4f, 1.6f, len), "StoneDark", default, O, true);
            Box("회벽", t, new Vector3(0f, 1.6f + (height - 2.4f) * 0.5f, 0f), new Vector3(1.1f, height - 2.4f, len), "Plaster", default, O, true);
            MeshObj("담장기와", t, Roof(Mathf.Max(2.5f, Mathf.Round(len + 0.6f)), 2.4f, 0.9f, 0.3f), new Vector3(0f, height - 0.8f, 0f), new Vector3(0f, 90f, 0f), Vector3.one, "RoofTile", O);
        }

        // ── 대기 / 지면 ───────────────────────────────────────

        private static void BuildAtmosphere(Transform root)
        {
            var t = Group("00_대기_한기와_정지된_시간", root);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = FogFrozen;
            RenderSettings.fogStartDistance = 40f;
            RenderSettings.fogEndDistance = 900f;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);
            RenderSettings.skybox = null;

            var sunGo = new GameObject("한기 어린 햇빛 (Directional)");
            sunGo.transform.SetParent(t, false);
            sunGo.transform.rotation = Quaternion.Euler(34f, -128f, 0f);
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.90f, 0.94f, 1f);
            sun.intensity = 1.15f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.75f;
            RenderSettings.sun = sun;

            // 전역 후처리: 채도를 눌러 수묵 느낌, 비네트, 전등/신핵 번짐, 한지 결
            string profilePath = $"{MatDir}/VP_HyeonmuWorld.asset";
            AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);

            var ca = profile.Add<ColorAdjustments>(true);
            ca.saturation.Override(-12f);
            ca.contrast.Override(18f);
            ca.postExposure.Override(0.15f);
            ca.colorFilter.Override(new Color(0.97f, 0.985f, 1f));

            var vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.34f);
            vignette.smoothness.Override(0.55f);
            vignette.color.Override(new Color(0.09f, 0.09f, 0.11f));

            var bloom = profile.Add<Bloom>(true);
            bloom.threshold.Override(0.95f);
            bloom.intensity.Override(0.8f);
            bloom.scatter.Override(0.6f);

            var grain = profile.Add<FilmGrain>(true);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.14f);
            grain.response.Override(0.6f);

            foreach (var c in profile.components)
            {
                c.hideFlags = HideFlags.HideInInspector | HideFlags.HideInHierarchy;
                AssetDatabase.AddObjectToAsset(c, profile);
            }
            EditorUtility.SetDirty(profile);

            var volGo = new GameObject("Global Volume (수묵 후처리)");
            volGo.transform.SetParent(t, false);
            var volume = volGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.sharedProfile = profile;

            // 허공에 멈춘 눈: 속도 0, 중력 0, 수명 무한 → 시간이 멈춘 도성
            var snowGo = new GameObject("멈춘 눈송이 (정지된 시간)");
            snowGo.transform.SetParent(t, false);
            snowGo.transform.localPosition = new Vector3(20f, 38f, 60f);
            var ps = snowGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.duration = 5f;
            main.startLifetime = 100000f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.24f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.95f), new Color(0.85f, 0.9f, 0.95f, 0.7f));
            main.gravityModifier = 0f;
            main.maxParticles = 26000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = true;
            var emission = ps.emission;
            emission.rateOverTime = 26000f / 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(560f, 76f, 620f);
            var psr = snowGo.GetComponent<ParticleSystemRenderer>();
            psr.sharedMaterial = Mat("FX_Snow");
            psr.renderMode = ParticleSystemRenderMode.Billboard;
            psr.maxParticleSize = 0.05f;

            // 가까운 곳의 굵은 눈
            var nearGo = Object.Instantiate(snowGo, t);
            nearGo.name = "멈춘 눈송이 (근경, 굵은 눈)";
            nearGo.transform.localPosition = new Vector3(0f, 10f, -20f);
            var nps = nearGo.GetComponent<ParticleSystem>();
            var nmain = nps.main;
            nmain.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.35f);
            nmain.maxParticles = 12000;
            var nemission = nps.emission;
            nemission.rateOverTime = 12000f / 5f;
            var nshape = nps.shape;
            nshape.scale = new Vector3(220f, 20f, 360f);

            // 산자락과 연못에 깔린 먹 안개
            Mist(t, "북악산 산허리 안개", new Vector3(0f, 22f, 330f), 330f, "FX_Mist");
            Mist(t, "북악산 산허리 안개 2", new Vector3(-40f, 45f, 380f), 300f, "FX_Mist");
            Mist(t, "인왕산 안개", new Vector3(-280f, 18f, 80f), 220f, "FX_Mist");
            Mist(t, "도성 바닥 안개", new Vector3(30f, 1.2f, -60f), 190f, "FX_Mist");
            Mist(t, "현무 발치의 먹안개", new Vector3(10f, 3f, 200f), 120f, "FX_MistInk");
        }

        private static void Mist(Transform parent, string name, Vector3 pos, float radius, string mat)
        {
            MeshObj(name, parent, Disc, pos, Vector3.zero, new Vector3(radius, 1f, radius), mat, null, false);
        }

        private static void BuildGround(Transform root)
        {
            var t = Group("00_지면", root);
            Box("눈 덮인 지면", t, new Vector3(0f, -0.5f, 60f), new Vector3(1000f, 1f, 1000f), "SnowGround", default, NoLine, true);
            Box("궁궐 박석 마당", t, new Vector3(0f, 0.04f, 10f), new Vector3(60f, 0.08f, 96f), "Paving", default, NoLine);
            Box("어도(御道)", t, new Vector3(0f, 0.1f, 5f), new Vector3(4f, 0.06f, 80f), "Stone", default, NoLine);
            Box("육조거리 길", t, new Vector3(0f, 0.03f, -88f), new Vector3(26f, 0.06f, 80f), "Road", default, NoLine);
            Box("운종가 길", t, new Vector3(82f, 0.03f, -125f), new Vector3(140f, 0.06f, 12f), "Road", default, NoLine);
            Box("삼청동 길", t, new Vector3(92f, 0.03f, 60f), new Vector3(10f, 0.06f, 110f), "Road", default, NoLine);

            // 신맥(神脈): 발전기에서 현무 쪽으로 땅이 갈라져 푸른 빛이 샌다
            var fissure = Group("신맥 균열 (神脈)", t);
            var p = new Vector3(-28f, 0.06f, 112f);
            var end = new Vector3(8f, 0.06f, 178f);
            for (int i = 0; i < 26; i++)
            {
                float u = (i + 1) / 26f;
                var next = Vector3.Lerp(new Vector3(-28f, 0.06f, 112f), end, u) + new Vector3(R(-3f, 3f), 0f, R(-1.5f, 1.5f));
                var seg = next - p;
                MeshObj("균열", fissure, Disc, (p + next) * 0.5f, new Vector3(0f, Mathf.Atan2(seg.x, seg.z) * Mathf.Rad2Deg, 0f),
                    new Vector3(R(0.6f, 1.3f), 1f, seg.magnitude * 0.6f), "FX_Sinmaek", null, false);
                Box("균열 틈", fissure, (p + next) * 0.5f, new Vector3(0.25f, 0.05f, seg.magnitude), "Ink",
                    new Vector3(0f, Mathf.Atan2(seg.x, seg.z) * Mathf.Rad2Deg, 0f), null);
                p = next;
            }
        }

        // ── 경복궁 궁성 ───────────────────────────────────────

        private static void BuildPalaceWalls(Transform t)
        {
            // 남쪽(광화문 좌우)
            Wall(t, new Vector3(-70f, 0f, -40f), new Vector3(-17f, 0f, -40f));
            Wall(t, new Vector3(17f, 0f, -40f), new Vector3(70f, 0f, -40f));
            // 동·서 (건춘문/영추문 자리 비움)
            Wall(t, new Vector3(70f, 0f, -40f), new Vector3(70f, 0f, 52f));
            Wall(t, new Vector3(70f, 0f, 68f), new Vector3(70f, 0f, 150f));
            Wall(t, new Vector3(-70f, 0f, -40f), new Vector3(-70f, 0f, 52f));
            Wall(t, new Vector3(-70f, 0f, 68f), new Vector3(-70f, 0f, 150f));
            // 북쪽: 현무 쪽 담장이 무너졌다
            Wall(t, new Vector3(-70f, 0f, 150f), new Vector3(-8f, 0f, 150f));
            Wall(t, new Vector3(8f, 0f, 150f), new Vector3(26f, 0f, 150f));
            Wall(t, new Vector3(48f, 0f, 150f), new Vector3(70f, 0f, 150f));

            var rubble = Group("무너진 북장 (현무의 꼬리 자국)", t);
            for (int i = 0; i < 22; i++)
            {
                var pos = new Vector3(R(26f, 48f), R(0.3f, 1.5f), 150f + R(-4f, 6f));
                Box("잔해", rubble, pos, new Vector3(R(0.8f, 2.6f), R(0.5f, 1.4f), R(0.8f, 2.4f)), i % 3 == 0 ? "RoofTile" : "StoneDark",
                    new Vector3(R(-30f, 30f), R(0f, 180f), R(-30f, 30f)), O);
            }

            // 동·서 문루
            Hanok("건춘문(동문)", t, new Vector3(70f, 4.2f, 60f), 90f, 16f, 6f, 3.2f, HouseStyle.Palace);
            Box("건춘문 석축", t, new Vector3(70f, 2.1f, 60f), new Vector3(8f, 4.2f, 18f), "Stone", default, O, true);
            Hanok("영추문(서문)", t, new Vector3(-70f, 4.2f, 60f), 90f, 16f, 6f, 3.2f, HouseStyle.Palace);
            Box("영추문 석축", t, new Vector3(-70f, 2.1f, 60f), new Vector3(8f, 4.2f, 18f), "Stone", default, O, true);
        }

        private static void BuildGwanghwamun(Transform t)
        {
            var g = Node("광화문 (光化門)", t, new Vector3(0f, 0f, -40f));

            // 석축 육축과 세 홍예문(무지개문)
            float[] piers = { -17f, -11.25f, -6.75f, -2.25f, 2.25f, 6.75f, 11.25f, 17f };
            for (int i = 0; i < piers.Length; i += 2)
            {
                float a = piers[i], b = piers[i + 1];
                Box("육축", g, new Vector3((a + b) * 0.5f, 3f, 0f), new Vector3(b - a, 6f, 12f), "Stone", default, O, true);
            }

            Box("육축 상부", g, new Vector3(0f, 7.5f, 0f), new Vector3(34f, 3f, 12f), "Stone", default, O, true);
            foreach (float x in new[] { -9f, 0f, 9f })
            {
                var arch = Cyl("홍예 그늘", g, new Vector3(x, 6f, 0f), new Vector3(4.5f, 6.02f, 4.5f), "Ink", new Vector3(90f, 0f, 0f), NoLine);
                arch.transform.localScale = new Vector3(4.5f, 6.02f, 2.6f);
            }

            Box("여장", g, new Vector3(0f, 9.4f, 0f), new Vector3(34.4f, 0.8f, 12.4f), "StoneDark", default, O);

            // 2층 문루
            var tower = Hanok("문루", g, new Vector3(0f, 9.8f, 0f), 0f, 24f, 8f, 3.6f, HouseStyle.Palace, true, 0f);
            Box("현판", tower, new Vector3(0f, 7.2f, 4.9f), new Vector3(3.6f, 1.3f, 0.2f), "Ink", default, OThin);
            Box("현판 테", tower, new Vector3(0f, 7.2f, 4.85f), new Vector3(3.9f, 1.55f, 0.1f), "GlowYellow", default, NoLine);

            // 해태 한 쌍
            foreach (float x in new[] { -12f, 12f })
            {
                var h = Node("해태", t, new Vector3(x, 0f, -58f), 180f);
                Box("대좌", h, new Vector3(0f, 0.8f, 0f), new Vector3(2.6f, 1.6f, 3.6f), "Stone", default, O, true);
                Ball("몸", h, new Vector3(0f, 2.35f, -0.2f), new Vector3(1.7f, 1.5f, 2.9f), "Stone", default, O);
                Ball("가슴", h, new Vector3(0f, 2.8f, 0.9f), new Vector3(1.5f, 1.7f, 1.4f), "Stone", default, O);
                Ball("머리", h, new Vector3(0f, 3.65f, 1.55f), new Vector3(1.3f, 1.15f, 1.35f), "Stone", default, O);
                Ball("갈기", h, new Vector3(0f, 3.75f, 1.1f), new Vector3(1.6f, 0.9f, 0.9f), "StoneDark", default, O);
                Box("뿔", h, new Vector3(0f, 4.35f, 1.6f), new Vector3(0.22f, 0.6f, 0.22f), "Stone", new Vector3(-25f, 0f, 0f), O);
                foreach (var leg in new[] { new Vector3(-0.55f, 1.9f, 1.1f), new Vector3(0.55f, 1.9f, 1.1f), new Vector3(-0.6f, 1.9f, -1.2f), new Vector3(0.6f, 1.9f, -1.2f) })
                    Cyl("다리", h, leg, new Vector3(0.45f, 0.35f, 0.45f), "Stone", default, OThin);
                AddIce(h, new Vector3(0f, 1.6f, 0f), 2.2f, 5);
            }
        }

        private static void BuildGeunjeongjeon(Transform t)
        {
            var g = Node("근정전 (勤政殿)", t, new Vector3(0f, 0f, 56f));
            Box("월대 하층", g, new Vector3(0f, 0.6f, 0f), new Vector3(46f, 1.2f, 36f), "Stone", default, O, true);
            Box("월대 상층", g, new Vector3(0f, 1.8f, 0f), new Vector3(38f, 1.2f, 28f), "Stone", default, O, true);
            Box("답도", g, new Vector3(0f, 1.2f, -17f), new Vector3(5f, 2.4f, 4f), "StoneDark", new Vector3(-25f, 0f, 0f), O, true);

            for (int i = 0; i < 20; i++)
            {
                float x = Mathf.Lerp(-18.5f, 18.5f, i / 19f);
                Box("난간석", g, new Vector3(x, 2.8f, -13.8f), new Vector3(0.35f, 0.8f, 0.35f), "Stone", default, OThin);
            }

            Hanok("정전", g, new Vector3(0f, 2.4f, 2f), 180f, 28f, 16f, 7f, HouseStyle.Palace, true, 0f);

            // 품계석
            var pg = Group("품계석", t);
            for (int i = 0; i < 9; i++)
            {
                float z = Mathf.Lerp(10f, 34f, i / 8f);
                Box("품계석", pg, new Vector3(-6f, 0.45f, z), new Vector3(0.5f, 0.9f, 0.3f), "Stone", default, OThin);
                Box("품계석", pg, new Vector3(6f, 0.45f, z), new Vector3(0.5f, 0.9f, 0.3f), "Stone", default, OThin);
            }

            // 좌우 행각
            Hanok("동행각", t, new Vector3(28f, 0f, 35f), -90f, 50f, 5f, 3.2f, HouseStyle.Office);
            Hanok("서행각", t, new Vector3(-28f, 0f, 35f), 90f, 50f, 5f, 3.2f, HouseStyle.Office);
        }

        // ── 도성 ───────────────────────────────────────────────

        private static void BuildYukjo(Transform t)
        {
            string[] names = { "의정부", "이조", "호조", "예조", "병조", "형조", "공조", "한성부" };
            for (int i = 0; i < 4; i++)
            {
                float z = -60f - i * 17f;
                Hanok($"{names[i * 2]} 관아", t, new Vector3(-26f, 0f, z), 90f, 14f, 8f, 3.4f, HouseStyle.Office);
                Hanok($"{names[i * 2 + 1]} 관아", t, new Vector3(26f, 0f, z), -90f, 14f, 8f, 3.4f, HouseStyle.Office);
            }

            // 거리의 괘서와 버려진 수레
            Placard(t, new Vector3(-22.05f, 2.2f, -62f), 90f);
            Placard(t, new Vector3(22.05f, 2.4f, -80f), -90f);
            Placard(t, new Vector3(-22.05f, 2.0f, -98f), 90f);
            Cart(t, new Vector3(6f, 0f, -72f), 30f);
            Cart(t, new Vector3(-7f, 0f, -104f), -60f);
        }

        private static void BuildUnjongga(Transform t)
        {
            string[] awnings = { "AwningIndigo", "AwningOchre", "AwningRust", "AwningHemp" };
            int n = 0;
            for (float x = 20f; x < 140f; x += 10f)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    n++;
                    float z = -125f + side * 12f;
                    float yaw = side > 0 ? 180f : 0f;
                    bool burnt = x > 118f && side > 0;
                    var style = burnt ? HouseStyle.Burnt : (n % 3 == 0 ? HouseStyle.Thatch : HouseStyle.Shop);
                    var shop = Hanok($"시전 {n:00}", t, new Vector3(x, 0f, z), yaw, 8f, 6f, 2.8f, style, false, 0.4f);

                    if (burnt)
                    {
                        FrozenFire(shop, new Vector3(0f, 3.6f, 0f));
                        FrozenSmoke(shop, new Vector3(0.5f, 5f, 0f), 7, 1.8f);
                        continue;
                    }

                    var aw = awnings[n % awnings.Length];
                    Box("차양", shop, new Vector3(0f, 2.9f, 4.2f), new Vector3(7.4f, 0.12f, 2.6f), aw, new Vector3(-16f, 0f, 0f), OThin);
                    Cyl("차양대", shop, new Vector3(-3.4f, 1.3f, 5.3f), new Vector3(0.12f, 1.3f, 0.12f), "Timber", default, OThin);
                    Cyl("차양대", shop, new Vector3(3.4f, 1.3f, 5.3f), new Vector3(0.12f, 1.3f, 0.12f), "Timber", default, OThin);

                    // 좌판: 항아리, 볏섬
                    Box("좌판", shop, new Vector3(0f, 0.55f, 4.6f), new Vector3(3.2f, 0.12f, 1.4f), "Timber", default, OThin);
                    for (int j = 0; j < 3; j++)
                        Ball("옹기", shop, new Vector3(-1.1f + j * 1.1f, 0.95f, 4.6f), new Vector3(0.6f, 0.7f, 0.6f), "EarthWall", default, OThin);
                    if (n % 2 == 0)
                    {
                        Cyl("볏섬", shop, new Vector3(-3.2f, 0.45f, 3.9f), new Vector3(0.9f, 0.5f, 0.9f), "Thatch", new Vector3(0f, 0f, 90f), OThin);
                        Cyl("볏섬", shop, new Vector3(-3.2f, 1.2f, 3.9f), new Vector3(0.9f, 0.5f, 0.9f), "Thatch", new Vector3(0f, 0f, 90f), OThin);
                    }

                    if (n % 2 == 1) Placard(shop, new Vector3(1.5f, 1.9f, 2.97f), 0f);
                }
            }

            // 흑수회(黑水會) 제단: 현무를 수호신으로 섬기며 도성을 얼리자 외치는 사교 집단
            var altar = Node("흑수회 제단", t, new Vector3(104f, 0f, -125f));
            Cyl("흑수 수반", altar, new Vector3(0f, 0.6f, 0f), new Vector3(3.2f, 0.6f, 3.2f), "StoneDark", default, O);
            Cyl("검은 물", altar, new Vector3(0f, 1.22f, 0f), new Vector3(2.8f, 0.02f, 2.8f), "Hyeonmu", default, NoLine);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + 45f;
                var pos = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 3.2f);
                BlackBanner(altar, pos, a);
            }
        }

        private static void BuildGeoncheonggung(Transform t)
        {
            var g = Node("건청궁 (乾淸宮)", t, new Vector3(0f, 0f, 137f));
            Wall(g, new Vector3(-26f, 0f, -11f), new Vector3(-3f, 0f, -11f), 3.6f);
            Wall(g, new Vector3(3f, 0f, -11f), new Vector3(26f, 0f, -11f), 3.6f);
            Wall(g, new Vector3(-26f, 0f, -11f), new Vector3(-26f, 0f, 11f), 3.6f);
            Wall(g, new Vector3(26f, 0f, -11f), new Vector3(26f, 0f, 11f), 3.6f);

            // 건청궁은 단청 없이 사대부가처럼 소박하게 지었다
            Hanok("장안당", g, new Vector3(-11f, 0f, 4f), 180f, 13f, 7f, 3.2f, HouseStyle.Office);
            Hanok("곤녕합", g, new Vector3(11f, 0f, 4f), 180f, 11f, 7f, 3.2f, HouseStyle.Office);
            Hanok("관문각", g, new Vector3(0f, 0f, 7.5f), 180f, 7f, 5f, 3f, HouseStyle.Office);

            // 조선 최초의 전등 — 켜진 그대로 얼어붙었다
            var lamps = Group("조선 최초의 전등 (얼어붙은 채 켜져 있다)", t);
            var lampPositions = new List<Vector3>
            {
                new Vector3(-8f, 0f, 130f), new Vector3(8f, 0f, 130f), new Vector3(-18f, 0f, 134f), new Vector3(18f, 0f, 134f),
                new Vector3(-6f, 0f, 140.8f), new Vector3(6f, 0f, 140.8f),
                new Vector3(-23f, 0f, 114f), new Vector3(23f, 0f, 114f), new Vector3(-22f, 0f, 96f), new Vector3(22f, 0f, 96f),
                new Vector3(-14f, 0f, 82f), new Vector3(14f, 0f, 82f),
            };

            Vector3? prevTop = null;
            foreach (var p in lampPositions)
            {
                var top = LampPost(lamps, p);
                if (prevTop.HasValue && Vector3.Distance(prevTop.Value, top) < 26f)
                    Pipe("전선", lamps, prevTop.Value, top, 0.035f, "Ink");
                prevTop = top;
            }

            // 물불 발전기: 향원정 연못물을 끌어올려 돌리던 증기 발전기 — 과부하로 폭주한 모습
            var gen = Node("물불 증기 발전기 (폭주 후 정지)", t, new Vector3(-34f, 0f, 116f), 35f);
            Box("기초", gen, new Vector3(0f, 0.4f, 0f), new Vector3(12f, 0.8f, 8f), "StoneDark", default, O, true);
            Cyl("보일러 동체", gen, new Vector3(0f, 2.6f, 0f), new Vector3(3.4f, 4f, 3.4f), "Iron", new Vector3(0f, 0f, 90f), O);
            Cyl("보일러 테", gen, new Vector3(-2f, 2.6f, 0f), new Vector3(3.6f, 0.15f, 3.6f), "Brass", new Vector3(0f, 0f, 90f), OThin);
            Cyl("보일러 테", gen, new Vector3(2f, 2.6f, 0f), new Vector3(3.6f, 0.15f, 3.6f), "Brass", new Vector3(0f, 0f, 90f), OThin);
            Cyl("굴뚝", gen, new Vector3(-3f, 7f, 0f), new Vector3(1f, 4f, 1f), "Iron", default, O);
            Cyl("플라이휠", gen, new Vector3(4.6f, 3f, 2.6f), new Vector3(5.2f, 0.25f, 5.2f), "Brass", new Vector3(90f, 0f, 0f), O);
            for (int i = 0; i < 4; i++)
                Box("살", gen, new Vector3(4.6f, 3f, 2.6f), new Vector3(0.25f, 5f, 0.2f), "Iron", new Vector3(0f, 0f, i * 45f), OThin);
            Box("발전기 함", gen, new Vector3(4.6f, 1.5f, -1.6f), new Vector3(2.4f, 2.2f, 2.4f), "Brass", default, O);
            Box("찢어진 보일러판", gen, new Vector3(0.6f, 4.4f, 1.4f), new Vector3(1.8f, 0.1f, 1.2f), "Iron", new Vector3(-50f, 20f, 30f), OThin);
            FrozenSmoke(gen, new Vector3(-3f, 11f, 0f), 9, 2.2f);
            FrozenSparks(gen, new Vector3(0.6f, 4.4f, 1.4f));

            // 연못으로 이어진 취수관
            Pipe("취수관", t, new Vector3(-30f, 0.6f, 112f), new Vector3(-18f, 0.6f, 104f), 0.45f, "Iron", OThin);
            Pipe("취수관", t, new Vector3(-30f, 0.6f, 113f), new Vector3(-30f, 2f, 115f), 0.45f, "Iron", OThin);
            Pipe("송전선", t, new Vector3(-30f, 5f, 118f), lampPositions[6] + Vector3.up * 4.9f, 0.035f, "Ink");
        }

        private static void BuildHyangwonjeong(Transform t)
        {
            var center = new Vector3(0f, 0f, 104f);
            var g = Node("향원정 (香遠亭)", t, center);

            MeshObj("얼어붙은 연못", g, Disc, new Vector3(0f, 0.05f, 0f), Vector3.zero, new Vector3(21f, 1f, 21f), "Ice", null);
            MeshObj("연못 호안석", g, SaveMesh("PondRim", () => WorldMeshFactory.Ring(0.95f, 1.05f, 72)), new Vector3(0f, 0.25f, 0f), Vector3.zero, new Vector3(21f, 1f, 21f), "StoneDark", O);

            for (int i = 0; i < 30; i++)
            {
                float a = R(0f, 360f);
                float r = R(6f, 19f);
                var pos = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.08f, r);
                Box("얼음 금", g, pos, new Vector3(0.07f, 0.02f, R(1.2f, 4f)), "Ink", new Vector3(0f, a + R(-50f, 50f), 0f), NoLine);
            }

            Cyl("섬", g, new Vector3(0f, 0.4f, 0f), new Vector3(10f, 0.4f, 10f), "SnowGround", default, O);
            MeshObj("육모 기단", g, Hex, new Vector3(0f, 0.8f, 0f), Vector3.zero, new Vector3(3.3f, 0.8f, 3.3f), "Stone", O);

            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f + 30f;
                var pos = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 2.7f);
                Cyl("1층 기둥", g, pos + Vector3.up * 3.1f, new Vector3(0.3f, 1.5f, 0.3f), "PillarRed", default, OThin);
                Cyl("2층 기둥", g, pos * 0.8f + Vector3.up * 6.1f, new Vector3(0.24f, 1.1f, 0.24f), "PillarRed", default, OThin);
            }

            MeshObj("1층 난간마루", g, Hex, new Vector3(0f, 4.6f, 0f), Vector3.zero, new Vector3(3.4f, 0.35f, 3.4f), "Timber", O);
            MeshObj("1층 육모지붕", g, SaveMesh("PavilionRoofLow", () => WorldMeshFactory.PavilionRoof(6, 4.6f, 1.1f, 0.8f)),
                new Vector3(0f, 4.6f, 0f), new Vector3(0f, 30f, 0f), Vector3.one, "RoofTile", O);
            MeshObj("2층 벽", g, Hex, new Vector3(0f, 5.2f, 0f), Vector3.zero, new Vector3(2f, 1.9f, 2f), "Plaster", O);
            MeshObj("2층 단청", g, Hex, new Vector3(0f, 7.1f, 0f), Vector3.zero, new Vector3(2.35f, 0.3f, 2.35f), "Dancheong", OThin);
            MeshObj("2층 육모지붕", g, SaveMesh("PavilionRoofTop", () => WorldMeshFactory.PavilionRoof(6, 4f, 3.1f, 0.9f)),
                new Vector3(0f, 7.3f, 0f), new Vector3(0f, 30f, 0f), Vector3.one, "RoofTile", O);
            Ball("절병통", g, new Vector3(0f, 10.6f, 0f), new Vector3(0.5f, 0.7f, 0.5f), "Ridge", default, OThin);

            // 취향교 (건청궁으로 이어지는 나무 다리)
            var bridge = Group("취향교", g);
            for (int i = 0; i < 10; i++)
            {
                float u = i / 9f;
                float z = Mathf.Lerp(4.6f, 21.5f, u);
                float y = 0.6f + Mathf.Sin(u * Mathf.PI) * 1.1f;
                float slope = Mathf.Cos(u * Mathf.PI) * -8f;
                Box("다리판", bridge, new Vector3(0f, y, z), new Vector3(2.6f, 0.18f, 1.95f), "Timber", new Vector3(slope, 0f, 0f), OThin, true);
                Box("난간", bridge, new Vector3(-1.3f, y + 0.55f, z), new Vector3(0.1f, 0.1f, 1.95f), "PillarRed", new Vector3(slope, 0f, 0f), NoLine);
                Box("난간", bridge, new Vector3(1.3f, y + 0.55f, z), new Vector3(0.1f, 0.1f, 1.95f), "PillarRed", new Vector3(slope, 0f, 0f), NoLine);
                Box("난간동자", bridge, new Vector3(-1.3f, y + 0.3f, z), new Vector3(0.1f, 0.5f, 0.1f), "PillarRed", default, NoLine);
                Box("난간동자", bridge, new Vector3(1.3f, y + 0.3f, z), new Vector3(0.1f, 0.5f, 0.1f), "PillarRed", default, NoLine);
            }

            Mist(t, "연못 물안개", center + Vector3.up * 1.4f, 26f, "FX_Mist");
        }

        private static void BuildGigichang(Transform t)
        {
            var g = Node("기기창 (機器廠)", t, new Vector3(112f, 0f, 108f));

            Box("공장 마당", g, new Vector3(0f, 0.04f, 0f), new Vector3(46f, 0.08f, 44f), "Road", default, NoLine);
            Brick(g, "주조창", new Vector3(-8f, 0f, -10f), 0f, 26f, 12f, 7f);
            Brick(g, "기계창", new Vector3(12f, 0f, 8f), 90f, 20f, 10f, 6f);
            Brick(g, "화약고", new Vector3(-14f, 0f, 12f), 0f, 10f, 8f, 4.5f);

            foreach (var p in new[] { new Vector3(-16f, 0f, -18f), new Vector3(-2f, 0f, -18f), new Vector3(18f, 0f, 16f) })
            {
                Cyl("벽돌 굴뚝", g, p + Vector3.up * 9f, new Vector3(1.6f, 9f, 1.6f), "Brick", default, O);
                Cyl("굴뚝 테", g, p + Vector3.up * 17.6f, new Vector3(1.9f, 0.3f, 1.9f), "Iron", default, OThin);
                FrozenSmoke(g, p + Vector3.up * 19f, 10, 2.4f);
            }

            // 톱니바퀴와 증기관
            Gear(g, new Vector3(4f, 3.2f, -3f), 3f, new Vector3(90f, 0f, 0f));
            Gear(g, new Vector3(7.6f, 2.2f, -3f), 1.9f, new Vector3(90f, 0f, 10f));
            Gear(g, new Vector3(-20f, 4f, 2f), 2.5f, new Vector3(0f, 0f, 90f));
            Pipe("증기관", g, new Vector3(-8f, 5f, -4f), new Vector3(4f, 5f, -4f), 0.35f, "Brass", OThin);
            Pipe("증기관", g, new Vector3(4f, 5f, -4f), new Vector3(4f, 1f, 4f), 0.35f, "Brass", OThin);

            // 증기 압축 연발 포대 — 얼어붙은 채 현무를 겨누고 있다
            var hyeonmu = new Vector3(10f, 50f, 200f);
            var battery = Node("증기 압축 연발 포대", t, new Vector3(100f, 0f, 134f));
            var dir = hyeonmu - battery.position;
            battery.localRotation = Quaternion.Euler(0f, Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg, 0f);
            Box("포대 석축", battery, new Vector3(0f, 1f, 0f), new Vector3(11f, 2f, 9f), "StoneDark", default, O, true);
            Cyl("압축 보일러", battery, new Vector3(0f, 3.6f, -2.2f), new Vector3(2.8f, 2.6f, 2.8f), "Iron", new Vector3(0f, 0f, 90f), O);
            Cyl("회전대", battery, new Vector3(0f, 2.3f, 1f), new Vector3(5f, 0.3f, 5f), "Brass", default, O);
            var rack = Node("발사틀", battery, new Vector3(0f, 3.4f, 1f));
            rack.localRotation = Quaternion.Euler(-32f, 0f, 0f);
            for (int row = 0; row < 2; row++)
                for (int col = 0; col < 4; col++)
                {
                    var p = new Vector3(-1.5f + col * 1f, row * 0.9f, 0f);
                    Cyl("포신", rack, p + Vector3.forward * 1.8f, new Vector3(0.55f, 2.2f, 0.55f), "Iron", new Vector3(90f, 0f, 0f), OThin);
                    Cyl("포구 테", rack, p + Vector3.forward * 4f, new Vector3(0.7f, 0.12f, 0.7f), "Brass", new Vector3(90f, 0f, 0f), NoLine);
                }
            Box("발사틀 몸체", rack, new Vector3(0f, 0.45f, 0.6f), new Vector3(4.6f, 2.2f, 2.2f), "Brass", default, O);
            FrozenSmoke(battery, new Vector3(0f, 6f, -2.2f), 5, 1.4f);
        }

        // ── 현무 ───────────────────────────────────────────────

        private static void BuildHyeonmu(Transform t)
        {
            var root = Node("현무 (玄武) — 열기와 시간을 삼키는 신수", t, new Vector3(10f, 0f, 200f), 180f);
            root.localScale = Vector3.one * 1.5f; // 궁궐을 내려다보는 크기

            var bodyGo = Ball("흑철 몸통", root, new Vector3(0f, 8f, 0f), new Vector3(40f, 18f, 52f), "Hyeonmu", default, OThick);
            bodyGo.AddComponent<SphereCollider>();

            // 등껍질 가갑: 타원체 표면에 육각판을 깐다. 두 장은 떨어져 나가 톱니가 드러났다.
            var shell = Group("흑철 가갑", root);
            const float Rx = 20f, Ry = 9f, Rz = 26f, Cy = 8f;
            int k = 0;
            for (int ix = -2; ix <= 2; ix++)
            {
                for (int iz = -2; iz <= 2; iz++)
                {
                    float x = ix * 7.2f + (iz % 2 == 0 ? 0f : 3.6f);
                    float z = iz * 9.5f;
                    float q = 1f - (x * x) / (Rx * Rx) - (z * z) / (Rz * Rz);
                    if (q < 0.18f) continue;

                    float y = Cy + Ry * Mathf.Sqrt(q);
                    var normal = new Vector3(x / (Rx * Rx), (y - Cy) / (Ry * Ry), z / (Rz * Rz)).normalized;
                    var rot = Quaternion.FromToRotation(Vector3.up, normal).eulerAngles;
                    k++;

                    bool missing = k == 4 || k == 11;
                    if (missing)
                    {
                        Gear(shell, new Vector3(x, y - 0.6f, z), 3.2f, rot);
                        FrozenSmoke(shell, new Vector3(x, y + 1.5f, z), 4, 1.6f);
                        continue;
                    }

                    MeshObj($"가갑 {k:00}", shell, Hex, new Vector3(x, y - 0.7f, z), rot, new Vector3(4.4f, 1.6f, 4.4f), "HyeonmuPlate", OThick);
                    if (k % 3 == 0)
                        Cyl("리벳 테", shell, new Vector3(x, y + 0.95f, z) + normal * 0.1f, new Vector3(2f, 0.08f, 2f), "Brass", rot, NoLine);
                }
            }

            // 등의 증기 굴뚝
            foreach (var p in new[] { new Vector3(-8f, 15f, -12f), new Vector3(8f, 15f, -12f), new Vector3(0f, 16f, -18f) })
            {
                Cyl("등 굴뚝", root, p + Vector3.up * 3f, new Vector3(1.8f, 3f, 1.8f), "Iron", default, O);
                Cyl("굴뚝 테", root, p + Vector3.up * 6.1f, new Vector3(2.2f, 0.3f, 2.2f), "Brass", default, OThin);
                FrozenSmoke(root, p + Vector3.up * 7.5f, 6, 2f);
            }

            // 다리 넷
            foreach (var p in new[] { new Vector3(-17f, 3.5f, 15f), new Vector3(17f, 3.5f, 15f), new Vector3(-18f, 3.5f, -14f), new Vector3(18f, 3.5f, -14f) })
            {
                Cyl("다리", root, p, new Vector3(8.5f, 4f, 8.5f), "Hyeonmu", default, OThick);
                for (int c = 0; c < 3; c++)
                    Box("발톱", root, p + new Vector3(-2.4f + c * 2.4f, -3.2f, 4.2f), new Vector3(1.1f, 1f, 2.6f), "HyeonmuPlate", new Vector3(20f, 0f, 0f), O);
                Cyl("관절 테", root, p + Vector3.up * 2.6f, new Vector3(9.2f, 0.5f, 9.2f), "Brass", default, OThin);
            }

            // 거북 머리 — 정면(남쪽)의 건청궁을 노려본다
            Ball("목", root, new Vector3(0f, 8f, 24f), new Vector3(10f, 8f, 12f), "Hyeonmu", default, OThick);
            var headT = Node("거북 머리", root, new Vector3(0f, 8.5f, 31f));
            headT.localRotation = Quaternion.Euler(8f, 0f, 0f);
            Ball("머리", headT, Vector3.zero, new Vector3(11f, 8f, 13f), "Hyeonmu", default, OThick);
            Box("아래턱", headT, new Vector3(0f, -2.8f, 2.4f), new Vector3(8.6f, 2f, 8f), "HyeonmuPlate", new Vector3(12f, 0f, 0f), O);
            Box("부리", headT, new Vector3(0f, -0.6f, 6.2f), new Vector3(3f, 2.2f, 2.6f), "HyeonmuPlate", new Vector3(20f, 0f, 0f), O);
            Ball("눈", headT, new Vector3(-3.3f, 1.6f, 4.8f), new Vector3(1.8f, 1.4f, 1.4f), "GlowBlue", default, NoLine);
            Ball("눈", headT, new Vector3(3.3f, 1.6f, 4.8f), new Vector3(1.8f, 1.4f, 1.4f), "GlowBlue", default, NoLine);
            MeshObj("얼어붙은 냉기 숨결", headT, Disc, new Vector3(0f, -1.5f, 14f), new Vector3(-80f, 0f, 0f), new Vector3(7f, 1f, 11f), "FX_FrozenBreath", null, false);

            // 신핵(神核) — 가슴 한가운데서 누렇게 빛난다(황)
            Ball("신핵 (神核)", root, new Vector3(0f, 6.5f, 25.5f), new Vector3(5.5f, 5.5f, 5.5f), "GlowYellow", default, O);
            Cyl("신핵 테", root, new Vector3(0f, 6.5f, 25.5f), new Vector3(7f, 0.5f, 7f), "Brass", new Vector3(90f, 0f, 0f), O);

            // 뱀의 목: 등에서 솟아 담장 너머 건청궁을 내려다본다
            var snake = Group("뱀의 목", root);
            var p0 = new Vector3(0f, 17f, -22f);
            var p1 = new Vector3(4f, 58f, -34f);
            var p2 = new Vector3(-14f, 66f, 22f);
            var p3 = new Vector3(-9f, 47f, 44f);
            const int Segments = 22;
            Vector3 prev = p0;
            for (int i = 0; i <= Segments; i++)
            {
                float u = i / (float)Segments;
                var pos = Bezier(p0, p1, p2, p3, u);
                float s = Mathf.Lerp(8.5f, 4.6f, u);
                var seg = Ball($"마디 {i:00}", snake, pos, new Vector3(s, s, s * 1.2f), i % 2 == 0 ? "Hyeonmu" : "HyeonmuPlate", default, OThick);
                if (i > 0) seg.transform.localRotation = Quaternion.LookRotation(pos - prev);
                if (i % 4 == 2) Cyl("놋쇠 마디 테", snake, pos, new Vector3(s * 1.06f, 0.35f, s * 1.06f), "Brass", Quaternion.FromToRotation(Vector3.up, (pos - prev).normalized).eulerAngles, NoLine);
                prev = pos;
            }

            var tangent = (p3 - Bezier(p0, p1, p2, p3, 0.95f)).normalized;
            var snakeHead = Node("뱀 머리", snake, p3);
            snakeHead.localRotation = Quaternion.LookRotation(tangent + Vector3.down * 0.6f);
            Ball("머리", snakeHead, new Vector3(0f, 0f, 2.5f), new Vector3(6f, 4.4f, 9f), "Hyeonmu", default, OThick);
            Box("위턱", snakeHead, new Vector3(0f, 0.6f, 7.2f), new Vector3(4.2f, 1.2f, 4.6f), "HyeonmuPlate", new Vector3(-10f, 0f, 0f), O);
            Box("아래턱", snakeHead, new Vector3(0f, -1.6f, 6.6f), new Vector3(3.8f, 1f, 4.2f), "HyeonmuPlate", new Vector3(18f, 0f, 0f), O);
            Ball("눈", snakeHead, new Vector3(-2.2f, 1.4f, 5f), new Vector3(1.1f, 0.9f, 1f), "GlowBlue", default, NoLine);
            Ball("눈", snakeHead, new Vector3(2.2f, 1.4f, 5f), new Vector3(1.1f, 0.9f, 1f), "GlowBlue", default, NoLine);
            Box("송곳니", snakeHead, new Vector3(-1.2f, -0.7f, 8.4f), new Vector3(0.35f, 1.6f, 0.35f), "IceCrystal", new Vector3(10f, 0f, 0f), OThin);
            Box("송곳니", snakeHead, new Vector3(1.2f, -0.7f, 8.4f), new Vector3(0.35f, 1.6f, 0.35f), "IceCrystal", new Vector3(10f, 0f, 0f), OThin);

            // 뱀 꼬리: 북동쪽 궁장을 휘감는다(세계 좌표)
            var tail = Group("성벽을 감은 꼬리", t);
            var tp = new[]
            {
                new Vector3(35f, 5f, 222f), new Vector3(92f, 9f, 205f), new Vector3(80f, 3f, 150f), new Vector3(73f, 4.5f, 138f),
                new Vector3(66f, 6f, 126f), new Vector3(78f, 2.5f, 100f), new Vector3(74f, 4f, 84f),
            };
            prev = tp[0];
            const int TailSegs = 44;
            for (int i = 0; i <= TailSegs; i++)
            {
                float u = i / (float)TailSegs;
                Vector3 pos = u < 0.5f
                    ? Bezier(tp[0], tp[1], tp[2], tp[3], u * 2f)
                    : Bezier(tp[3], tp[4], tp[5], tp[6], (u - 0.5f) * 2f);
                float s = Mathf.Lerp(6.5f, 1.8f, u);
                var seg = Ball($"꼬리 마디 {i:00}", tail, pos, new Vector3(s, s, s * 1.3f), i % 2 == 0 ? "Hyeonmu" : "HyeonmuPlate", default, OThick);
                if (i > 0 && (pos - prev).sqrMagnitude > 0.01f) seg.transform.localRotation = Quaternion.LookRotation(pos - prev);
                prev = pos;
            }

            // 시간을 삼키는 혼천의 고리
            var rings = Group("삼켜진 시간 — 혼천의 고리", t);
            var rc = new Vector3(10f, 62f, 200f);
            MeshObj("적도 고리", rings, ThinRing, rc, new Vector3(0f, 0f, 12f), Vector3.one * 82f, "FX_TimeRing", null, false);
            MeshObj("황도 고리", rings, ThinRing, rc, new Vector3(90f, 30f, 0f), Vector3.one * 64f, "FX_TimeRing", null, false);
            MeshObj("자오 고리", rings, ThinRing, rc, new Vector3(90f, -45f, 0f), Vector3.one * 52f, "FX_TimeRingFaint", null, false);
            for (int i = 0; i < 12; i++)
            {
                var pos = rc + Quaternion.Euler(0f, 0f, 12f) * (Quaternion.Euler(0f, i * 30f, 0f) * new Vector3(0f, 0f, 82f));
                Box($"시각 눈금 {i + 1}", rings, pos, new Vector3(1.8f, 1.8f, 5f), i % 3 == 0 ? "GlowYellow" : "GlowBlue",
                    new Vector3(0f, i * 30f, 12f), null);
            }

            // 발치의 얼음 가시
            var spikes = Group("얼음 가시", t);
            for (int i = 0; i < 40; i++)
            {
                float a = R(0f, 360f);
                float r = R(44f, 78f);
                var pos = new Vector3(10f, 0f, 200f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, r);
                if (pos.z < 158f) continue;
                float h = R(4f, 16f);
                MeshObj("얼음 가시", spikes, Spike, pos, new Vector3(R(-18f, 18f), R(0f, 360f), R(-18f, 18f)), new Vector3(R(1f, 2.4f), h, R(1f, 2.4f)), "IceCrystal", O);
            }
        }

        private static Vector3 Bezier(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b + 3f * u * t * t * c + t * t * t * d;
        }

        // ── 원경 ───────────────────────────────────────────────

        private static void BuildMountains(Transform t)
        {
            var list = new (Vector3 pos, float r, float h, string mat, int seed, string name)[]
            {
                (new Vector3(10f, 0f, 380f), 150f, 165f, "MountainNear", 11, "북악산 주봉"),
                (new Vector3(-120f, 0f, 350f), 120f, 110f, "MountainNear", 12, "북악산 서릉"),
                (new Vector3(130f, 0f, 360f), 110f, 95f, "MountainNear", 13, "북악산 동릉"),
                (new Vector3(-300f, 0f, 60f), 140f, 150f, "MountainNear", 14, "인왕산"),
                (new Vector3(-280f, 0f, 250f), 120f, 115f, "MountainMid", 15, "인왕산 북릉"),
                (new Vector3(290f, 0f, 40f), 90f, 60f, "MountainMid", 16, "낙산"),
                (new Vector3(-60f, 0f, 560f), 220f, 230f, "MountainMid", 17, "북한산 (먼 봉우리)"),
                (new Vector3(160f, 0f, 580f), 200f, 250f, "MountainMid", 18, "북한산 보현봉"),
                (new Vector3(330f, 0f, 460f), 180f, 150f, "MountainFar", 19, "먼 산 1"),
                (new Vector3(-360f, 0f, 480f), 200f, 170f, "MountainFar", 20, "먼 산 2"),
                (new Vector3(40f, 0f, -520f), 240f, 120f, "MountainFar", 21, "남산 너머"),
                (new Vector3(80f, 0f, -330f), 110f, 75f, "MountainMid", 22, "남산(목멱산)"),
            };

            foreach (var m in list)
            {
                var mesh = SaveMesh($"Mountain_{m.seed}", () => WorldMeshFactory.Mountain(14, 1f, 1f, 0.3f, m.seed));
                var peak = MeshObj(m.name, t, mesh, m.pos, new Vector3(0f, m.seed * 23f, 0f), new Vector3(m.r, m.h, m.r * 0.8f), m.mat, OMountain, false);

                // 곁봉우리 둘: 능선이 이어지는 산수화 느낌
                for (int k = 0; k < 2; k++)
                {
                    int seed = m.seed * 10 + k;
                    var sub = SaveMesh($"Mountain_{seed}", () => WorldMeshFactory.Mountain(12, 1f, 1f, 0.35f, seed));
                    var side = new Vector3((k == 0 ? -1f : 1f) * m.r * R(0.55f, 0.8f), 0f, R(-0.2f, 0.25f) * m.r);
                    MeshObj($"{m.name} 곁봉", peak.transform.parent, sub, m.pos + side, new Vector3(0f, seed * 17f, 0f),
                        new Vector3(m.r * R(0.5f, 0.7f), m.h * R(0.5f, 0.72f), m.r * R(0.45f, 0.6f)), m.mat, OMountain, false);
                }
            }
        }

        private static void BuildPines(Transform t)
        {
            var zones = new[]
            {
                new Rect(-66f, -30f, 26f, 170f), new Rect(40f, -30f, 26f, 100f),
                new Rect(-140f, 160f, 110f, 150f), new Rect(60f, 170f, 120f, 130f),
                new Rect(-160f, -160f, 110f, 120f), new Rect(140f, 30f, 80f, 150f),
            };

            int count = 0;
            foreach (var z in zones)
            {
                int n = Mathf.RoundToInt(z.width * z.height / 600f);
                for (int i = 0; i < n; i++)
                {
                    var p = new Vector3(R(z.xMin, z.xMax), 0f, R(z.yMin, z.yMax));
                    if (p.x > -55f && p.x < 75f && p.z > 150f && p.z < 290f) continue; // 현무 자리
                    if (Mathf.Abs(p.x) < 24f && p.z < 70f && p.z > -40f) continue;    // 궁궐 마당
                    Pine(t, p, R(0.7f, 1.4f));
                    count++;
                }
            }
        }

        private static void Pine(Transform parent, Vector3 pos, float s)
        {
            var t = Node("설송(雪松)", parent, pos, R(0f, 360f));
            float lean = R(-12f, 12f);
            var top = new Vector3(Mathf.Sin(lean * Mathf.Deg2Rad) * 6f * s, 7f * s, 0f);
            Pipe("줄기", t, Vector3.zero, top, 0.35f * s, "PineTrunk", OThin);
            for (int i = 0; i < 4; i++)
            {
                float u = 0.45f + i * 0.18f;
                var c = Vector3.Lerp(Vector3.zero, top, u) + new Vector3(R(-1.5f, 1.5f), 0.6f, R(-1.5f, 1.5f)) * s;
                float w = (4.2f - i * 0.7f) * s;
                Ball("솔잎", t, c, new Vector3(w, 0.9f * s, w * 0.8f), "Pine", new Vector3(0f, R(0f, 180f), 0f), O);
                Ball("눈 얹힘", t, c + Vector3.up * 0.45f * s, new Vector3(w * 0.8f, 0.35f * s, w * 0.6f), "SnowGround", new Vector3(0f, R(0f, 180f), 0f), NoLine);
            }
        }

        private static void BuildObangFlags(Transform t)
        {
            // 오방(五方): 동-청, 남-적, 중앙-황, 서-백, 북-흑
            Flag(t, "청(靑) · 동방기", new Vector3(74f, 0f, 60f), "FlagCheong");
            Flag(t, "적(赤) · 남방기", new Vector3(0f, 0f, -64f), "FlagJeok");
            Flag(t, "황(黃) · 중앙기", new Vector3(0f, 2.4f, 40f), "FlagHwang");
            Flag(t, "백(白) · 서방기", new Vector3(-74f, 0f, 60f), "FlagBaek");
            Flag(t, "흑(黑) · 북방기 — 현무 쪽으로 찢겨 있다", new Vector3(0f, 0f, 146f), "FlagHeuk", true);
        }

        private static void Flag(Transform parent, string name, Vector3 pos, string mat, bool torn = false)
        {
            var t = Node(name, parent, pos);
            Cyl("깃대", t, new Vector3(0f, 5f, 0f), new Vector3(0.22f, 5f, 0.22f), "Timber", default, OThin);
            Ball("깃대 머리", t, new Vector3(0f, 10.2f, 0f), new Vector3(0.5f, 0.5f, 0.5f), "Brass", default, OThin);

            // 바람이 멈춘 채 휘날리던 모양 그대로 굳은 깃발(세 조각으로 굽힘)
            for (int i = 0; i < 3; i++)
            {
                float x = 1f + i * 1.3f;
                float yaw = i * 14f * (torn ? -1.4f : 1f);
                Box("깃폭", t, new Vector3(x, 8.6f - i * 0.15f, Mathf.Sin(i * 0.9f) * 0.5f), new Vector3(1.35f, torn && i == 2 ? 1.1f : 2.6f, 0.06f), mat,
                    new Vector3(0f, yaw, 0f), OThin);
            }

            Box("화염각(불꽃 술)", t, new Vector3(1.2f, 7.1f, 0f), new Vector3(3.8f, 0.25f, 0.07f), "GlowRed", default, NoLine);
        }

        // ── 인물 ───────────────────────────────────────────────

        private enum Hat { None, Gat, Ikseongwan, Jeonrip, Hood, Headscarf }

        private static void BuildFigures(Transform t)
        {
            // 건청궁 마당: 고종이 비밀 무사에게 현무 토벌 어명을 내리는 순간
            var scene = Group("어명의 순간 (건청궁 마당)", t);
            var gojong = Figure(scene, "고종 (황룡포)", new Vector3(0f, 0.4f, 139f), 180f, "RobeYellow", Hat.Ikseongwan);
            Cyl("흉배 (금사 용)", gojong, new Vector3(0f, 1.2f, 0.23f), new Vector3(0.3f, 0.02f, 0.3f), "Brass", new Vector3(90f, 0f, 0f), OThin);
            Box("옥대", gojong, new Vector3(0f, 0.95f, 0f), new Vector3(0.62f, 0.1f, 0.5f), "PillarRed", default, OThin);
            Box("어명 교지", gojong, new Vector3(0.25f, 1.05f, 0.42f), new Vector3(0.5f, 0.06f, 0.34f), "Paper", new Vector3(-20f, 0f, 0f), OThin);
            Box("어좌 단", scene, new Vector3(0f, 0.2f, 139f), new Vector3(3f, 0.4f, 2.4f), "Stone", default, O);

            var hero = Figure(scene, "친군영 비밀 무사 (주인공)", new Vector3(0f, 0f, 135.6f), 0f, "RobeInk", Hat.Gat, true);
            Box("스팀 팩", hero, new Vector3(0f, 0.95f, -0.32f), new Vector3(0.46f, 0.58f, 0.26f), "PillarRed", default, OThin);
            Ball("과열 코어 (적)", hero, new Vector3(0f, 1.05f, -0.47f), new Vector3(0.2f, 0.2f, 0.1f), "GlowRed", default, NoLine);
            Pipe("증기관", hero, new Vector3(-0.18f, 1.25f, -0.3f), new Vector3(-0.18f, 1.6f, -0.2f), 0.04f, "Brass", null);
            Pipe("증기관", hero, new Vector3(0.18f, 1.25f, -0.3f), new Vector3(0.18f, 1.6f, -0.2f), 0.04f, "Brass", null);
            MeshObj("역류 혼천의 (청)", hero, ThinRing, new Vector3(0f, 1.15f, -0.5f), new Vector3(90f, 0f, 0f), Vector3.one * 0.42f, "GlowBlue", null);
            MeshObj("역류 혼천의 (청)", hero, ThinRing, new Vector3(0f, 1.15f, -0.5f), new Vector3(90f, 0f, 60f), Vector3.one * 0.34f, "GlowBlue", null);
            Box("흑철 대검", hero, new Vector3(0.55f, 0.08f, 0.35f), new Vector3(0.07f, 0.04f, 1.25f), "Iron", new Vector3(0f, 15f, 0f), OThin);
            Box("장용 화승총", hero, new Vector3(0.1f, 1.1f, -0.36f), new Vector3(0.06f, 0.06f, 1.1f), "Timber", new Vector3(60f, 0f, 30f), OThin);

            Figure(scene, "내관 (얼어붙음)", new Vector3(-3f, 0f, 138f), 160f, "VillagerIndigo", Hat.Gat, false, true);
            Figure(scene, "상궁 (얼어붙음)", new Vector3(3f, 0f, 138.4f), 200f, "VillagerWhite", Hat.Headscarf, false, true);

            // 광화문 안쪽: 흑화한 백동수(가상의 인물)가 문을 지키고 섰다
            var baekScene = Group("백검(白劍) 백동수 — 가상의 인물", t);
            var baek = Figure(baekScene, "백동수 (흑화)", new Vector3(0f, 0f, -26f), 180f, "RobeWhite", Hat.Gat);
            Box("백검", baek, new Vector3(0.45f, 1.25f, 0.6f), new Vector3(0.06f, 0.05f, 1.4f), "IceCrystal", new Vector3(-35f, 10f, 0f), OThin);
            Ball("눈빛", baek, new Vector3(-0.1f, 1.7f, 0.28f), new Vector3(0.07f, 0.05f, 0.05f), "GlowBlue", default, NoLine);
            Ball("눈빛", baek, new Vector3(0.1f, 1.7f, 0.28f), new Vector3(0.07f, 0.05f, 0.05f), "GlowBlue", default, NoLine);
            AddIce(baekScene, new Vector3(0f, 0f, -26f), 3.2f, 12);

            // 광화문 앞의 변이병(한기에 중독된 군졸)
            var soldiers = Group("변이병 (한기에 중독된 군졸)", t);
            var sp = new[] { new Vector3(-6f, 0f, -49f), new Vector3(6f, 0f, -49f), new Vector3(-9f, 0f, -80f), new Vector3(10f, 0f, -96f), new Vector3(3f, 0f, -115f) };
            for (int i = 0; i < sp.Length; i++)
            {
                var s = Figure(soldiers, $"변이병 {i + 1}", sp[i], i < 2 ? 180f : R(0f, 360f), "RobeGrey", Hat.Jeonrip);
                Pipe("창", s, new Vector3(0.35f, 0f, 0.1f), new Vector3(0.45f, 2.6f, 0.3f), 0.03f, "Timber", null);
                Box("창날", s, new Vector3(0.46f, 2.75f, 0.31f), new Vector3(0.08f, 0.35f, 0.03f), "IceCrystal", default, NoLine);
                Ball("눈빛", s, new Vector3(-0.1f, 1.7f, 0.28f), new Vector3(0.06f, 0.04f, 0.04f), "GlowBlue", default, NoLine);
                Ball("눈빛", s, new Vector3(0.1f, 1.7f, 0.28f), new Vector3(0.06f, 0.04f, 0.04f), "GlowBlue", default, NoLine);
                for (int c = 0; c < 4; c++)
                    Box("얼음 결정", s, new Vector3(R(-0.35f, 0.35f), R(1.2f, 1.5f), R(-0.3f, -0.1f)), new Vector3(0.1f, R(0.3f, 0.6f), 0.1f), "IceCrystal",
                        new Vector3(R(-40f, 40f), 0f, R(-40f, 40f)), OThin);
            }

            // 운종가: 멈춘 채 얼어붙은 백성들
            var people = Group("얼어붙은 백성 (운종가)", t);
            string[] robes = { "VillagerBrown", "VillagerWhite", "VillagerIndigo" };
            for (int i = 0; i < 18; i++)
            {
                var p = new Vector3(R(22f, 118f), 0f, -125f + R(-4.5f, 4.5f));
                bool kneel = i % 4 == 1;
                var f = Figure(people, kneel ? "빌고 있는 백성" : "달아나던 백성", p, R(0f, 360f), robes[i % 3], i % 3 == 0 ? Hat.Gat : Hat.Headscarf, kneel, true);
                if (!kneel) f.GetChild(0).localRotation = Quaternion.Euler(R(8f, 22f), 0f, 0f);
                if (i % 5 == 0) Figure(people, "아이", p + new Vector3(0.7f, 0f, 0.4f), R(0f, 360f), robes[(i + 1) % 3], Hat.None, false, true, 0.62f);
            }

            // 흑수회 광신도 — 얼지 않고 제단을 둘러쌌다
            var cult = Group("흑수회 광신도", t);
            for (int i = 0; i < 6; i++)
            {
                float a = i * 60f;
                var p = new Vector3(104f, 0f, -125f) + Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, 5f);
                var f = Figure(cult, $"광신도 {i + 1}", p, a + 180f, "RobeBlack", Hat.Hood, i % 2 == 0);
                Ball("흰 탈", f, new Vector3(0f, i % 2 == 0 ? 1.36f : 1.66f, 0.2f), new Vector3(0.24f, 0.28f, 0.1f), "Paper", default, OThin);
            }

            // 기기창의 얼어붙은 기술자
            var eng = Group("기기창 기술자 (얼어붙음)", t);
            Figure(eng, "포대 기술자", new Vector3(96f, 2f, 131f), 30f, "VillagerIndigo", Hat.Headscarf, false, true);
            Figure(eng, "화부", new Vector3(104f, 2f, 131f), -20f, "VillagerBrown", Hat.None, true, true);
        }

        /// <summary>도형 인물. 원점은 발밑, +Z 가 정면. 첫 자식이 몸통 묶음(기울일 때 쓴다).</summary>
        private static Transform Figure(Transform parent, string name, Vector3 pos, float yaw, string robe, Hat hat,
            bool kneel = false, bool frozen = false, float scale = 1f)
        {
            var root = Node(name, parent, pos, yaw);
            root.localScale = Vector3.one * scale;
            var body = Group("몸", root);
            string m = frozen ? "Frozen" : robe;
            string skin = frozen ? "Frozen" : "Skin";
            float drop = kneel ? 0.45f : 0f;

            if (kneel)
                Cyl("무릎 꿇은 하체", body, new Vector3(0f, 0.22f, -0.05f), new Vector3(0.7f, 0.22f, 0.75f), m, default, OThin);
            else
                Cyl("치마·도포 자락", body, new Vector3(0f, 0.42f, 0f), new Vector3(0.64f, 0.42f, 0.6f), m, default, OThin);

            P(PrimitiveType.Capsule, "몸통", body, new Vector3(0f, 1.15f - drop, 0f), new Vector3(0.52f, 0.5f, 0.42f), m, default, OThin);
            P(PrimitiveType.Capsule, "팔", body, new Vector3(-0.33f, 1.12f - drop, 0.05f), new Vector3(0.17f, 0.32f, 0.17f), m, new Vector3(0f, 0f, -12f), OThin);
            P(PrimitiveType.Capsule, "팔", body, new Vector3(0.33f, 1.12f - drop, 0.05f), new Vector3(0.17f, 0.32f, 0.17f), m, new Vector3(0f, 0f, 12f), OThin);
            float hy = 1.66f - drop;
            Ball("머리", body, new Vector3(0f, hy, 0f), new Vector3(0.32f, 0.34f, 0.32f), skin, default, OThin);

            switch (hat)
            {
                case Hat.Gat:
                    Cyl("갓 양태", body, new Vector3(0f, hy + 0.15f, 0f), new Vector3(0.78f, 0.012f, 0.78f), "Ink", default, NoLine);
                    Cyl("갓 모자", body, new Vector3(0f, hy + 0.25f, 0f), new Vector3(0.24f, 0.11f, 0.24f), "Ink", default, NoLine);
                    break;
                case Hat.Ikseongwan:
                    Cyl("익선관", body, new Vector3(0f, hy + 0.2f, -0.02f), new Vector3(0.32f, 0.1f, 0.32f), "Ink", default, OThin);
                    Box("익선관 뿔", body, new Vector3(-0.12f, hy + 0.3f, -0.14f), new Vector3(0.16f, 0.12f, 0.02f), "Ink", new Vector3(0f, 0f, 20f), NoLine);
                    Box("익선관 뿔", body, new Vector3(0.12f, hy + 0.3f, -0.14f), new Vector3(0.16f, 0.12f, 0.02f), "Ink", new Vector3(0f, 0f, -20f), NoLine);
                    break;
                case Hat.Jeonrip:
                    Cyl("전립 양태", body, new Vector3(0f, hy + 0.13f, 0f), new Vector3(0.62f, 0.02f, 0.62f), "Ink", default, NoLine);
                    Ball("전립 모자", body, new Vector3(0f, hy + 0.2f, 0f), new Vector3(0.3f, 0.2f, 0.3f), "Ink", default, NoLine);
                    Ball("상모", body, new Vector3(0f, hy + 0.33f, 0f), new Vector3(0.08f, 0.08f, 0.08f), "PillarRed", default, NoLine);
                    break;
                case Hat.Hood:
                    Ball("두건", body, new Vector3(0f, hy + 0.03f, -0.03f), new Vector3(0.42f, 0.46f, 0.42f), "RobeBlack", default, OThin);
                    break;
                case Hat.Headscarf:
                    Ball("수건", body, new Vector3(0f, hy + 0.09f, -0.02f), new Vector3(0.35f, 0.24f, 0.35f), frozen ? "Frozen" : "VillagerWhite", default, NoLine);
                    break;
            }

            if (frozen) AddIce(root, Vector3.zero, 0.6f, 4);
            return root;
        }

        // ── 소품 ───────────────────────────────────────────────

        private static Vector3 LampPost(Transform parent, Vector3 pos)
        {
            var t = Node("전등주", parent, pos);
            Cyl("기둥", t, new Vector3(0f, 2.4f, 0f), new Vector3(0.14f, 2.4f, 0.14f), "Iron", default, OThin);
            Box("가로대", t, new Vector3(0f, 4.6f, 0f), new Vector3(1.1f, 0.08f, 0.08f), "Iron", default, NoLine);
            Cyl("갓등", t, new Vector3(0f, 4.95f, 0f), new Vector3(0.5f, 0.08f, 0.5f), "Iron", default, OThin);
            Ball("전구", t, new Vector3(0f, 4.72f, 0f), new Vector3(0.34f, 0.4f, 0.34f), "GlowLamp", default, NoLine);

            var lightGo = new GameObject("전등 불빛");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = new Vector3(0f, 4.5f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.83f, 0.52f);
            l.range = 11f;
            l.intensity = 2.2f;
            l.shadows = LightShadows.None;

            return t.position + Vector3.up * 4.9f;
        }

        /// <summary>흑수회 괘서(掛書): 나라가 망한다는 참언을 적은 종이.</summary>
        private static void Placard(Transform parent, Vector3 pos, float yaw)
        {
            var t = Node("괘서 (掛書)", parent, pos, yaw);
            Box("종이", t, Vector3.zero, new Vector3(0.9f, 1.25f, 0.02f), "Paper", default, OThin);
            for (int i = 0; i < 4; i++)
                Box("먹글씨", t, new Vector3(-0.28f + i * 0.18f, 0.02f, 0.015f), new Vector3(0.05f, 0.95f, 0.01f), "Ink", default, NoLine);
            Ball("흑수회 표식", t, new Vector3(0f, -0.48f, 0.02f), new Vector3(0.16f, 0.16f, 0.01f), "Ink", default, NoLine);
        }

        private static void BlackBanner(Transform parent, Vector3 pos, float yaw)
        {
            var t = Node("흑수회 기", parent, pos, yaw);
            Cyl("깃대", t, new Vector3(0f, 2.2f, 0f), new Vector3(0.1f, 2.2f, 0.1f), "Ink", default, NoLine);
            Box("검은 기", t, new Vector3(0.7f, 3.6f, 0f), new Vector3(1.3f, 1.6f, 0.04f), "RobeBlack", default, OThin);
            Cyl("흰 고리 (물결)", t, new Vector3(0.7f, 3.6f, 0.03f), new Vector3(0.7f, 0.01f, 0.7f), "Paper", new Vector3(90f, 0f, 0f), NoLine);
            Cyl("검은 점", t, new Vector3(0.7f, 3.6f, 0.04f), new Vector3(0.45f, 0.012f, 0.45f), "RobeBlack", new Vector3(90f, 0f, 0f), NoLine);
        }

        private static void Cart(Transform parent, Vector3 pos, float yaw)
        {
            var t = Node("버려진 수레", parent, pos, yaw);
            Box("짐칸", t, new Vector3(0f, 0.9f, 0f), new Vector3(1.6f, 0.2f, 2.6f), "Timber", new Vector3(0f, 0f, 8f), OThin);
            Cyl("바퀴", t, new Vector3(-0.95f, 0.6f, 0f), new Vector3(1.2f, 0.06f, 1.2f), "Timber", new Vector3(0f, 0f, 90f), OThin);
            Cyl("바퀴", t, new Vector3(0.95f, 0.75f, 0f), new Vector3(1.2f, 0.06f, 1.2f), "Timber", new Vector3(0f, 0f, 90f), OThin);
            Pipe("채", t, new Vector3(-0.5f, 0.9f, 1.3f), new Vector3(-0.5f, 0.1f, 3f), 0.05f, "Timber", null);
            Pipe("채", t, new Vector3(0.5f, 0.9f, 1.3f), new Vector3(0.5f, 0.1f, 3f), 0.05f, "Timber", null);
            Cyl("볏섬", t, new Vector3(0f, 1.4f, 0f), new Vector3(0.9f, 0.6f, 0.9f), "Thatch", new Vector3(90f, 0f, 0f), OThin);
        }

        private static void Brick(Transform parent, string name, Vector3 pos, float yaw, float w, float d, float h)
        {
            var t = Node(name, parent, pos, yaw);
            Box("벽돌 벽", t, new Vector3(0f, h * 0.5f, 0f), new Vector3(w, h, d), "Brick", default, O, true);
            for (int i = 0; i < Mathf.FloorToInt(w / 3f); i++)
            {
                float x = -w * 0.5f + 1.5f + i * 3f;
                Box("아치 창", t, new Vector3(x, h * 0.55f, d * 0.5f + 0.02f), new Vector3(1.1f, h * 0.4f, 0.06f), "Ink", default, NoLine);
                Box("창 테", t, new Vector3(x, h * 0.55f + h * 0.22f, d * 0.5f + 0.04f), new Vector3(1.4f, 0.2f, 0.06f), "Stone", default, NoLine);
            }

            MeshObj("함석 지붕", t, Roof(w + 1.5f, d + 1.5f, h * 0.35f, 0f), new Vector3(0f, h, 0f), Vector3.zero, Vector3.one, "Iron", O);
        }

        private static void Gear(Transform parent, Vector3 pos, float radius, Vector3 euler)
        {
            var t = Node("톱니바퀴", parent, pos);
            t.localRotation = Quaternion.Euler(euler);
            Cyl("톱니 몸", t, Vector3.zero, new Vector3(radius * 2f, 0.3f, radius * 2f), "Brass", default, O);
            Cyl("축", t, Vector3.zero, new Vector3(radius * 0.5f, 0.45f, radius * 0.5f), "Iron", default, OThin);
            int teeth = Mathf.Max(8, Mathf.RoundToInt(radius * 6f));
            for (int i = 0; i < teeth; i++)
            {
                float a = i * 360f / teeth;
                var p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, radius + 0.2f);
                Box("톱니", t, p, new Vector3(radius * 0.35f, 0.5f, 0.5f), "Brass", new Vector3(0f, a, 0f), NoLine);
            }
        }

        /// <summary>시간이 멈춰 허공에 굳은 연기(수묵 구름처럼 그린다).</summary>
        private static void FrozenSmoke(Transform parent, Vector3 start, int puffs, float size)
        {
            var t = Group("굳은 연기", parent);
            t.localPosition = start;
            var p = Vector3.zero;
            var wind = new Vector3(R(0.6f, 1.2f), 0f, R(-0.4f, 0.4f));
            float phase = R(0f, 6f);
            for (int i = 0; i < puffs; i++)
            {
                float grow = 1f + i * 0.22f;
                float s = size * grow * R(0.75f, 1.25f);
                var off = new Vector3(Mathf.Sin(phase + i * 0.9f) * s * 0.35f, 0f, Mathf.Cos(phase + i * 0.7f) * s * 0.25f);
                Ball("연기", t, p + off, new Vector3(s * R(1f, 1.4f), s * R(0.45f, 0.65f), s * R(0.9f, 1.2f)), "Plaster",
                    new Vector3(R(-15f, 15f), R(0f, 180f), R(-15f, 15f)), OThin);
                if (i % 2 == 1)
                    Ball("연기 자락", t, p + off + new Vector3(s * 0.5f, -s * 0.15f, 0f), Vector3.one * s * 0.55f, "Plaster", default, OThin);
                p += new Vector3(0f, s * 0.42f, 0f) + wind * s * 0.35f * (i * 0.25f);
            }
        }

        /// <summary>얼어붙은 불길: 타오르던 모양 그대로 굳은 적색 불꽃.</summary>
        private static void FrozenFire(Transform parent, Vector3 pos)
        {
            var t = Group("굳은 불길", parent);
            t.localPosition = pos;
            for (int i = 0; i < 9; i++)
            {
                var p = new Vector3(R(-2.8f, 2.8f), R(-0.2f, 0.6f), R(-2f, 2f));
                MeshObj("불꽃", t, Spike, p, new Vector3(R(-10f, 10f), R(0f, 360f), R(-10f, 10f)), new Vector3(R(0.6f, 1.2f), R(1.8f, 3.4f), R(0.6f, 1.2f)),
                    i % 3 == 0 ? "GlowRed" : "GlowFire", OThin, false);
            }

            var lightGo = new GameObject("굳은 불빛");
            lightGo.transform.SetParent(t, false);
            lightGo.transform.localPosition = Vector3.up * 1.5f;
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = new Color(1f, 0.5f, 0.2f);
            l.range = 12f;
            l.intensity = 2.4f;
        }

        private static void FrozenSparks(Transform parent, Vector3 pos)
        {
            var t = Group("굳은 불티", parent);
            t.localPosition = pos;
            for (int i = 0; i < 14; i++)
            {
                var d = new Vector3(R(-1f, 1f), R(0.2f, 1.2f), R(-1f, 1f)).normalized * R(0.5f, 3f);
                Ball("불티", t, d, Vector3.one * R(0.08f, 0.18f), i % 2 == 0 ? "GlowLamp" : "GlowBlue", default, NoLine);
            }
        }

        private static void AddIce(Transform parent, Vector3 center, float radius, int count)
        {
            var t = Group("얼음 결정", parent);
            t.localPosition = center;
            for (int i = 0; i < count; i++)
            {
                float a = R(0f, 360f);
                var p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0f, R(radius * 0.3f, radius));
                MeshObj("얼음", t, Spike, p, new Vector3(R(-25f, 25f), R(0f, 360f), R(-25f, 25f)),
                    new Vector3(R(0.15f, 0.35f), R(0.5f, 1.4f), R(0.15f, 0.35f)) * Mathf.Max(1f, radius * 0.5f), "IceCrystal", OThin, false);
            }
        }

        // ── 카메라 ─────────────────────────────────────────────

        private static void BuildCamera(Transform root)
        {
            var camGo = new GameObject("Main Camera (전경)");
            camGo.transform.SetParent(root, false);
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(6f, 24f, -150f);
            camGo.transform.rotation = Quaternion.LookRotation(new Vector3(6f, 44f, 160f) - camGo.transform.position);

            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = FogFrozen;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 1400f;
            cam.fieldOfView = 46f;
            camGo.AddComponent<AudioListener>();

            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;

            // 둘러볼 때 쓰는 시점 표식(씬 뷰에서 더블클릭해 이동)
            var views = Group("시점 표식", root);
            AddView(views, "시점_전경 (도성 동남쪽)", new Vector3(78f, 34f, -118f), new Vector3(8f, 22f, 100f));
            AddView(views, "시점_어명의 순간", new Vector3(5f, 2.6f, 130.5f), new Vector3(0f, 1.3f, 138f));
            AddView(views, "시점_광화문과 백동수", new Vector3(10f, 5f, -70f), new Vector3(0f, 6f, -30f));
            AddView(views, "시점_운종가", new Vector3(12f, 6f, -128f), new Vector3(80f, 2f, -125f));
            AddView(views, "시점_향원정과 현무", new Vector3(-30f, 12f, 70f), new Vector3(5f, 40f, 170f));
            AddView(views, "시점_기기창 포대", new Vector3(125f, 10f, 95f), new Vector3(60f, 30f, 190f));
        }

        private static void AddView(Transform parent, string name, Vector3 pos, Vector3 lookAt)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.rotation = Quaternion.LookRotation(lookAt - pos);
        }
    }

    internal static class ColorExt
    {
        public static Color WithA(this Color c, float a)
        {
            c.a = a;
            return c;
        }
    }
}
