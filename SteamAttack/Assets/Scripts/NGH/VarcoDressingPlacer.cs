#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Environment/Place Remaining Varco Dressing (Open Scene)
    /// 바르코 플로우에서 만들었지만 씬에 없던 모델(Arts/Models/Varco3D/*)을 구역별로 맵 분위기에 맞게 배치합니다.
    ///  - 플레이어가 지나가는 가운데 길(|x| &lt; Corridor)은 비움
    ///  - 구역 사각형 안에서 시드 랜덤으로 자리를 고르고, 기존 물체(테두리)·이미 놓은 것과 겹치면 다른 자리를 고름
    ///  - 건물은 기존 육조거리 관아(VARCO_PR_YukjoOffice)의 방향을 기준으로 길 쪽을 보게 돌림
    ///  - 결과는 NGH_VarcoDressing 아래 구역별 묶음, 이름은 VARCO_&lt;모델이름&gt;. 다시 실행하면 지우고 새로 놓음
    ///  - Remove 메뉴로 통째로 지움
    /// </summary>
    public static class VarcoDressingPlacer
    {
        const string Dir = "Assets/Arts/Models/Varco3D";
        const string RootName = "NGH_VarcoDressing";
        const float Corridor = 14f;
        const int Seed = 1887;

        enum Fit { Max, Height }
        enum Face { Random, Road, Fixed }

        struct Rect2 { public float x0, x1, z0, z1; public Rect2(float a, float b, float c, float d) { x0 = a; x1 = b; z0 = c; z1 = d; } }

        class Item
        {
            public string name; public string zone; public Fit fit; public float size; public int count; public Face face; public float yaw;
            public Vector2? at;
        }

        static readonly Dictionary<string, Rect2[]> Zones = new Dictionary<string, Rect2[]>
        {
            // 궁 안(담장 x ±43, z -40~78)에는 아무것도 두지 않음. 궁 안에 둘 법한 것도 광화문 남쪽 육조거리로
            { "육조거리_길가", new[] { new Rect2(-40, -12, -124, -47), new Rect2(12, 40, -124, -47) } },
            { "육조거리_바깥", new[] { new Rect2(-66, -28, -140, -46), new Rect2(28, 66, -140, -46) } },
            { "육조거리_뒤편마을", new[] { new Rect2(-100, -44, -140, -48), new Rect2(44, 100, -140, -48) } },
            { "궁궐_서쪽밖", new[] { new Rect2(-120, -52, -20, 85) } },
            { "궁궐_동쪽밖", new[] { new Rect2(52, 120, -20, 85) } },
        };

        static Item I(string n, string zone, Fit f, float size, int count = 1, Face face = Face.Random, float yaw = 0f, Vector2? at = null)
            => new Item { name = n, zone = zone, fit = f, size = size, count = count, face = face, yaw = yaw, at = at };

        static readonly Item[] Items =
        {
            // 고정 자리
            I("PR_Gyeonghoeru", "궁궐_서쪽밖", Fit.Max, 34, 1, Face.Road, 0, new Vector2(-88, 40)),
            I("PR_Dongsipjagak", "궁궐_동쪽밖", Fit.Max, 11, 1, Face.Fixed, 0, new Vector2(56, -50)),
            I("PR_Sajeongjeon", "육조거리_바깥", Fit.Max, 18, 1, Face.Road),
            I("PR_Gangnyeongjeon", "육조거리_바깥", Fit.Max, 20, 1, Face.Road),
            I("PR_FogGatePillar", "육조거리_길가", Fit.Height, 7, 1, Face.Fixed, 0, new Vector2(-4.5f, -87)),
            I("PR_FogGatePillar_02", "육조거리_길가", Fit.Height, 7, 1, Face.Fixed, 0, new Vector2(4.5f, -87)),
            I("PR_Haenggak_02", "육조거리_바깥", Fit.Max, 24, 1, Face.Road),

            // 육조거리 길가 (얼어붙은 저잣거리)
            I("PR_Jumak", "육조거리_길가", Fit.Max, 12, 1, Face.Road),
            I("PR_MarketStall", "육조거리_길가", Fit.Max, 3, 3, Face.Road),
            I("PR_Gama", "육조거리_길가", Fit.Max, 2, 1),
            I("PR_Pyeongsang", "육조거리_길가", Fit.Max, 1.8f, 2),
            I("PR_Chests", "육조거리_길가", Fit.Max, 1.3f, 2),
            I("PR_Jangdokdae", "육조거리_길가", Fit.Max, 3, 1),
            I("PR_Well", "육조거리_길가", Fit.Max, 2.5f, 1),
            I("PR_Firewood", "육조거리_길가", Fit.Max, 1.6f, 2),
            I("PR_StrawStack", "육조거리_길가", Fit.Height, 1.8f, 2),
            I("PR_Cheongsachorong", "육조거리_길가", Fit.Height, 1.4f, 3),
            I("PR_TorchStand", "육조거리_길가", Fit.Height, 2.5f, 3),
            I("PR_Brazier", "육조거리_길가", Fit.Max, 1.2f, 2),
            I("PR_TelegraphPole", "육조거리_길가", Fit.Height, 8, 3),
            I("PR_FrozenPojol_Ice", "육조거리_길가", Fit.Height, 2.3f, 1, Face.Road),
            I("PR_FrozenPackHorse", "육조거리_길가", Fit.Max, 2.8f, 1),
            I("PR_SnowDrift", "육조거리_길가", Fit.Max, 4, 3),
            I("PR_Stones_Snow", "육조거리_길가", Fit.Max, 1.5f, 2),
            I("PR_IceShards_Scattered", "육조거리_길가", Fit.Max, 4, 2),
            I("PR_BlackWaterBanner", "육조거리_길가", Fit.Height, 6, 2),
            I("PR_BlackWaterLantern", "육조거리_길가", Fit.Height, 1, 2),
            I("PR_Weapon_Chang", "육조거리_길가", Fit.Max, 2, 1),
            I("PR_Weapon_Dangpa", "육조거리_길가", Fit.Max, 2.2f, 1),
            I("PR_Weapon_Gonbong", "육조거리_길가", Fit.Max, 1.2f, 1),
            I("PR_Weapon_Deungpae", "육조거리_길가", Fit.Max, 0.9f, 1),
            I("PR_Weapon_Chongtong", "육조거리_길가", Fit.Max, 1, 1),
            I("PR_Weapon_Hwando", "육조거리_길가", Fit.Max, 1, 1),
            I("PR_Bimangnok", "육조거리_길가", Fit.Max, 0.3f, 1),
            I("PR_Yakcheop", "육조거리_길가", Fit.Max, 0.5f, 1),

            // 육조거리 뒤편 마을
            I("PR_Chogajip", "육조거리_뒤편마을", Fit.Max, 12, 2, Face.Road),
            I("PR_ChogaHut", "육조거리_뒤편마을", Fit.Max, 7, 2, Face.Road),
            I("PR_Giwajip", "육조거리_뒤편마을", Fit.Max, 12, 2, Face.Road),
            I("PR_Podocheong", "육조거리_뒤편마을", Fit.Max, 22, 1, Face.Road),
            I("PR_SoseulDaemun", "육조거리_뒤편마을", Fit.Max, 7, 1, Face.Road),
            I("PR_BellPavilion", "육조거리_뒤편마을", Fit.Max, 12, 1, Face.Road),
            I("PR_Mangru", "육조거리_뒤편마을", Fit.Height, 8, 1),
            I("PR_Nugak", "육조거리_뒤편마을", Fit.Height, 6, 1),
            I("PR_StoneBridge", "육조거리_뒤편마을", Fit.Max, 8, 1),
            I("PR_YeonjaMill", "육조거리_뒤편마을", Fit.Max, 4, 1),
            I("PR_Jangseung", "육조거리_뒤편마을", Fit.Height, 3, 1, Face.Road),
            I("PR_Sotdae", "육조거리_뒤편마을", Fit.Height, 6, 1),
            I("PR_SsariFence", "육조거리_뒤편마을", Fit.Max, 4, 4),
            I("PR_Doldam", "육조거리_뒤편마을", Fit.Max, 5, 4),
            I("PR_Saripmun", "육조거리_뒤편마을", Fit.Max, 2.4f, 2, Face.Road),
            I("PR_Willow", "육조거리_뒤편마을", Fit.Height, 9, 2),
            I("PR_Zelkova", "육조거리_뒤편마을", Fit.Height, 13, 2),
            I("PR_Bamboo", "육조거리_뒤편마을", Fit.Height, 6, 3),
            I("PR_Boulder_Snow", "육조거리_뒤편마을", Fit.Max, 2.5f, 3),
            I("PR_FlatRocks_Snow", "육조거리_뒤편마을", Fit.Max, 2.5f, 2),
            I("PR_Weapon_Gakgung", "육조거리_뒤편마을", Fit.Max, 1.2f, 1),
            I("PR_Weapon_Arrow", "육조거리_뒤편마을", Fit.Max, 0.9f, 2),

            // 궁궐 남쪽 마당 (근정전 앞, 길 양옆)
            I("PR_Deumeu", "육조거리_길가", Fit.Max, 1.6f, 4),
            I("PR_StoneLamp", "육조거리_길가", Fit.Height, 2.5f, 2),
            I("PR_Angbuilgu", "육조거리_길가", Fit.Max, 1.2f, 1),
            I("PR_Cheugugi", "육조거리_길가", Fit.Height, 1.3f, 1),
            I("PR_Jagyeongnu", "육조거리_바깥", Fit.Height, 5, 1, Face.Road),
            I("PR_Honcheonui", "육조거리_길가", Fit.Height, 2.5f, 1),
            I("PR_Woldae", "육조거리_바깥", Fit.Max, 6, 4, Face.Road),
            I("PR_Dapdo", "육조거리_바깥", Fit.Max, 6, 1, Face.Road),
            I("PR_Cheonrok", "육조거리_길가", Fit.Max, 2, 2, Face.Road),
            I("PR_Yeongjegyo", "육조거리_바깥", Fit.Max, 12, 1, Face.Road),
            I("PR_Dansang", "육조거리_바깥", Fit.Max, 6, 1),
            I("PR_HyeonmuPlate", "육조거리_길가", Fit.Max, 2.5f, 2),
            I("PR_HyeonmuLeg", "육조거리_바깥", Fit.Max, 4, 1),
            I("PR_HyeonmuHatch", "육조거리_바깥", Fit.Max, 4, 1),
            I("PR_HyeonmuCore", "육조거리_길가", Fit.Max, 1.5f, 1),
            I("PR_RoofDebris", "육조거리_바깥", Fit.Max, 5, 2),
            I("PR_IcePillar_Boss", "육조거리_바깥", Fit.Height, 6, 2),

            // 물불 발전기 주변 (증기 기관 부품)
            I("PR_Boiler", "육조거리_바깥", Fit.Max, 7, 1, Face.Road),
            I("PR_SteamPanel", "육조거리_길가", Fit.Height, 2.5f, 1, Face.Road),
            I("PR_SteamPipe", "육조거리_길가", Fit.Max, 2, 4),
            I("PR_SteamPipeElbow", "육조거리_길가", Fit.Max, 1.2f, 2),
            I("PR_SteamRefill", "육조거리_길가", Fit.Height, 2.5f, 2, Face.Road),
            I("PR_CoalPile", "육조거리_길가", Fit.Max, 1.6f, 2),
            I("PR_SteamCanister", "육조거리_길가", Fit.Max, 0.5f, 3),
            I("PR_BronzeGear_01", "육조거리_길가", Fit.Max, 1.6f, 1),
            I("PR_BronzeGear_02", "육조거리_길가", Fit.Max, 1.4f, 1),
            I("PR_BronzeGear_03", "육조거리_길가", Fit.Max, 1.2f, 1),
            I("PR_BronzeGear_04", "육조거리_길가", Fit.Max, 1.0f, 1),
            I("PR_BronzeGear_05", "육조거리_길가", Fit.Max, 1.2f, 1),
            I("PR_BronzeGear_06", "육조거리_길가", Fit.Max, 1.0f, 1),
            I("PR_BronzeGear_07", "육조거리_길가", Fit.Max, 1.4f, 1),

            // 임금 곁 (건청궁 앞)
            I("PR_Gyojiham", "육조거리_길가", Fit.Max, 0.6f, 1),
            I("PR_Eobo", "육조거리_길가", Fit.Max, 0.3f, 1),
            I("PR_EdictScroll", "육조거리_길가", Fit.Max, 0.8f, 1),
            I("PR_Bandaji", "육조거리_길가", Fit.Max, 1.2f, 1, Face.Road),

            // 궁궐 밖
            I("PR_Lotus", "궁궐_서쪽밖", Fit.Max, 2, 5),
            I("PR_Willow", "궁궐_서쪽밖", Fit.Height, 9, 1),
            I("PR_Zelkova", "궁궐_동쪽밖", Fit.Height, 13, 1),
            I("PR_Bamboo", "궁궐_동쪽밖", Fit.Height, 6, 2),
        };

        [MenuItem("Tools/NGH/Environment/Place Remaining Varco Dressing (Open Scene)")]
        public static void Place()
        {
            var old = GameObject.Find(RootName);
            if (old != null) Undo.DestroyObjectImmediate(old);

            // 기존 물체 테두리 (평평한 것·아주 큰 것·땅은 뺌)
            var obstacles = new List<Bounds>();
            foreach (var r in Object.FindObjectsByType<Renderer>(FindObjectsInactive.Exclude))
            {
                if (!r.enabled || r is ParticleSystemRenderer || r is TrailRenderer || r is LineRenderer) continue;
                var b = r.bounds;
                if (b.size.y < 0.3f || Mathf.Max(b.size.x, b.size.z) > 120f) continue;
                obstacles.Add(b);
            }

            // 관아 방향: 길 서쪽(x&lt;0) 건물이 길을 보는 yaw
            float westYaw = 90f;
            var office = GameObject.Find("VARCO_PR_YukjoOffice");
            if (office != null) westYaw = office.transform.eulerAngles.y + (office.transform.position.x < 0 ? 0f : 180f);

            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "Place Varco Dressing");
            var zoneRoots = new Dictionary<string, Transform>();
            var rng = new System.Random(Seed);
            var mats = new Dictionary<string, Material>();
            int placed = 0; var skipped = new List<string>();

            foreach (var it in Items)
            {
                string path = $"{Dir}/{it.name}/{it.name}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (model == null) { skipped.Add(it.name + "(모델 없음)"); continue; }
                if (!mats.TryGetValue(it.name, out var mat)) mats[it.name] = mat = PrepareMaterial(path, it.name);
                if (!zoneRoots.TryGetValue(it.zone, out var zr))
                {
                    zr = new GameObject(it.zone).transform; zr.SetParent(root.transform, false); zoneRoots[it.zone] = zr;
                }

                for (int n = 0; n < it.count; n++)
                {
                    var inst = (GameObject)PrefabUtility.InstantiatePrefab(model, zr);
                    inst.name = "VARCO_" + it.name;
                    foreach (var mr in inst.GetComponentsInChildren<MeshRenderer>())
                    {
                        var slots = new Material[mr.sharedMaterials.Length];
                        for (int i = 0; i < slots.Length; i++) slots[i] = mat;
                        mr.sharedMaterials = slots;
                    }
                    var baseRot = model.transform.localRotation;
                    inst.transform.localScale = Vector3.one;
                    inst.transform.rotation = baseRot;
                    var b0 = Bounds(inst);
                    float unit = it.fit == Fit.Height ? b0.size.y : Mathf.Max(b0.size.x, Mathf.Max(b0.size.y, b0.size.z));
                    float s = it.size / Mathf.Max(unit, 0.0001f) * (it.count > 1 ? (float)(0.9 + rng.NextDouble() * 0.2) : 1f);
                    inst.transform.localScale = Vector3.one * s;

                    bool ok = false;
                    for (int attempt = 0; attempt < 300 && !ok; attempt++)
                    {
                        Vector2 p;
                        if (it.at.HasValue) { p = it.at.Value; if (attempt > 0) p += new Vector2((float)(rng.NextDouble() - 0.5) * attempt * 0.4f, (float)(rng.NextDouble() - 0.5) * attempt * 0.4f); }
                        else
                        {
                            var rects = Zones[it.zone];
                            var rc = rects[rng.Next(rects.Length)];
                            p = new Vector2(Mathf.Lerp(rc.x0, rc.x1, (float)rng.NextDouble()), Mathf.Lerp(rc.z0, rc.z1, (float)rng.NextDouble()));
                        }
                        float yaw = it.face switch
                        {
                            Face.Road => p.x < 0 ? westYaw : westYaw + 180f,
                            Face.Fixed => it.yaw,
                            _ => (float)(rng.NextDouble() * 360.0),
                        };
                        inst.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * baseRot;
                        inst.transform.position = Vector3.zero;
                        var b = Bounds(inst);
                        inst.transform.position = new Vector3(p.x - b.center.x, -b.min.y - b.size.y * 0.02f, p.y - b.center.z);
                        b = Bounds(inst);

                        bool pathBlocked = !it.at.HasValue && b.min.x < Corridor && b.max.x > -Corridor && b.max.z > -125f && b.min.z < 85f;
                        if (pathBlocked) continue;
                        // 궁 안(담장 안쪽)에는 걸치지도 않게
                        bool inPalace = b.min.x < 45f && b.max.x > -45f && b.min.z < 80f && b.max.z > -42f;
                        if (inPalace) continue;
                        var test = b; test.Expand(new Vector3(1f, 0f, 1f));
                        bool hit = false;
                        foreach (var o in obstacles)
                            if (test.min.x < o.max.x && test.max.x > o.min.x && test.min.z < o.max.z && test.max.z > o.min.z) { hit = true; break; }
                        if (hit && !(it.at.HasValue && attempt > 200)) continue;
                        obstacles.Add(b);
                        ok = true;
                    }
                    if (!ok) { Object.DestroyImmediate(inst); skipped.Add(it.name); continue; }
                    GameObjectUtility.SetStaticEditorFlags(inst, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    placed++;
                }
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log($"[NGH] 바르코 소품 배치: {placed}개" + (skipped.Count > 0 ? $", 자리 못 찾음: {string.Join(", ", skipped)}" : ""));
        }

        [MenuItem("Tools/NGH/Environment/Remove Placed Varco Dressing (Open Scene)")]
        public static void Remove()
        {
            var old = GameObject.Find(RootName);
            if (old != null) { Undo.DestroyObjectImmediate(old); EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene()); }
        }

        static Bounds Bounds(GameObject go)
        {
            var rs = go.GetComponentsInChildren<Renderer>();
            var b = rs[0].bounds; foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        static Material PrepareMaterial(string modelPath, string name)
        {
            string dir = Path.GetDirectoryName(modelPath).Replace('\\', '/');
            string matPath = $"{dir}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat != null && mat.GetTexture("_BaseMap") != null) return mat;

            var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
            importer.ExtractTextures(dir);
            AssetDatabase.Refresh();
            Texture2D baseTex = null, normal = null;
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { dir }))
            {
                var p = AssetDatabase.GUIDToAssetPath(guid);
                if (Path.GetDirectoryName(p).Replace('\\', '/') != dir) continue;
                var lower = Path.GetFileNameWithoutExtension(p).ToLowerInvariant();
                if (lower.Contains("normal"))
                {
                    var ti = (TextureImporter)AssetImporter.GetAtPath(p);
                    if (ti.textureType != TextureImporterType.NormalMap) { ti.textureType = TextureImporterType.NormalMap; ti.SaveAndReimport(); }
                    normal = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
                }
                else if (!lower.Contains("orm") && !lower.Contains("rough") && !lower.Contains("metal") && baseTex == null)
                    baseTex = AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetTexture("_BaseMap", baseTex);
            if (normal != null) { mat.SetTexture("_BumpMap", normal); mat.EnableKeyword("_NORMALMAP"); }
            mat.SetFloat("_Smoothness", 0.2f);
            mat.SetFloat("_Metallic", 0f);
            EditorUtility.SetDirty(mat);
            AssetDatabase.SaveAssets();
            return mat;
        }
    }
}
#endif
