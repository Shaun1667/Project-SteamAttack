using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NMJ
{
    /// <summary>
    /// 원경 산을 바르코3D 모델로 바꿔 배치 (조선스팀펑크 플로우에서 만든 산 4종, Arts/Models/NMJ/VarcoMountains).
    /// 메뉴: SteamAttack ▸ NMJ ▸ 원경 산 바르코 모델로 배치
    ///
    /// · 기존 '10_북악산_원경'의 산 36개 위치·회전·크기를 그대로 따라 NMJ_BackgroundMountains 프리팹을 만든다
    ///   (모델은 블렌더에서 바닥 가운데 기준 · 반지름 1 · 높이 1로 맞춰 둠 → 기존 배율 그대로 씀)
    /// · 이름으로 모델을 고른다: 먼 산 = 푸른 원경 / 남산·낙산 = 소나무 눈 언덕 / 서릉·동릉·북릉 = 세 봉우리 능선 / 나머지 = 뾰족한 바위산
    /// · 열린 씬에 프리팹을 놓고 기존 산 묶음은 끈다 (지우지 않음 — 되돌리려면 켜고 프리팹을 지우면 됨). 씬 저장은 사람이 한다
    /// </summary>
    public static class NmjVarcoMountainPlacer
    {
        private const string Dir = "Assets/Arts/Models/NMJ/VarcoMountains";
        private const string PrefabPath = "Assets/Prefabs/NMJ/NMJ_BackgroundMountains.prefab";
        private const string OldGroup = "현무_세계관 (1887 정월, 멈춘 도성)/10_북악산_원경";
        private static readonly string[] Models = { "SnowyMountainPeak", "SnowyRockyMountains", "SnowyMountainPines", "BlueLowPolyMountains" };
        // 족자(산수화) 화풍: 사용자 족자 사진을 참고 이미지로 바르코에서 새로 뽑은 4종 (같은 순서: 주봉 / 세 봉우리 능선 / 소나무 언덕 / 먼 산)
        private static readonly string[] ScrollModels = { "ScrollPeak", "ScrollRidge", "ScrollPineHill", "ScrollFarRange" };

        [MenuItem("SteamAttack/NMJ/원경 산 바르코 모델로 배치", false, 22)]
        public static void Run() => Place(Models);

        [MenuItem("SteamAttack/NMJ/원경 산 족자화풍 모델로 배치", false, 23)]
        public static void RunScroll() => Place(ScrollModels);

        // ───────── 수묵화 산 (2026-10-26) ─────────
        // 동선 v4(근정전 앞 → 광화문 → 육조거리, 남쪽으로 달린다)에서는 북악산 쪽 산이 등 뒤라 거의 안 보였다.
        // 남쪽 정면(관악산 줄기)·동서쪽에 큰 산을 더하고, 참고 그림(먹빛 바위 + 눈 + 소나무)으로 바르코에서 새로 뽑은 수묵 산(Ink/)을 쓴다.
        // 멀고 가까움은 거리 안개, 산 발치는 운무 띠(Mist/)가 맡는다
        private const string MistDir = Dir + "/Mist";

        private struct Extra { public string name; public int model; public Vector3 pos; public Vector3 scale; public float yaw; }
        private static readonly Extra[] SumukExtras =
        {
            // 남쪽 정면: 육조거리를 달리는 내내 정면에 보이는 큰 봉우리 (카메라 먼 거리 1000m 안쪽)
            new Extra { name = "관악산 주봉", model = 0, pos = new Vector3(30f, 0f, -430f), scale = new Vector3(150f, 230f, 130f), yaw = 20f },
            new Extra { name = "관악산 곁봉", model = 1, pos = new Vector3(-120f, 0f, -410f), scale = new Vector3(120f, 140f, 100f), yaw = 200f },
            new Extra { name = "관악산 곁봉", model = 1, pos = new Vector3(190f, 0f, -400f), scale = new Vector3(110f, 130f, 95f), yaw = 160f },
            new Extra { name = "청계산", model = 0, pos = new Vector3(-260f, 0f, -360f), scale = new Vector3(110f, 170f, 100f), yaw = 80f },
            new Extra { name = "남한산 능선", model = 1, pos = new Vector3(300f, 0f, -330f), scale = new Vector3(130f, 150f, 110f), yaw = 300f },
            // 동서: 근정전 마당에서 담 너머로 보이는 산
            new Extra { name = "아차산", model = 0, pos = new Vector3(330f, 0f, -120f), scale = new Vector3(110f, 170f, 100f), yaw = 140f },
            new Extra { name = "안산", model = 0, pos = new Vector3(-280f, 0f, -200f), scale = new Vector3(100f, 150f, 90f), yaw = 250f },
        };

        [MenuItem("SteamAttack/NMJ/원경 산 수묵화로 배치 (동선 v4)", false, 24)]
        public static void RunSumuk()
        {
            var old = FindIncludingInactive(OldGroup);
            if (old == null) { Debug.LogWarning("[NMJ] 씬에 '10_북악산_원경'이 없다. 공용 Main을 열고 다시 실행해라."); return; }

            var meshes = new Mesh[ScrollModels.Length];
            var mats = new Material[ScrollModels.Length];
            var footprint = new Vector2[ScrollModels.Length];   // 새 모델 폭을 옛 모델 폭에 맞추는 배율 (배치 배율이 옛 모델 기준이라)
            for (int i = 0; i < ScrollModels.Length; i++)
            {
                var oldMesh = PrepareMesh(ScrollModels[i]);
                var ink = PrepareInk(InkModels[i]);
                if (ink.mesh != null)
                {
                    meshes[i] = ink.mesh; mats[i] = ink.mat;
                    footprint[i] = new Vector2(oldMesh.bounds.extents.x / ink.mesh.bounds.extents.x, oldMesh.bounds.extents.z / ink.mesh.bounds.extents.z);
                }
                else
                {
                    // 수묵 산 파일이 없으면 족자화풍 원본으로 (배치는 같게)
                    meshes[i] = oldMesh; mats[i] = PrepareMaterial(ScrollModels[i]); footprint[i] = Vector2.one;
                }
            }

            var root = new GameObject("NMJ_BackgroundMountains");
            int n = 0, side = 0;
            void Add(string name, int k, Vector3 pos, Quaternion rot, Vector3 scale)
            {
                var go = new GameObject(name + " (수묵)");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(pos, rot);
                go.transform.localScale = new Vector3(scale.x * footprint[k].x, scale.y, scale.z * footprint[k].y);
                go.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { mats[k] };
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                go.isStatic = true;
                n++;
            }

            foreach (Transform t in old.transform)
            {
                if (t.GetComponent<MeshRenderer>() == null) continue;
                Vector3 s = t.lossyScale;
                // 남쪽·동쪽 낮은 언덕은 키를 키워 지붕 너머로 보이게
                if (t.name.Contains("남산(목멱산)")) s.y *= 1.35f;
                if (t.name.Contains("낙산")) s.y *= 1.8f;
                Vector3 pos = t.position;
                // 보스방(Main 안 (-110, 104), 60×60)에 산 자락이 걸리지 않게: 서쪽 산은 보스방 서쪽 끝(x -140)에서 30m 더 물린다
                float halfWidth = s.x * 0.8f;   // 능선 모델 반폭(약 0.8) 기준 넉넉히
                if (pos.x < -100f && pos.z > 40f && pos.z < 170f && pos.x + halfWidth > -170f) pos.x = -170f - halfWidth;
                Add(t.name, Pick(t.name, ref side), pos, t.rotation, s);
            }
            foreach (var e in SumukExtras) Add(e.name, e.model, e.pos, Quaternion.Euler(0f, e.yaw, 0f), e.scale);
            AddMistBands(root.transform);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            var existing = GameObject.Find("NMJ_BackgroundMountains");
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.scene);
            Undo.RegisterCreatedObjectUndo(inst, "수묵 산 배치");
            old.SetActive(false);
            // 공기감: 거리 안개 — 플레이하는 곳(200m 안)은 선명, 산은 멀수록 하늘빛으로 아련하게 물러난다
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = MistColor;
            RenderSettings.fogStartDistance = 200f;
            RenderSettings.fogEndDistance = 1150f;
            EditorSceneManager.MarkSceneDirty(old.scene);
            Debug.Log($"[NMJ] 수묵화 산 {n}개를 배치했다 (남쪽·동서 {SumukExtras.Length}개 추가). 씬을 저장하면 반영된다.");
        }

        // ───────── 안개 띠: 산과 산 사이에 낮게 깔린 운무 (산수화의 여백) ─────────
        private static readonly Color MistColor = new Color(0.80f, 0.82f, 0.82f, 1f);   // 하늘·안개 같은 색
        private struct Band { public Vector3 pos; public float yaw, width, height; }
        private static readonly Band[] MistBands =
        {
            // 남쪽: 남산 앞 / 관악산 발치 / 그 너머 — 겹겹이
            new Band { pos = new Vector3(40f, 0f, -290f), yaw = 0f, width = 900f, height = 38f },
            new Band { pos = new Vector3(0f, 0f, -380f), yaw = 0f, width = 1000f, height = 70f },
            new Band { pos = new Vector3(0f, 0f, -500f), yaw = 0f, width = 1100f, height = 90f },
            // 동서: 담 너머 산 발치
            new Band { pos = new Vector3(240f, 0f, -60f), yaw = 90f, width = 700f, height = 45f },
            new Band { pos = new Vector3(-230f, 0f, -60f), yaw = 90f, width = 700f, height = 45f },
            // 북쪽: 북악산 발치 (오프닝·뒤돌아볼 때)
            new Band { pos = new Vector3(0f, 0f, 300f), yaw = 0f, width = 1000f, height = 60f },
        };

        private static void AddMistBands(Transform root)
        {
            var mat = PrepareMistMaterial();
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var group = new GameObject("운무 띠").transform;
            group.SetParent(root, false);
            for (int i = 0; i < MistBands.Length; i++)
            {
                var b = MistBands[i];
                var go = new GameObject($"운무_{i + 1}");
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(b.pos + Vector3.up * (b.height * 0.5f - 2f), Quaternion.Euler(0f, b.yaw, 0f));
                go.transform.localScale = new Vector3(b.width, b.height, 1f);
                go.AddComponent<MeshFilter>().sharedMesh = quad;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                // 양면으로 보이게 뒤집은 판 하나 더
                var back = Object.Instantiate(go, group);
                back.name = go.name + "_뒤";
                back.transform.rotation = go.transform.rotation * Quaternion.Euler(0f, 180f, 0f);
            }
        }

        // 수묵 산 4종 (Ink/): 같은 순서 — 주봉 / 세 봉우리 능선 / 소나무 언덕 / 먼 산줄기
        private static readonly string[] InkModels = { "InkPeak", "InkRidge", "InkPineHill", "InkFarRange" };

        /// <summary>Ink/{model}.fbx + 2K 먹빛 텍스처 + 원본 요철. 햇빛을 받는 Lit (번들거림 없음)</summary>
        private static (UnityEngine.Mesh mesh, Material mat) PrepareInk(string model)
        {
            string dir = $"{Dir}/Ink";
            string fbx = $"{dir}/{model}.fbx";
            if (!(AssetImporter.GetAtPath(fbx) is ModelImporter mi)) return (null, null);
            if (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation)
            {
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = false;
                mi.SaveAndReimport();
            }
            UnityEngine.Mesh mesh = null;
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(fbx)) if (o is UnityEngine.Mesh m) mesh = m;

            string albedo = $"{dir}/{model}_Albedo.png", normal = $"{dir}/{model}_Normal.png";
            if (AssetImporter.GetAtPath(albedo) is TextureImporter ai && ai.maxTextureSize != 2048) { ai.maxTextureSize = 2048; ai.SaveAndReimport(); }
            if (AssetImporter.GetAtPath(normal) is TextureImporter ni && ni.textureType != TextureImporterType.NormalMap) { ni.textureType = TextureImporterType.NormalMap; ni.SaveAndReimport(); }

            var lit = Shader.Find("Universal Render Pipeline/Lit");
            string matPath = $"{dir}/M_{model}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null) { mat = new Material(lit); AssetDatabase.CreateAsset(mat, matPath); }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(albedo));
            var n = AssetDatabase.LoadAssetAtPath<Texture2D>(normal);
            if (n != null) { mat.SetTexture("_BumpMap", n); mat.SetFloat("_BumpScale", 1.2f); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0f); mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_EnvironmentReflections", 0f); mat.SetFloat("_SpecularHighlights", 0f);
            EditorUtility.SetDirty(mat);
            return (mesh, mat);
        }

        /// <summary>a~b 사이에서 0→1로 부드럽게 (Mathf.SmoothStep은 두 값 사이 보간이라 다르다)</summary>
        private static float Smooth01(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }

        /// <summary>아래는 짙고 위로 갈수록, 양끝으로 갈수록 사라지는 운무 텍스처 + 빛 없는 투명 재질</summary>
        private static Material PrepareMistMaterial()
        {
            System.IO.Directory.CreateDirectory(MistDir);
            string texPath = $"{MistDir}/T_MistBand.png";
            if (!System.IO.File.Exists(texPath))
            {
                const int W = 512, H = 128;
                var t = new Texture2D(W, H, TextureFormat.RGBA32, false);
                for (int y = 0; y < H; y++)
                    for (int x = 0; x < W; x++)
                    {
                        float u = x / (W - 1f), v = y / (H - 1f);
                        float edge = Smooth01(0f, 0.22f, Mathf.Min(u, 1f - u));              // 양끝 사라짐
                        float wave = 0.12f * Mathf.Sin(u * 17f) + 0.08f * Mathf.Sin(u * 41f + 1.3f);       // 위 경계를 구름처럼 출렁이게
                        float top = 1f - Smooth01(0.05f, 0.62f + wave * 0.6f, v);   // 위로 갈수록 옅게, 맨 위는 반드시 0 (직선 경계가 안 보이게)
                        float a = top * edge;
                        t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                t.Apply();
                System.IO.File.WriteAllBytes(texPath, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(texPath);
                if (AssetImporter.GetAtPath(texPath) is TextureImporter ti) { ti.alphaIsTransparency = true; ti.wrapMode = TextureWrapMode.Clamp; ti.SaveAndReimport(); }
            }
            string matPath = $"{MistDir}/M_MistBand.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(texPath));
            mat.SetColor("_BaseColor", new Color(MistColor.r, MistColor.g, MistColor.b, 0.85f));
            // 투명
            mat.SetFloat("_Surface", 1f); mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", 2f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static void Place(string[] models)
        {
            var old = FindIncludingInactive(OldGroup);   // 한 번 배치하면 꺼져 있으므로 GameObject.Find로는 못 찾음
            if (old == null) { Debug.LogWarning("[NMJ] 씬에 '10_북악산_원경'이 없다. 공용 Main을 열고 다시 실행해라."); return; }

            var meshes = new Mesh[models.Length];
            var mats = new Material[models.Length];
            for (int i = 0; i < models.Length; i++) { meshes[i] = PrepareMesh(models[i]); mats[i] = PrepareMaterial(models[i]); }

            var root = new GameObject("NMJ_BackgroundMountains");
            int n = 0, side = 0;
            foreach (Transform t in old.transform)
            {
                if (t.GetComponent<MeshRenderer>() == null) continue;
                int k = Pick(t.name, ref side);
                var go = new GameObject(t.name + " (바르코)");
                go.transform.SetParent(root.transform, false);
                go.transform.SetPositionAndRotation(t.position, t.rotation);
                go.transform.localScale = t.lossyScale;
                go.AddComponent<MeshFilter>().sharedMesh = meshes[k];
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterials = new[] { mats[k] };   // 먹 외곽선은 뺀다: 세밀한 모델 표면 전체에 겹쳐 산이 검게 덮임
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;   // 먼 배경: 그림자 생략
                go.isStatic = true;
                n++;
            }

            System.IO.Directory.CreateDirectory("Assets/Prefabs/NMJ");
            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);

            // 씬: 이미 놓였으면 바꿔 놓고, 기존 산 묶음은 끈다
            var existing = GameObject.Find("NMJ_BackgroundMountains");
            if (existing != null) Undo.DestroyObjectImmediate(existing);
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab, old.scene);
            Undo.RegisterCreatedObjectUndo(inst, "바르코 산 배치");
            Undo.RecordObject(old, "기존 산 끄기");
            old.SetActive(false);
            EditorSceneManager.MarkSceneDirty(old.scene);
            Debug.Log($"[NMJ] 바르코 산 {n}개를 배치했다 ({PrefabPath}). 기존 '10_북악산_원경'은 꺼 둠. 씬을 저장하면 반영된다.");
        }

        private static GameObject FindIncludingInactive(string path)
        {
            int slash = path.IndexOf('/');
            string rootName = slash < 0 ? path : path.Substring(0, slash);
            for (int s = 0; s < UnityEngine.SceneManagement.SceneManager.sceneCount; s++)
                foreach (var r in UnityEngine.SceneManagement.SceneManager.GetSceneAt(s).GetRootGameObjects())
                {
                    if (r.name != rootName) continue;
                    if (slash < 0) return r;
                    var t = r.transform.Find(path.Substring(slash + 1));
                    if (t != null) return t.gameObject;
                }
            return null;
        }

        private static int Pick(string name, ref int side)
        {
            if (name.Contains("먼 산")) return 3;
            if (name.Contains("남산") || name.Contains("낙산")) return 2;
            if (name.Contains("릉")) return 1;
            if (name.Contains("곁봉")) return (side++ % 2 == 0) ? 0 : 1;
            return 0;
        }

        private static Mesh PrepareMesh(string model)
        {
            string path = $"{Dir}/{model}.fbx";
            var mi = AssetImporter.GetAtPath(path) as ModelImporter;
            if (mi != null && (mi.materialImportMode != ModelImporterMaterialImportMode.None || mi.importAnimation))
            {
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importAnimation = false;
                mi.isReadable = false;
                mi.SaveAndReimport();
            }
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh m) return m;
            return null;
        }

        private static Material PrepareMaterial(string model)
        {
            string matPath = $"{Dir}/M_{model}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            string normalPath = $"{Dir}/{model}_Normal.png";
            if (AssetImporter.GetAtPath(normalPath) is TextureImporter ti && ti.textureType != TextureImporterType.NormalMap)
            {
                ti.textureType = TextureImporterType.NormalMap;
                ti.maxTextureSize = 1024;
                ti.SaveAndReimport();
            }
            mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>($"{Dir}/{model}_Albedo.png"));
            var normal = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Smoothness", 0.12f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
