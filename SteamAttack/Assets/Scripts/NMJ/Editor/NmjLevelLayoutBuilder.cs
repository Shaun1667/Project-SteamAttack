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
        static readonly Vector3 FogGatePos = new Vector3(0f, 0f, -90f);    // v4: 육조거리 한가운데 (광화문을 나와 남쪽으로)
        static readonly Vector3 FogReturnPos = new Vector3(0f, 0.1f, -85f);  // 보스방에서 돌아오는 자리 (문을 등지고 북쪽을 본다)
        // v4 시작: 근정전 계단 앞 서쪽 (남쪽을 보고 선다). 향원정 옆(-35, 80)에서 당겨 이동 171m → 약 120m
        static readonly Vector3 StartPos = new Vector3(-12f, 0.1f, 30f);
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
                // v4: 모든 전투를 궁 안 → 광화문 안쪽에서. 북쪽에서 내려오는 플레이어를 보게 (yaw 0)
                // (묶음 이름 '육조거리'는 퀘스트 아이디·리젠이 쓰므로 그대로 — 실제 자리는 근정전 마당)
                { "육조거리/변이포졸_육조_1", new Vector3(-5f, 0.1f, 3f) },          // 근정전 마당 첫 전투
                { "육조거리/변이포졸_육조_2", new Vector3(5f, 0.1f, 4f) },
                { "육조거리/궁수자리_육조거리(누각)", new Vector3(14f, 0f, -10f) },   // 동쪽 단상 위 궁수 → 사격
                { "육조거리/흑수회궁수_육조", new Vector3(14f, 3.1f, -10f) },
                { "육조거리/변이포졸_육조_3_받아치기", new Vector3(0f, 0.1f, -17f) },  // 혼자 내려치기 → 첫 받아치기
                { "광화문/변이포졸_광화문_1", new Vector3(-6f, 0.1f, -32f) },        // 광화문 안쪽 전투
                { "광화문/변이포졸_광화문_2", new Vector3(6f, 0.1f, -32f) },
                { "광화문/궁수자리_광화문(해태 뒤 단상)", new Vector3(-15.5f, 0f, -36f) },
                { "광화문/흑수회궁수_광화문", new Vector3(-15.5f, 3.05f, -36f) },
                { "광화문/변이포졸_광화문_3", new Vector3(0f, 0.1f, -39f) },   // 마지막 한 명: 얼음 문 바로 앞
            };
            foreach (var kv in p)
            {
                var t = root.transform.Find(kv.Key);
                if (t == null) { Miss("적 " + kv.Key); continue; }
                t.localPosition = kv.Value;
                t.localRotation = Quaternion.Euler(0f, t.name.StartsWith("흑수회궁수_광화문") ? 30f : t.name.StartsWith("흑수회궁수") ? 300f : 0f, 0f);
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
            if (gwaeseo) { gwaeseo.localPosition = new Vector3(-7f, 0f, -60f); gwaeseo.localEulerAngles = new Vector3(0f, 120f, 0f); }   // 광화문을 나서자마자 보이는 도성의 괘서
            var memo = root.transform.Find("NMJ_비망록_육조거리");
            if (memo) { memo.localPosition = new Vector3(-6f, 0f, -3f); memo.localEulerAngles = new Vector3(0f, 197f, 0f); }   // 궁수 사격 직전
            var tut = root.transform.Find("NMJ_시작튜토리얼_증기충전");
            if (tut) { tut.localPosition = new Vector3(StartPos.x, 0f, StartPos.z - 6f); tut.localEulerAngles = new Vector3(0f, 180f, 0f); }   // 시작 자리 바로 앞 허수아비
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
                // v3: 꼬리가 z 88~222(근정전 뒤 멀리)라 궁 안 길에서 거의 안 보였다 → 60m 남쪽으로 내려
                // 동쪽 담(x 70)을 따라 근정전 옆까지 감기게. 궁 안을 달리는 동안 오른쪽 담 너머로 계속 보인다 (4.4)
                var tail = hm.Find("성벽을 감은 꼬리");
                if (tail) { Undo.RecordObject(tail, "레벨 v3"); tail.position = new Vector3(0f, 1.5f, -60f); }
            }

            // 안개문을 근정전 앞으로 당긴다 (예전 자리 z 80은 근정전 뒤를 돌아가야 해 49초 걸렸다)
            var gate2 = world.Find("07_향원정_얼어붙은_연못/NMJ_FogGate_향원정");
            if (gate2) { Undo.RecordObject(gate2, "레벨 v2"); gate2.position = FogGatePos; gate2.rotation = Quaternion.Euler(0f, 180f, 0f); }   // 북쪽에서 온다
            // 보스방에서 돌아오는 자리: 안개문 앞, 문을 등지고 (예전 z 74는 월대 모서리라 몸이 박혔다)
            var back = world.Find("07_향원정_얼어붙은_연못/NMJ_FogGate_돌아온자리");
            if (back) { Undo.RecordObject(back, "레벨 v2"); back.position = FogReturnPos; back.rotation = Quaternion.identity; }

            // v4: 시장길(운종가)·기기창은 동선 밖이라 잘라 낸다 — 향원정 → 궁 안 → 광화문 → 육조거리만 남김
            foreach (var cut in new[]
            {
                "05_운종가_저잣거리", "08_삼청동_기기창", "00_지면/운종가 길", "00_지면/삼청동 길",
                "13_인물/흑수회 광신도", "13_인물/기기창 기술자 (얼어붙음)", "13_인물/얼어붙은 백성 (운종가)",   // 백성은 육조거리 사본만 남김
                "시점 표식/시점_운종가", "시점 표식/시점_기기창 포대", "시점 표식/시점_전경 (도성 동남쪽)",
            })
                SetActive(world, cut, false);

            // v4: 플레이어 시작 자리 (향원정 남서쪽) — 씬의 플레이어·카메라를 옮긴다
            var scene = world.gameObject.scene;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == "NGH_Player") { Undo.RecordObject(go.transform, "레벨 v4"); go.transform.SetPositionAndRotation(StartPos, Quaternion.Euler(0f, 180f, 0f)); }
                if (go.name == "NGH_PlayerCamera") { Undo.RecordObject(go.transform, "레벨 v4"); go.transform.SetPositionAndRotation(StartPos + new Vector3(-0.25f, 1.3f, 2.4f), Quaternion.Euler(0f, 180f, 0f)); }
            }
            // 보스방(별도 씬)에서 돌아올 때 나타나는 자리도 안개문 앞으로
            var portal = GameObject.Find("NGH_Spawn_FromBossArena");
            if (portal) { Undo.RecordObject(portal.transform, "레벨 v4"); portal.transform.SetPositionAndRotation(FogReturnPos + new Vector3(2f, -0.1f, 0f), Quaternion.identity); }

            // 얼음 문은 광화문 앞 4명만 지킨다 (받아치기 포졸 z -70은 빼고 해태 뒤 궁수는 넣는 거리)
            var gate = world.Find("02_광화문/NMJ_광화문_얼음문");
            if (gate) SetFloat(gate.gameObject, "IceGate", "autoFindRadius", 19f);
        }

        // ---------------------------------------------------------------- 퀘스트 도착 지점
        static void ApplyQuestZones()
        {
            var root = PrefabUtility.LoadPrefabContents(QuestZonePrefab);
            var gw = root.transform.Find("도달_광화문앞");
            if (gw) gw.localPosition = new Vector3(0f, 2f, -26f);   // 받아치기 포졸 뒤, 광화문 안쪽 전투 직전
            var z = root.transform.Find("도달_향원정안개문");
            if (z) z.localPosition = new Vector3(FogGatePos.x, 2f, FogGatePos.z + 5f);   // 안개문 바로 앞 마당 (상자 12×4×5)
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
                // v4: 시작(근정전 앞) → 근정전 마당 → 광화문
                new Vector3(-12f, 0f, 20f), new Vector3(-6f, 0f, 10f), new Vector3(-2f, 0f, -6f), new Vector3(0f, 0f, -22f),
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

            BuildDressingV3(world, root, people, cart, spike, ice, glow);

            PrefabUtility.SaveAsPrefabAsset(root, DressingPrefab);
            Object.DestroyImmediate(root);
        }

        // ---------------------------------------------------------------- v3: 기획서 4장 장면을 크게 (2026-10-26)
        // 가운데 길(x -6 ~ 6)은 비워 두어 동선·전투는 v2 그대로. 위에서 봐도 구간마다 무슨 일이 있었는지 보이게 채운다.
        static void BuildDressingV3(Transform world, GameObject root, Transform people, Transform cart, Mesh spike, Material ice, Material glow)
        {
            // 1) 육조거리: 북쪽(재앙)에서 남쪽으로 달아나다 그대로 얼어붙은 백성 무리 (4.2 "도망치다 멈춘 아이와 어미")
            var crowd = Group(root, "v3_육조거리_달아나다_멈춘_백성");
            if (people)
            {
                var spots = new (float x, float z, string src)[]
                {
                    (-9f, -66f, "달아나던 백성"), (-10.5f, -67.5f, "아이"), (-8f, -69f, "달아나던 백성"),
                    (9.5f, -73f, "빌고 있는 백성"), (11f, -71f, "달아나던 백성"),
                    (-11f, -79f, "달아나던 백성"), (-9.5f, -80.5f, "달아나던 백성"), (-11.5f, -82f, "아이"),
                    (10f, -88f, "달아나던 백성"), (11.5f, -89.5f, "아이"), (9f, -91f, "달아나던 백성"),
                    (-9f, -95f, "빌고 있는 백성"), (-11f, -100f, "달아나던 백성"), (10.5f, -103f, "달아나던 백성"),
                };
                for (int i = 0; i < spots.Length; i++)
                {
                    var s = spots[i];
                    CopyPerson(people, s.src, crowd, new Vector3(s.x, 0f, s.z), 180f + (i % 5 - 2) * 14f, $"얼어붙은 백성_{i + 1:00}");
                    IceBase(crowd, spike, ice, new Vector3(s.x, 0f, s.z), i);   // 발이 얼음에 붙잡혔다
                }
            }

            // 2) 육조거리 가장자리: 버려진 수레·짐 (달아나다 놓고 간 것)
            if (cart)
            {
                var carts = Group(root, "v3_육조거리_버려진_수레");
                var cs = new (Vector3 p, float yaw, bool tipped)[]
                {
                    (new Vector3(-10f, 0f, -74f), 160f, false), (new Vector3(10f, 0.6f, -96f), 20f, true),
                    (new Vector3(-10.5f, 0.6f, -88f), 250f, true), (new Vector3(9.5f, 0f, -64f), 200f, false),
                };
                for (int i = 0; i < cs.Length; i++)
                {
                    var c = Object.Instantiate(cart.gameObject, carts.transform);
                    c.name = $"버려진 수레_{i + 1}";
                    c.transform.position = cs[i].p;
                    c.transform.rotation = Quaternion.Euler(0f, cs[i].yaw, cs[i].tipped ? 80f : 0f);
                    var bc = c.AddComponent<BoxCollider>();
                    bc.center = new Vector3(0f, 0.6f, 0f); bc.size = new Vector3(2.2f, 1.6f, 3.4f);
                }
            }

            // 3) 관아 앞 서리 — 길 양쪽 처마 밑에 얼음 결정 무리 (도성이 얼어붙었다)
            if (spike && ice)
            {
                var frost = Group(root, "v3_길가_얼음결정");
                int k = 0;
                for (float z = -108f; z <= -60f; z += 8f)
                    foreach (var side in new[] { -1f, 1f })
                        for (int j = 0; j < 3; j++, k++)
                            Spike(frost, spike, ice, new Vector3(side * (16f + j * 1.6f), 0f, z + (j - 1) * 2f), k, 2.2f + (k % 4) * 0.9f);

                // 4) 광화문: 얼음에 덮인 해태상 (4.3) — 해태 둘레를 큰 얼음 결정이 감싼다
                var haetae = Group(root, "v3_광화문_얼음에_덮인_해태");
                foreach (var hx in new[] { -12f, 12f })
                    for (int j = 0; j < 7; j++, k++)
                    {
                        float a = j / 7f * Mathf.PI * 2f;
                        Spike(haetae, spike, ice, new Vector3(hx + Mathf.Cos(a) * 2.8f, 0f, -58f + Mathf.Sin(a) * 2.8f), k, 3.5f + (j % 3) * 1.2f);
                    }

                // 5) 궁 안: 서리 낀 어도와 품계석 (6장) — 품계석 줄을 따라 작은 얼음, 근정전 계단 양옆에 큰 얼음
                var court = Group(root, "v3_궁안_서리");
                for (float z = 12f; z <= 32f; z += 4f)
                    foreach (var side in new[] { -1f, 1f })
                    {
                        Spike(court, spike, ice, new Vector3(side * 8.5f, 0f, z), k, 1.4f + (k % 3) * 0.6f);
                        k++;
                    }
                foreach (var p in new[] { new Vector3(-9f, 0f, 33f), new Vector3(9f, 0f, 33f), new Vector3(-12f, 0f, 34f), new Vector3(12f, 0f, 34f) })   // 새 근정전 하월대 앞 가장자리(z 36) 앞으로
                    Spike(court, spike, ice, p, k++, 4.5f);
            }

            // 5-2) 얼어붙은 웅덩이 — 길과 마당 바닥에 푸른 얼음판 (위에서 봐도 '얼어붙은 도성'이 보이게)
            var disc = AssetDatabase.LoadAssetAtPath<Mesh>("Assets/Arts/Models/NMJ/HyeonmuWorld/Disc.asset");
            var iceFlat = AssetDatabase.LoadAssetAtPath<Material>(MatDir + "M_Ice.mat");
            if (disc && iceFlat)
            {
                var pools = Group(root, "v3_얼어붙은_웅덩이");
                var ps = new (float x, float z, float r)[]
                {
                    (-5f, -100f, 5f), (6f, -92f, 6f), (-6f, -78f, 7f), (5f, -66f, 5f), (0f, -56f, 8f),   // 육조거리 ~ 광화문 앞
                    (-14f, -20f, 8f), (15f, -8f, 9f), (-16f, 12f, 7f), (16f, 26f, 8f),                    // 근정전 마당
                };
                for (int i = 0; i < ps.Length; i++)
                {
                    var d = new GameObject($"얼음판_{i + 1}");
                    d.transform.SetParent(pools.transform);
                    d.transform.position = new Vector3(ps[i].x, 0.03f, ps[i].z);
                    d.transform.rotation = Quaternion.Euler(0f, i * 47f, 0f);
                    d.transform.localScale = new Vector3(ps[i].r, 1f, ps[i].r * 0.7f);
                    d.AddComponent<MeshFilter>().sharedMesh = disc;
                    var mr = d.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = iceFlat;
                    mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }

            // 6) 푸른 시간 부스러기를 시작부터 광화문까지 (4.2 "그 사이로 푸른 시간 부스러기가 보인다") — 궁 안 길잡이와 이어진다
            var trail = Group(root, "v3_육조거리_시간부스러기");
            int n = 0;
            for (float z = -86f; z <= -50f; z += 3.5f, n++)   // v4: 광화문을 나와 안개문까지
            {
                var shard = GameObject.CreatePrimitive(PrimitiveType.Cube);
                Object.DestroyImmediate(shard.GetComponent<Collider>());
                shard.name = $"부스러기_{n + 1:00}";
                shard.transform.SetParent(trail.transform);
                shard.transform.position = new Vector3(Mathf.Sin(n * 1.7f) * 4.5f, 1.6f + (n % 4) * 0.45f, z);   // 공중에 멈춰 떠 있다
                shard.transform.rotation = Quaternion.Euler(45f, n * 41f, 45f);
                shard.transform.localScale = Vector3.one * (0.14f + (n % 3) * 0.05f);
                var r = shard.GetComponent<Renderer>();
                if (glow) r.sharedMaterial = glow;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            foreach (var z in new[] { -84f, -68f, -54f })
            {
                var l = new GameObject("부스러기 빛").AddComponent<Light>();
                l.transform.SetParent(trail.transform);
                l.transform.position = new Vector3(0f, 2.5f, z);
                l.type = LightType.Point; l.color = new Color(0.45f, 0.75f, 1f); l.range = 10f; l.intensity = 2.5f;
                l.shadows = LightShadows.None;
                l.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
            }
        }

        static void Spike(GameObject parent, Mesh mesh, Material mat, Vector3 pos, int i, float height)
        {
            var s = new GameObject($"얼음_{i:000}");
            s.transform.SetParent(parent.transform);
            s.transform.position = pos;
            s.transform.rotation = Quaternion.Euler((i % 3 - 1) * 14f, i * 61f, (i % 2 == 0 ? 12f : -10f));
            s.transform.localScale = new Vector3(0.45f + height * 0.18f, height, 0.45f + height * 0.18f);
            s.AddComponent<MeshFilter>().sharedMesh = mesh;
            s.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        static void IceBase(GameObject parent, Mesh mesh, Material mat, Vector3 pos, int i)
        {
            if (!mesh || !mat) return;
            for (int j = 0; j < 3; j++)
            {
                float a = (i * 50f + j * 120f) * Mathf.Deg2Rad;
                Spike(parent, mesh, mat, pos + new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f), i * 3 + j, 0.5f + j * 0.15f);
            }
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
