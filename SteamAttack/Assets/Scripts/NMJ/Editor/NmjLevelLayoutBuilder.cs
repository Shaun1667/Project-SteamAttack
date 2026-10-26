using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace NMJ.EditorTools
{
    /// <summary>
    /// Common/Main 시연 동선 레벨 배치 v2 (5분 시연 빌드 v6 기준 개정본 4장).
    ///
    /// 한 구간 = 한 가지만 가르친다. 앞 구간 적이 뒤 구간까지 끌려오지 않게 구간 사이를 띄운다.
    ///   z -108 시작        → -102 허수아비(증기 충전 연습, 적 감지 밖)
    ///   z  -97 괘서         → -93 얼어붙은 어미와 아이 (멈춘 도성 소개)
    ///   z  -86 첫 전투: 포졸 2 (뒤집힌 수레 = 엄폐물)
    ///   z  -81 비망록 (첫 사격 직전에 "왜 증기로 쏘나")
    ///   z  -76 누각 위 궁수 → 사격 배우기
    ///   z  -70 포졸 1 (내려치기) → 첫 받아치기
    ///   z  -64 광화문 앞 도착
    ///   z  -55 광화문 전투: 포졸 2 + 마지막 포졸(문 바로 앞) + 해태 뒤 궁수 → 얼음 문
    ///   궁 안: 근정전 옆길을 넓히고, 푸른 '시간 부스러기'를 따라 동쪽으로 → 향원정 안개문
    ///         동쪽 담 너머로 현무의 꼬리가 보인다
    ///
    /// 메뉴: SteamAttack ▸ NMJ ▸ 레벨 배치 v2 적용 (여러 번 눌러도 같은 결과)
    /// 바꾸는 곳: NMJ_EnemySpawns·NMJ_Story 프리팹, 새 NMJ_LevelDressing 프리팹, Main의 NMJ 월드(현무_세계관) 일부
    /// </summary>
    public static class NmjLevelLayoutBuilder
    {
        const string EnemyPrefab = "Assets/Prefabs/NMJ/NMJ_EnemySpawns.prefab";
        const string StoryPrefab = "Assets/Prefabs/NMJ/NMJ_Story.prefab";
        const string DressingPrefab = "Assets/Prefabs/NMJ/NMJ_LevelDressing.prefab";
        const string QuestZonePrefab = "Assets/Prefabs/NMJ/NMJ_QuestZones.prefab";
        // 보스방(향원정) 안개문 — 근정전 계단(답도, z 36.7~) 바로 앞 마당. 궁 안 이동 147m(49초) → 64m(21초)
        static readonly Vector3 FogGatePos = new Vector3(0f, 0f, 33f);
        static readonly Vector3 FogReturnPos = new Vector3(0f, 0.1f, 29f);   // 보스방에서 돌아오는 자리 (문을 등지고 선다)
        const string MatDir = "Assets/Materials/NMJ/HyeonmuWorld/";
        const float PocheolWander = 2.5f;   // 기본 6m면 옆 구간까지 걸어가 구간이 섞인다

        const string MainScenePath = "Assets/Scenes/Common/Main.unity";
        static readonly List<string> Missing = new List<string>();

        [MenuItem("SteamAttack/NMJ/레벨 배치 v2 적용")]
        public static void Apply()
        {
            if (EditorApplication.isPlaying) { EditorUtility.DisplayDialog("레벨 배치 v2", "Play를 멈춘 뒤에 눌러 주세요.", "확인"); return; }

            // Main이 안 열려 있으면 열어 준다 (예전엔 오류 한 줄만 남기고 조용히 끝나 '안 된다'로 보였다)
            var world = FindWorld();
            if (world == null)
            {
                if (!EditorUtility.DisplayDialog("레벨 배치 v2", "공용 씬 Common/Main을 열고 적용할까요?", "열고 적용", "취소")) return;
                if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
                EditorSceneManager.OpenScene(MainScenePath, OpenSceneMode.Single);
                world = FindWorld();
                if (world == null) { EditorUtility.DisplayDialog("레벨 배치 v2", "Main 씬에 '현무_세계관'이 없습니다. 최신 내용을 Pull 했는지 확인하세요.", "확인"); return; }
            }
            var scene = world.gameObject.scene;
            UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
            Missing.Clear();

            ApplyEnemies();
            ApplyStory();
            ApplyQuestZones();
            ApplyWorld(world);
            BuildDressing(world);
            // 씬에서 배치 묶음을 지웠거나 덮어쓴 경우도 프리팹 그대로 되돌린다
            EnsureInstance(scene, EnemyPrefab);
            EnsureInstance(scene, StoryPrefab);
            EnsureInstance(scene, QuestZonePrefab);
            EnsureInstance(scene, DressingPrefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();

            string msg = "배치 v2를 적용하고 Main 씬을 저장했습니다.";
            if (Missing.Count > 0) msg += "\n\n못 찾은 것 " + Missing.Count + "개 (Console 참고):\n" + string.Join("\n", Missing.GetRange(0, Mathf.Min(8, Missing.Count)));
            Debug.Log("[NMJ 레벨] " + msg);
            EditorUtility.DisplayDialog("레벨 배치 v2", msg, "확인");
        }

        // ---------------------------------------------------------------- 적
        static void ApplyEnemies()
        {
            var root = PrefabUtility.LoadPrefabContents(EnemyPrefab);
            var p = new Dictionary<string, Vector3>
            {
                { "육조거리/변이포졸_육조_1", new Vector3(-4f, 0.1f, -86f) },
                { "육조거리/변이포졸_육조_2", new Vector3(4f, 0.1f, -85f) },
                { "육조거리/궁수자리_육조거리(누각)", new Vector3(13f, 0f, -76f) },
                { "육조거리/흑수회궁수_육조", new Vector3(13f, 3.1f, -76f) },
                { "육조거리/변이포졸_육조_3_받아치기", new Vector3(0f, 0.1f, -70f) },
                { "광화문/변이포졸_광화문_1", new Vector3(-6f, 0.1f, -55f) },
                { "광화문/변이포졸_광화문_2", new Vector3(6f, 0.1f, -55f) },
                { "광화문/변이포졸_광화문_3", new Vector3(0f, 0.1f, -51f) },   // 마지막 한 명: 얼음 문 바로 앞
            };
            foreach (var kv in p)
            {
                var t = root.transform.Find(kv.Key);
                if (t == null) { Miss("적 " + kv.Key); continue; }
                t.localPosition = kv.Value;
                if (t.name.StartsWith("변이포졸")) SetFloat(t.gameObject, "EnemyAI", "_wanderRadius", PocheolWander);
                // 문 바로 앞 마지막 포졸: 내려치기(동심원 강공격)를 자주 → 받아치기 → 경직 → 강공격을 자연스럽게 보게 (육조 받아치기 포졸은 1)
                if (t.name == "변이포졸_광화문_3") SetFloat(t.gameObject, "EnemyMeleeAttack", "_heavyChance", 0.75f);
            }
            PrefabUtility.SaveAsPrefabAsset(root, EnemyPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------------------------------------------------------------- 괘서·비망록
        static void ApplyStory()
        {
            var root = PrefabUtility.LoadPrefabContents(StoryPrefab);
            var gwaeseo = root.transform.Find("NMJ_괘서_육조거리");
            if (gwaeseo) { gwaeseo.localPosition = new Vector3(-7f, 0f, -97f); gwaeseo.localEulerAngles = new Vector3(0f, 60f, 0f); }
            var memo = root.transform.Find("NMJ_비망록_육조거리");
            if (memo) { memo.localPosition = new Vector3(-6f, 0f, -81f); memo.localEulerAngles = new Vector3(0f, 17f, 0f); }
            PrefabUtility.SaveAsPrefabAsset(root, StoryPrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------------------------------------------------------------- 월드 (Main 씬 안 NMJ 월드)
        static void ApplyWorld(Transform world)
        {
            // 적처럼 생겼지만 움직이지 않는 옛 장식 병사 — 플레이어가 적으로 착각한다
            SetActive(world, "13_인물/변이병 (한기에 중독된 군졸)", false);
            // 중간보스는 시연 범위에서 제외 (개정본 3장) — 광화문 바로 안쪽 길을 막고 있었다
            SetActive(world, "13_인물/백검(白劍) 백동수 — 가상의 인물", false);

            // 반투명 안개판 끄기 — 특히 '도성 바닥 안개'(높이 1m)가 적 다리를 흐리게 가려 읽기 어렵다
            // (안개문 FogGate는 보스방 입구 기능이라 그대로 둔다)
            foreach (var mist in new[]
            {
                "00_대기_한기와_정지된_시간/북악산 산허리 안개", "00_대기_한기와_정지된_시간/북악산 산허리 안개 2",
                "00_대기_한기와_정지된_시간/인왕산 안개", "00_대기_한기와_정지된_시간/도성 바닥 안개",
                "00_대기_한기와_정지된_시간/현무 발치의 먹안개", "07_향원정_얼어붙은_연못/연못 물안개",
            })
                SetActive(world, mist, false);

            // 근정전과 행각 사이가 1.8m뿐 → 8.8m로 (주 동선이 좁은 틈이 되지 않게)
            MoveX(world, "03_근정전/동행각", 35f);
            MoveX(world, "03_근정전/서행각", -35f);

            // 남방기 깃대가 광화문 앞 길 한가운데(0, -64)에 있어 마지막 포졸을 가리고 길을 막는다 → 동쪽 해태 옆으로
            var flag = world.Find("12_오방기/적(赤) · 남방기");
            if (flag) { Undo.RecordObject(flag, "레벨 v2"); flag.position = new Vector3(16f, flag.position.y, -62f); }

            // 궁 안: 담을 감은 꼬리만 보인다 (몸통·고리·가시는 보스 아레나에서)
            var hm = world.Find("09_현무_玄武");
            if (hm)
            {
                Undo.RecordObject(hm.gameObject, "레벨 v2");
                hm.gameObject.SetActive(true);
                foreach (Transform c in hm)
                {
                    Undo.RecordObject(c.gameObject, "레벨 v2");
                    c.gameObject.SetActive(c.name == "성벽을 감은 꼬리");
                }
            }

            // 안개문을 근정전 앞으로 당긴다 (예전 자리 z 80은 근정전 뒤를 돌아가야 해 49초 걸렸다)
            var gate2 = world.Find("07_향원정_얼어붙은_연못/NMJ_FogGate_향원정");
            if (gate2) { Undo.RecordObject(gate2, "레벨 v2"); gate2.position = FogGatePos; gate2.rotation = Quaternion.identity; }
            // 보스방에서 돌아오는 자리: 안개문 앞, 문을 등지고 (예전 z 74는 월대 모서리라 몸이 박혔다)
            var back = world.Find("07_향원정_얼어붙은_연못/NMJ_FogGate_돌아온자리");
            if (back) { Undo.RecordObject(back, "레벨 v2"); back.position = FogReturnPos; back.rotation = Quaternion.Euler(0f, 180f, 0f); }

            // 얼음 문은 광화문 앞 4명만 지킨다 (받아치기 포졸 z -70은 빼고 해태 뒤 궁수는 넣는 거리)
            var gate = world.Find("02_광화문/NMJ_광화문_얼음문");
            if (gate) SetFloat(gate.gameObject, "IceGate", "autoFindRadius", 19f);
        }

        // ---------------------------------------------------------------- 퀘스트 도착 지점
        static void ApplyQuestZones()
        {
            var root = PrefabUtility.LoadPrefabContents(QuestZonePrefab);
            var z = root.transform.Find("도달_향원정안개문");
            if (z) z.localPosition = new Vector3(FogGatePos.x, 2f, FogGatePos.z - 5f);   // 안개문 바로 앞 마당 (상자 12×4×5)
            PrefabUtility.SaveAsPrefabAsset(root, QuestZonePrefab);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // ---------------------------------------------------------------- 연출 소품 프리팹
        static void BuildDressing(Transform world)
        {
            var root = new GameObject("NMJ_LevelDressing");

            // 1) 육조거리에서 얼어붙은 백성 — 북쪽(재앙)에서 남쪽으로 달아나다 멈췄다
            var people = World(world, "13_인물/얼어붙은 백성 (운종가)");
            var g1 = Group(root, "육조거리_얼어붙은_백성");
            if (people)
            {
                CopyPerson(people, "달아나던 백성", g1, new Vector3(-11f, 0f, -90.5f), 170f, "어미");
                CopyPerson(people, "아이", g1, new Vector3(-10.2f, 0f, -91.1f), 175f, "아이");
                CopyPerson(people, "빌고 있는 백성", g1, new Vector3(9f, 0f, -99f), 230f, "빌고 있는 백성");
                CopyPerson(people, "달아나던 백성", g1, new Vector3(11f, 0f, -63f), 200f, "광화문 앞 백성");
            }

            // 2) 첫 전투의 엄폐물 — 뒤집힌 수레 (궁수 화살을 가릴 수 있다)
            var cart = World(world, "04_육조거리/버려진 수레");
            if (cart)
            {
                var c = Object.Instantiate(cart.gameObject, root.transform);
                c.name = "뒤집힌 수레 (첫 전투 엄폐)";
                c.transform.position = new Vector3(7f, 0.6f, -82f);
                c.transform.rotation = Quaternion.Euler(0f, 70f, 80f);
                var bc = c.AddComponent<BoxCollider>();
                bc.center = new Vector3(0f, 0.6f, 0f); bc.size = new Vector3(2.2f, 1.6f, 3.4f);
            }

            // 3) 궁 안 길잡이 — 푸른 시간 부스러기를 따라가면 향원정 (오방색: 청 = 시간 부스러기)
            var glow = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_GlowBlue.mat");
            var g3 = Group(root, "궁안_시간부스러기_길잡이");
            var path = new[]
            {
                new Vector3(0f, 0f, -30f), new Vector3(-2f, 0f, -5f), new Vector3(2f, 0f, 15f), new Vector3(0f, 0f, 31f),   // 어도를 따라 근정전 앞 안개문까지
            };
            int n = 0;
            for (int i = 0; i < path.Length - 1; i++)
            {
                float len = Vector3.Distance(path[i], path[i + 1]);
                int steps = Mathf.Max(1, Mathf.RoundToInt(len / 5f));
                for (int s = 0; s < steps; s++)
                {
                    var pos = Vector3.Lerp(path[i], path[i + 1], s / (float)steps);
                    var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    Object.DestroyImmediate(shard.GetComponent<Collider>());
                    shard.name = $"부스러기_{++n:00}";
                    shard.transform.SetParent(g3.transform);
                    shard.transform.position = pos + new Vector3((n % 2 == 0 ? 0.4f : -0.4f), 1.3f + (n % 3) * 0.2f, 0f);
                    shard.transform.rotation = Quaternion.Euler(45f, n * 37f, 45f);
                    shard.transform.localScale = Vector3.one * 0.18f;
                    if (glow) shard.GetComponent<Renderer>().sharedMaterial = glow;
                    shard.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    shard.AddComponent<PickupBob>();
                    SetFloat(shard, "PickupBob", "bobHeight", 0.15f);
                    SetFloat(shard, "PickupBob", "bobSpeed", 1.2f);
                    SetFloat(shard, "PickupBob", "spinDegreesPerSecond", 40f);
                }
            }
            // 꺾이는 곳마다 푸른 빛
            foreach (var at in new[] { path[1], path[3] })
            {
                var l = new GameObject("길잡이 빛").AddComponent<Light>();
                l.transform.SetParent(g3.transform);
                l.transform.position = at + Vector3.up * 2f;
                l.type = LightType.Point; l.color = new Color(0.45f, 0.75f, 1f); l.range = 9f; l.intensity = 3f;
                l.shadows = LightShadows.None;
                l.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>(); // 없으면 씬에서 덧붙인 컴포넌트로 남는다
            }

            // 4) 광화문 앞 문턱 표시 — 해태 사이 바닥에 얼음 결정 (여기서부터 전투)
            var spike = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Arts/Models/NMJ/HyeonmuWorld/Spike.asset");
            var ice = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_IceCrystal.mat");
            var g4 = Group(root, "광화문_얼음결정");
            if (spike && ice)
            {
                var spots = new[] { new Vector3(-9f, 0f, -62f), new Vector3(-10.5f, 0f, -60.5f), new Vector3(9.5f, 0f, -62f), new Vector3(10.5f, 0f, -60f), new Vector3(-3f, 0f, -45.5f), new Vector3(3.5f, 0f, -45.5f) };
                for (int i = 0; i < spots.Length; i++)
                {
                    var s = new GameObject($"얼음결정_{i + 1}");
                    s.transform.SetParent(g4.transform);
                    s.transform.position = spots[i];
                    s.transform.rotation = Quaternion.Euler((i % 3 - 1) * 12f, i * 61f, (i % 2 == 0 ? 10f : -8f));
                    s.transform.localScale = new Vector3(0.6f, 1.1f + (i % 3) * 0.4f, 0.6f);
                    s.AddComponent<MeshFilter>().sharedMesh = spike;
                    s.AddComponent<MeshRenderer>().sharedMaterial = ice;
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, DressingPrefab);
            Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- 도우미
        static Transform FindWorld()
        {
            // 씬을 여러 개 열어 둔 경우(Title·BossArena 함께)에도 찾는다
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
            {
                var sc = UnityEngine.SceneManagement.SceneManager.GetSceneAt(i);
                if (!sc.isLoaded) continue;
                foreach (var go in sc.GetRootGameObjects())
                    if (go.name.StartsWith("현무_세계관")) return go.transform;
            }
            return null;
        }

        static void Miss(string what)
        {
            Missing.Add(what);
            Debug.LogWarning("[NMJ 레벨] 못 찾음: " + what);
        }

        static Transform World(Transform world, string path) => world.Find(path);

        static void SetActive(Transform world, string path, bool on)
        {
            var t = world.Find(path);
            if (t == null) { Miss(path); return; }
            Undo.RecordObject(t.gameObject, "레벨 v2");
            t.gameObject.SetActive(on);
        }

        static void MoveX(Transform world, string path, float x)
        {
            var t = world.Find(path);
            if (t == null) { Miss(path); return; }
            Undo.RecordObject(t, "레벨 v2");
            var p = t.position; p.x = x; t.position = p;
        }

        static GameObject Group(GameObject root, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(root.transform);
            return g;
        }

        static void CopyPerson(Transform people, string srcName, GameObject parent, Vector3 pos, float yaw, string name)
        {
            Transform src = null;
            foreach (Transform c in people) if (c.name == srcName) { src = c; break; }
            if (src == null) return;
            var p = Object.Instantiate(src.gameObject, parent.transform);
            p.name = name;
            p.transform.position = pos;
            p.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            var cap = p.AddComponent<CapsuleCollider>();   // 지나가지 못하게 (얼어붙은 몸)
            cap.center = new Vector3(0f, 0.8f, 0f); cap.radius = 0.35f; cap.height = 1.6f;
        }

        static void SetFloat(GameObject go, string component, string field, float v)
        {
            var c = go.GetComponent(component);
            if (c == null) return;
            var so = new SerializedObject(c);
            var p = so.FindProperty(field);
            if (p == null) return;
            p.floatValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureInstance(UnityEngine.SceneManagement.Scene scene, string prefabPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null) { Miss(prefabPath); return; }
            foreach (var go in scene.GetRootGameObjects())
                if (PrefabUtility.GetCorrespondingObjectFromSource(go) == prefab)
                {
                    // 프리팹이 정답 — 씬 쪽 덧붙임·수정은 지운다
                    PrefabUtility.RevertPrefabInstance(go, InteractionMode.AutomatedAction);
                    return;
                }
            var inst = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(inst, "레벨 v2");
        }
    }
}
