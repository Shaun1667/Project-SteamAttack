using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NGH.EditorTools
{
    /// <summary>
    /// 경복궁 마당 바닥을 조선 궁궐식으로 여러 장의 타일로 나눠 깐다 (텍스처를 통짜로 늘려 붙이면 깨지므로).
    ///  - 박석(薄石) 마당: 4m × 4m 타일. Varco 로 만든 '네 변이 이어지는' 텍스처 한 장을 모든 칸에 깔고,
    ///    UV 를 월드 위치 기준으로 줘서 옆 칸·잘린 칸과도 무늬가 끊기지 않고 이어진다
    ///  - 삼도(三道): 가운데 어도(御道, 임금 길) 4m 폭이 가장 높고, 양옆 신도(臣道) 2m 폭이 한 단 낮다. 박석 마당보다 조금 솟아 있음
    ///  - 근정전 월대 아래처럼 완전히 가려지는 칸은 깔지 않는다
    ///  - 모서리 칸은 잘라서(크기를 줄여) 깔되 UV 도 같은 비율로 잘라 텍스처 밀도는 그대로 (늘어나지 않음)
    /// 기존 '궁궐 박석 마당'·'어도' 상자는 그물(렌더러)만 끄고 충돌체는 그대로 둔다 (걷는 높이·판정 유지).
    /// 메뉴: NGH/궁궐 바닥 타일 다시 깔기 — 몇 번 눌러도 이전 타일을 지우고 다시 깐다.
    /// 텍스처 교체: Materials/NGH/PalaceFloor 의 머티리얼 Base Map 에 넣으면 된다 (타일 한 장 = 텍스처 한 장, 4m).
    /// </summary>
    public static class PalaceFloorTileBuilder
    {
        const string GroundPath = "현무_세계관 (1887 정월, 멈춘 도성)/00_지면";
        const string RootName = "NGH_궁궐바닥_타일";
        const string MatDir = "Assets/Materials/NGH/PalaceFloor";
        const string MeshPath = "Assets/Materials/NGH/PalaceFloor/PalaceFloorTileMeshes.asset";
        const string ShaderName = "NGH/InkToonTextured";
        const string TexDir = "Assets/Arts/Textures/NGH/PalaceFloor";   // Varco 로 만든 이어 붙는 바닥 텍스처
        const string BaksukKey = "Baksuk_B";                              // 박석 마당 전체에 깔 텍스처 (A·C 로 바꿔 볼 수 있음)

        // 바닥 영역 (기존 박석 마당 두 장을 합친 범위)
        const float MinX = -30f, MaxX = 30f, MinZ = -38f, MaxZ = 149.3f;
        const float Tile = 4f;              // 박석 타일 한 장 (m) = 텍스처 한 장
        // 삼도
        const float EodoHalf = 2f;          // 어도 반폭 (가운데 4m)
        const float SindoWidth = 2f;        // 신도 폭 (양옆 2m씩)
        const float SamdoMinZ = -35f, SamdoMaxZ = 110.7f;   // 기존 어도가 놓인 구간
        // 높이 (윗면 y)
        const float BaksukY = 0.08f, SindoY = 0.105f, EodoY = 0.13f;
        // 깔지 않을 곳: 근정전 월대 하층 (x, z 최소·최대)
        static readonly Rect[] Skip = { Rect.MinMaxRect(-23f, 12.4f, 23f, 48.4f) };
        // 박석을 깔 영역 (x, z 최소·최대): 원래 박석 마당 + 궁성 담장(동·서·남·북) 안쪽까지 꽉 채운 마당
        //  담장 안쪽 면: 서 x -42.2, 동 x 42.2, 남 z -39.3, 북 z 77.2 (01_경복궁_궁성/담장 석축)
        static readonly Rect[] Fill =
        {
            Rect.MinMaxRect(MinX, MinZ, MaxX, MaxZ),
            Rect.MinMaxRect(-42.2f, -39.3f, 42.2f, 77.2f),
        };

        enum Kind { Baksuk, Sindo, Eodo }

        [MenuItem("NGH/궁궐 바닥 타일 다시 깔기")]
        public static void Build()
        {
            var ground = GameObject.Find(GroundPath);
            if (!ground) { Debug.LogError("[PalaceFloor] 지면 그룹을 찾지 못했습니다: " + GroundPath); return; }

            // 이전 타일 지우기
            var old = ground.transform.Find(RootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RootName);
            Undo.RegisterCreatedObjectUndo(root, "궁궐 바닥 타일");
            root.transform.SetParent(ground.transform, false);

            var mats = EnsureMaterials();
            var meshes = new MeshCache(MeshPath);

            // x 경계: 4m 격자 + 삼도 경계(±2, ±4)
            // 격자: 4m 간격(원래 마당 기준) + 채울 영역들의 경계 → 칸마다 통째로 영역 안이거나 밖
            float gx0 = MinX, gx1 = MaxX, gz0 = MinZ, gz1 = MaxZ;
            foreach (var f in Fill) { gx0 = Mathf.Min(gx0, f.xMin); gx1 = Mathf.Max(gx1, f.xMax); gz0 = Mathf.Min(gz0, f.yMin); gz1 = Mathf.Max(gz1, f.yMax); }
            var xs = new SortedSet<float>();
            for (float x = MinX - Mathf.Ceil((MinX - gx0) / Tile) * Tile; x <= gx1 + 0.001f; x += Tile)
                if (x >= gx0 - 0.001f) xs.Add(Mathf.Round(x * 1000f) / 1000f);
            foreach (var x in new[] { -EodoHalf - SindoWidth, -EodoHalf, EodoHalf, EodoHalf + SindoWidth }) xs.Add(x);
            var zset = new SortedSet<float>();
            for (float z = MinZ - Mathf.Ceil((MinZ - gz0) / Tile) * Tile; z <= gz1 + 0.001f; z += Tile)
                if (z >= gz0 - 0.001f) zset.Add(Mathf.Round(z * 1000f) / 1000f);
            foreach (var f in Fill) { xs.Add(f.xMin); xs.Add(f.xMax); zset.Add(f.yMin); zset.Add(f.yMax); }
            var zs = new List<float>(zset);
            var xl = new List<float>(xs);

            int count = 0, skipped = 0;
            for (int iz = 0; iz < zs.Count - 1; iz++)
            {
                float z0 = zs[iz], z1 = zs[iz + 1];
                for (int ix = 0; ix < xl.Count - 1; ix++)
                {
                    float x0 = xl[ix], x1 = xl[ix + 1];
                    var cell = Rect.MinMaxRect(x0, z0, x1, z1);
                    float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
                    bool inFill = false;
                    foreach (var f in Fill) if (f.Contains(new Vector2(cx, cz))) { inFill = true; break; }
                    if (!inFill) continue;
                    if (FullyInsideSkip(cell)) { skipped++; continue; }

                    bool inSamdoZ = cz > SamdoMinZ && cz < SamdoMaxZ;
                    Kind kind = Kind.Baksuk;
                    if (inSamdoZ && Mathf.Abs(cx) < EodoHalf) kind = Kind.Eodo;
                    else if (inSamdoZ && Mathf.Abs(cx) < EodoHalf + SindoWidth) kind = Kind.Sindo;

                    float w = x1 - x0, d = z1 - z0;
                    // UV 는 월드 위치 기준 (4m = 텍스처 1장) → 옆 타일과 무늬가 끊기지 않고 이어짐
                    float u0 = Mathf.Repeat((x0 - MinX) / Tile, 1f), v0 = Mathf.Repeat((z0 - MinZ) / Tile, 1f);
                    Material mat; string label;
                    switch (kind)
                    {
                        case Kind.Eodo: mat = mats["Eodo"]; label = "어도"; break;
                        case Kind.Sindo: mat = mats["Sindo"]; label = cx < 0 ? "신도_서" : "신도_동"; break;
                        default:

                            mat = mats[BaksukKey];   // 한 장으로 이어 깔아야 무늬가 연속됨 (변형을 섞으면 경계에서 끊김)
                            label = "박석"; break;
                    }
                    float top = kind == Kind.Eodo ? EodoY : kind == Kind.Sindo ? SindoY : BaksukY;
                    float height = top - BaksukY;   // 마당보다 솟은 만큼 옆면을 둔다

                    var go = new GameObject(label + "_" + iz.ToString("00") + "_" + ix.ToString("00"));
                    go.transform.SetParent(root.transform, false);
                    go.transform.localPosition = new Vector3(cx, top, cz);
                    go.AddComponent<MeshFilter>().sharedMesh = meshes.Get(w, d, height, u0, v0);
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = mat;
                    GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
                    count++;
                }
            }
            meshes.Save();

            // 원래 박석 마당 상자 밖으로 넓힌 곳(담장 안쪽 양옆·남쪽 가장자리)에도 같은 높이의 바닥 판정을 둔다
            // (원래 마당은 기존 상자의 충돌체를 그대로 씀)
            var main = Fill[0];
            AddFloorCollider(root.transform, "바닥판정_서", Rect.MinMaxRect(Fill[1].xMin, Fill[1].yMin, main.xMin, Fill[1].yMax));
            AddFloorCollider(root.transform, "바닥판정_동", Rect.MinMaxRect(main.xMax, Fill[1].yMin, Fill[1].xMax, Fill[1].yMax));
            AddFloorCollider(root.transform, "바닥판정_남", Rect.MinMaxRect(main.xMin, Fill[1].yMin, main.xMax, main.yMin));

            // 기존 통짜 바닥은 그물만 끔 (충돌체는 유지)
            foreach (Transform c in ground.transform)
            {
                if (!(c.name.StartsWith("궁궐 박석 마당") || c.name.StartsWith("어도"))) continue;
                var r = c.GetComponent<MeshRenderer>();
                if (r && r.enabled) { Undo.RecordObject(r, "궁궐 바닥 타일"); r.enabled = false; }
            }

            EditorSceneManager.MarkSceneDirty(ground.scene);
            Debug.Log($"[PalaceFloor] 타일 {count}장 깔기 완료 (월대 아래 {skipped}칸 생략), 메시 {meshes.Count}종");
        }

        // ───────── 육조거리 눈밭 (눈이 소복이 쌓인) ─────────
        const string RoadName = "육조거리 길";
        const string RoadRootName = "NGH_육조거리_길_타일";
        const string RoadMeshPath = "Assets/Materials/NGH/PalaceFloor/YukjoRoadTileMeshes.asset";
        const string RoadMatPath = "Assets/Materials/NGH/PalaceFloor/M_YukjoRoad_Snow.mat";
        const string RoadTexPath = "Assets/Arts/Textures/NGH/PalaceFloor/T_YukjoRoad_Snow.png";
        // 덮을 구역: 육조거리 관아·소품 전체를 감싸는 타원 (광화문 앞 z -42 부터 남쪽 끝까지)
        static readonly Vector2 SnowCenter = new Vector2(0f, -112f);
        static readonly Vector2 SnowRadius = new Vector2(90f, 70f);
        const float SnowTile = 8f;     // 타일 한 장 크기 (텍스처는 4m 마다 반복 — 월드 기준이라 이어짐)
        const float SnowTopY = 0.03f;  // 눈 지면(0)보다 살짝 위

        /// <summary>
        /// 육조거리 일대(타원 구역)를 8m 타일로 나눠 '눈이 소복이 쌓인' 텍스처를 깐다.
        /// UV 는 월드 기준(4m = 텍스처 1장)이라 타일끼리 무늬가 이어진다. 타일 가운데가 타원 안이면 깐다.
        /// 발자국이 남도록 NghFootprintSurface(같은 타원)를 붙인다. 원래 길 상자는 렌더러만 끈다.
        /// </summary>
        [MenuItem("NGH/육조거리 눈밭 타일 다시 깔기")]
        public static void BuildYukjoRoad()
        {
            var ground = GameObject.Find(GroundPath);
            var road = ground ? ground.transform.Find(RoadName) : null;
            if (!ground) { Debug.LogError("[YukjoSnow] 지면 그룹을 찾지 못했습니다"); return; }

            var old = ground.transform.Find(RoadRootName);
            if (old) Undo.DestroyObjectImmediate(old.gameObject);
            var root = new GameObject(RoadRootName);
            Undo.RegisterCreatedObjectUndo(root, "육조거리 눈밭 타일");
            root.transform.SetParent(ground.transform, false);

            var mat = AssetDatabase.LoadAssetAtPath<Material>(RoadMatPath);
            if (!mat)
            {
                mat = new Material(Shader.Find(ShaderName));
                mat.SetColor("_BaseColor", Color.white);
                mat.SetColor("_ShadowColor", new Color(0.70f, 0.73f, 0.80f));   // 눈 그늘은 살짝 푸르게
                mat.SetColor("_InkColor", new Color(0.07f, 0.07f, 0.08f));
                mat.SetFloat("_RimInk", 0.05f);
                mat.SetFloat("_Grain", 0.04f);
                AssetDatabase.CreateAsset(mat, RoadMatPath);
            }
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(RoadTexPath);
            if (tex && mat.GetTexture("_BaseMap") != tex) { mat.SetTexture("_BaseMap", tex); EditorUtility.SetDirty(mat); }

            var meshes = new MeshCache(RoadMeshPath);
            float minX = SnowCenter.x - SnowRadius.x, maxX = SnowCenter.x + SnowRadius.x;
            float minZ = SnowCenter.y - SnowRadius.y, maxZ = SnowCenter.y + SnowRadius.y;
            // 격자를 4m 배수에 맞춰 시작 → UV 시작점이 항상 0 (메시 종류가 적음)
            minX = Mathf.Floor(minX / Tile) * Tile; minZ = Mathf.Floor(minZ / Tile) * Tile;
            int count = 0;
            for (float z0 = minZ; z0 < maxZ; z0 += SnowTile)
            for (float x0 = minX; x0 < maxX; x0 += SnowTile)
            {
                float cx = x0 + SnowTile * 0.5f, cz = z0 + SnowTile * 0.5f;
                float ex = (cx - SnowCenter.x) / SnowRadius.x, ez = (cz - SnowCenter.y) / SnowRadius.y;
                if (ex * ex + ez * ez > 1f) continue;
                float u0 = Mathf.Repeat(x0 / Tile, 1f), v0 = Mathf.Repeat(z0 / Tile, 1f);
                var go = new GameObject($"눈밭_{count:0000}");
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = new Vector3(cx, SnowTopY, cz);
                go.AddComponent<MeshFilter>().sharedMesh = meshes.Get(SnowTile, SnowTile, 0f, u0, v0);
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = mat;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.ContributeGI);
                count++;
            }
            meshes.Save();

            var surf = root.AddComponent<NGH.NghFootprintSurface>();
            surf.Recalculate();
            surf.topY = SnowTopY;
            surf.ellipseCenter = SnowCenter; surf.ellipseRadius = SnowRadius;
            surf.printColor = new Color(0.62f, 0.66f, 0.74f, 0.75f);   // 하얀 눈에 눌린 자국은 푸르스름한 그늘색
            if (road)
            {
                var rr = road.GetComponent<MeshRenderer>();
                if (rr && rr.enabled) { Undo.RecordObject(rr, "육조거리 눈밭 타일"); rr.enabled = false; }
            }
            EditorSceneManager.MarkSceneDirty(ground.scene);
            Debug.Log($"[YukjoSnow] 눈밭 타일 {count}장 ({SnowTile}m), 발자국 영역 타원 {SnowCenter} ± {SnowRadius}");
        }

        // 윗면이 박석 높이(BaksukY)인 보이지 않는 바닥 판정 상자
        static void AddFloorCollider(Transform parent, string name, Rect r)
        {
            if (r.width <= 0.01f || r.height <= 0.01f) return;
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(r.center.x, BaksukY - 0.05f, r.center.y);
            var box = go.AddComponent<BoxCollider>();
            box.size = new Vector3(r.width, 0.1f, r.height);
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
        }

        static bool FullyInsideSkip(Rect cell)
        {
            foreach (var s in Skip)
                if (cell.xMin >= s.xMin && cell.xMax <= s.xMax && cell.yMin >= s.yMin && cell.yMax <= s.yMax) return true;
            return false;
        }

        static Dictionary<string, Material> EnsureMaterials()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials/NGH")) AssetDatabase.CreateFolder("Assets/Materials", "NGH");
            if (!AssetDatabase.IsValidFolder(MatDir)) AssetDatabase.CreateFolder("Assets/Materials/NGH", "PalaceFloor");
            var shader = Shader.Find(ShaderName);
            // 박석 A·B·C 는 Varco 로 만든 변형 (기본은 B 를 씀 — 이음매가 가장 고름). 어도·신도는 길 전용 텍스처
            var defs = new[] { "Baksuk_A", "Baksuk_B", "Baksuk_C", "Sindo", "Eodo" };
            var result = new Dictionary<string, Material>();
            foreach (var key in defs)
            {
                string path = $"{MatDir}/M_PalaceFloor_{key}.mat";
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (!m)
                {
                    m = new Material(shader);
                    m.SetColor("_ShadowColor", new Color(0.60f, 0.58f, 0.57f));
                    m.SetColor("_InkColor", new Color(0.07f, 0.07f, 0.08f));
                    m.SetFloat("_RimInk", 0.1f);
                    AssetDatabase.CreateAsset(m, path);
                }
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexDir}/T_PalaceFloor_{key}.png");
                if (tex && m.GetTexture("_BaseMap") != tex)
                {
                    m.SetTexture("_BaseMap", tex);
                    m.SetColor("_BaseColor", new Color(0.95f, 0.95f, 0.95f));   // 텍스처 본래 색 (살짝만 눌러 수묵 톤에 맞춤)
                    m.SetFloat("_Grain", 0.06f);                                  // 텍스처에 결이 있으니 종이결은 약하게
                    EditorUtility.SetDirty(m);
                }
                result[key] = m;
            }
            return result;
        }

        /// <summary>크기·돌림별 타일 메시를 하나씩만 만들어 한 에셋 파일에 모아 저장</summary>
        class MeshCache
        {
            readonly Dictionary<string, Mesh> map = new Dictionary<string, Mesh>();
            readonly string path;
            public MeshCache(string assetPath) { path = assetPath; }
            public int Count => map.Count;

            public Mesh Get(float w, float d, float h, float u0, float v0)
            {
                string key = $"Tile_{w:0.###}x{d:0.###}_h{h:0.###}_u{u0:0.###}_v{v0:0.###}";
                if (map.TryGetValue(key, out var m)) return m;
                m = Build(w, d, h, u0, v0);
                m.name = key;
                map[key] = m;
                return m;
            }

            public void Save()
            {
                var existing = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (existing)
                {
                    // 같은 이름이면 기존 하위 메시를 재사용해 참조를 유지
                    var olds = new Dictionary<string, Mesh>();
                    foreach (var o in AssetDatabase.LoadAllAssetsAtPath(path)) if (o is Mesh om) olds[om.name] = om;
                    foreach (var kv in map)
                    {
                        if (olds.TryGetValue(kv.Key, out var om)) { EditorUtility.CopySerialized(kv.Value, om); Replace(kv.Value, om); }
                        else AssetDatabase.AddObjectToAsset(kv.Value, path);
                    }
                    // 이번 배치에서 안 쓰는 옛 메시는 정리 (주 에셋은 남김)
                    var main = AssetDatabase.LoadMainAssetAtPath(path);
                    foreach (var kv in olds)
                        if (!map.ContainsKey(kv.Key) && kv.Value != main) AssetDatabase.RemoveObjectFromAsset(kv.Value);
                }
                else
                {
                    bool first = true;
                    foreach (var kv in map)
                    {
                        if (first) { AssetDatabase.CreateAsset(kv.Value, path); first = false; }
                        else AssetDatabase.AddObjectToAsset(kv.Value, path);
                    }
                }
                AssetDatabase.SaveAssets();
            }

            // 새로 만든 임시 메시를 쓰던 MeshFilter 를 기존 에셋 메시로 바꿈
            static void Replace(Mesh temp, Mesh asset)
            {
                foreach (var mf in Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include))
                    if (mf.sharedMesh == temp) mf.sharedMesh = asset;
            }

            // 윗면(+옆면 h>0) 판. 피벗 = 윗면 가운데. UV 는 4m = 1 이라 잘린 칸도 텍스처가 늘어나지 않음
            static Mesh Build(float w, float d, float h, float u0, float v0)
            {
                var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
                float hx = w * 0.5f, hz = d * 0.5f;
                float uMax = w / Tile, vMax = d / Tile;
                // 윗면
                int b = v.Count;
                v.Add(new Vector3(-hx, 0, -hz)); v.Add(new Vector3(-hx, 0, hz)); v.Add(new Vector3(hx, 0, hz)); v.Add(new Vector3(hx, 0, -hz));
                uv.Add(new Vector2(u0, v0)); uv.Add(new Vector2(u0, v0 + vMax)); uv.Add(new Vector2(u0 + uMax, v0 + vMax)); uv.Add(new Vector2(u0 + uMax, v0));
                for (int i = 0; i < 4; i++) n.Add(Vector3.up);
                tri.AddRange(new[] { b, b + 1, b + 2, b, b + 2, b + 3 });
                // 옆면 (솟은 길만)
                if (h > 0.001f)
                {
                    void Side(Vector3 a, Vector3 c, Vector3 normal, float len)
                    {
                        int s = v.Count;
                        v.Add(a); v.Add(a + Vector3.down * h); v.Add(c + Vector3.down * h); v.Add(c);
                        float hv = h / Tile, lu = len / Tile;
                        uv.Add(new Vector2(0, hv)); uv.Add(new Vector2(0, 0)); uv.Add(new Vector2(lu, 0)); uv.Add(new Vector2(lu, hv));
                        for (int i = 0; i < 4; i++) n.Add(normal);
                        tri.AddRange(new[] { s, s + 2, s + 1, s, s + 3, s + 2 });   // 바깥에서 보이는 방향
                    }
                    Side(new Vector3(-hx, 0, hz), new Vector3(-hx, 0, -hz), Vector3.left, d);
                    Side(new Vector3(hx, 0, -hz), new Vector3(hx, 0, hz), Vector3.right, d);
                    Side(new Vector3(-hx, 0, -hz), new Vector3(hx, 0, -hz), Vector3.back, w);
                    Side(new Vector3(hx, 0, hz), new Vector3(-hx, 0, hz), Vector3.forward, w);
                }
                var m = new Mesh();
                m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0);
                m.RecalculateBounds(); m.RecalculateTangents();
                return m;
            }
        }
    }
}
