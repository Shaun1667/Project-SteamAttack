#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Hero/7. Build Sleeve Physics Bones
    /// NGH_Player 저고리(OutterJeogori) 소매 아래로 늘어진 배래에 뼈 사슬을 심어 SleevePhysics로 흔들리게 합니다.
    ///  - 팔마다 어깨~손목 사이에 사슬 Chains개, 사슬마다 마디 Joints개 (뿌리는 위팔·아래팔에 붙음)
    ///  - 팔 축에서 아래로 처진 정점만 사슬로 옮겨 붙이고(깊을수록 사슬 비중↑), 나머지 저고리는 원래 가중치 그대로
    ///  - 뼈는 "SleevePhys_L/R_Cx_Jy", 메시는 NGH_OutterJeogori_Physics.asset 으로 새로 저장
    ///  - 허리~가슴 몸통 캡슐(데이터)을 넣어 팔을 내렸을 때 소매가 몸을 뚫지 않게 함
    ///  - 다시 실행하면 기존 소매 뼈를 지우고 원본 메시에서 새로 만듦
    /// </summary>
    public static class SleevePhysicsBuilder
    {
        const string PlayerPrefabPath = "Assets/Prefabs/NGH/NGH_Player.prefab";
        const string SourceMeshPath = "Assets/Prefabs/YPH/HeroPlayer/CollisionMeshes/OutterJeogori_Clearance.asset";
        const string OutputMeshPath = "Assets/Prefabs/NGH/Meshes/NGH_OutterJeogori_Physics.asset";
        const string JacketName = "OutterJeogori";
        const string BonePrefix = "SleevePhys_";
        const int Chains = 4;            // 팔 하나당 사슬 수
        const int Joints = 3;            // 사슬당 마디 수 (뿌리 1 + 아래로 2)
        const float FirstT = 0.12f;      // 어깨에서 첫 사슬까지 (m, 팔 방향)
        const float ShoulderRamp = 0.10f; // 첫 사슬부터 이 거리만큼 팔 쪽으로 가면서 흔들림이 서서히 커짐 (어깨 근처는 팔을 그대로 따라감)
        const float RootDepth = 0.04f;   // 팔 축에서 사슬 뿌리까지 (팔 표면, m)
        const float BlendDepth = 0.6f;   // 배래 깊이의 이 비율까지 내려가면 완전히 사슬을 따라감 (넓을수록 팔과의 경계가 부드럽게 꺾임)
        const float TorsoRadius = 0.12f; // 소매가 피할 몸통 반지름 (m)
        static readonly string[] BodyColliders = { "YPH_ClothCollider_Torso", "YPH_ClothCollider_LeftHip", "YPH_ClothCollider_RightHip", "YPH_ClothCollider_LeftThigh", "YPH_ClothCollider_RightThigh" };

        [MenuItem("Tools/NGH/Hero/7. Build Sleeve Physics Bones")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                string log = BuildIn(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[NGH] 소매 물리 생성: " + log);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        struct Sleeve
        {
            public Transform upper, fore, hand;
            public Vector3 shoulder, axis, down;
            public float elbowT, handT;
            public float[] chainT, bottom;     // 사슬 위치(팔 방향 거리)와 그 위치의 배래 깊이
            public int firstChain;             // 전체 사슬 번호에서 이 소매의 첫 사슬
        }

        static string BuildIn(GameObject root)
        {
            var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == JacketName);
            var hero = smr.transform.parent;
            var animator = hero.GetComponent<Animator>();
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMeshPath);
            if (!source.isReadable)
            {
                var so = new SerializedObject(source);
                so.FindProperty("m_IsReadable").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            // 기존 소매 뼈 제거 (원래 뼈 순서는 원본 메시의 bindpose 개수만큼)
            var baseBones = smr.bones.Take(source.bindposes.Length).ToArray();
            foreach (var t in hero.GetComponentsInChildren<Transform>(true).Where(t => t && t.name.StartsWith(BonePrefix) && !t.parent.name.StartsWith(BonePrefix)).ToArray())
                Object.DestroyImmediate(t.gameObject);

            // 1) 현재 자세에서 정점의 월드 위치·노멀 (원본 메시 + 원래 뼈로 직접 스키닝)
            var srcV = source.vertices; var srcN = source.normals; var srcT = source.tangents;
            var srcW = source.boneWeights; var srcBind = source.bindposes;
            var skin = new Matrix4x4[baseBones.Length];
            for (int b = 0; b < baseBones.Length; b++) skin[b] = baseBones[b].localToWorldMatrix * srcBind[b];
            int nv = srcV.Length;
            var wPos = new Vector3[nv]; var wNrm = new Vector3[nv]; var wTan = new Vector4[nv];
            for (int i = 0; i < nv; i++)
            {
                Matrix4x4 m = Blend(skin, srcW[i]);
                wPos[i] = m.MultiplyPoint3x4(srcV[i]);
                if (srcN.Length == nv) wNrm[i] = m.MultiplyVector(srcN[i]).normalized;
                if (srcT.Length == nv) { var tv = m.MultiplyVector(srcT[i]).normalized; wTan[i] = new Vector4(tv.x, tv.y, tv.z, srcT[i].w); }
            }

            // 2) 소매마다 팔 축·아래 방향·사슬 위치와 배래 깊이
            var sleeves = new List<Sleeve>();
            var joints = new List<Transform>();
            var linkNext = new List<bool>();
            foreach (bool left in new[] { true, false })
            {
                var s = new Sleeve
                {
                    upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm),
                    fore = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm),
                    hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand),
                };
                s.shoulder = s.upper.position;
                s.handT = Vector3.Distance(s.hand.position, s.shoulder);
                s.axis = (s.hand.position - s.shoulder) / s.handT;
                s.down = Vector3.ProjectOnPlane(-hero.up, s.axis).normalized;
                s.elbowT = Vector3.Dot(s.fore.position - s.shoulder, s.axis);
                s.chainT = new float[Chains]; s.bottom = new float[Chains];
                s.firstChain = joints.Count / Joints;
                for (int c = 0; c < Chains; c++)
                {
                    float t = Mathf.Lerp(FirstT, s.handT - 0.03f, c / (float)(Chains - 1));
                    float deepest = RootDepth + 0.04f;
                    for (int i = 0; i < nv; i++)
                    {
                        if (!IsArmVertex(srcW[i], baseBones, s)) continue;
                        Vector3 p = wPos[i] - s.shoulder;
                        if (Mathf.Abs(Vector3.Dot(p, s.axis) - t) > 0.04f) continue;
                        deepest = Mathf.Max(deepest, Vector3.Dot(p, s.down));
                    }
                    s.chainT[c] = t; s.bottom[c] = deepest;

                    // 뼈: 뿌리는 팔(팔꿈치 앞은 위팔, 뒤는 아래팔)에, 나머지는 위 마디에
                    Transform parent = t < s.elbowT ? s.upper : s.fore;
                    for (int j = 0; j < Joints; j++)
                    {
                        float depth = Mathf.Lerp(RootDepth, deepest, j / (float)(Joints - 1));
                        var go = new GameObject(string.Format("{0}{1}_C{2}_J{3}", BonePrefix, left ? "L" : "R", c, j));
                        go.transform.SetParent(parent, false);
                        go.transform.SetPositionAndRotation(s.shoulder + s.axis * t + s.down * depth, parent.rotation);
                        joints.Add(go.transform);
                        parent = go.transform;
                    }
                    linkNext.Add(c < Chains - 1);
                }
                sleeves.Add(s);
            }

            // 3) 가중치: 팔 축 아래로 처진 정점만 사슬로. 사슬 방향(팔 방향)으로 양옆 사슬, 깊이로 위아래 마디를 섞음
            int baseCount = baseBones.Length;
            var weights = (BoneWeight[])srcW.Clone();
            int moved = 0;
            for (int i = 0; i < nv; i++)
            {
                foreach (var s in sleeves)
                {
                    if (!IsArmVertex(srcW[i], baseBones, s)) continue;
                    Vector3 p = wPos[i] - s.shoulder;
                    float t = Vector3.Dot(p, s.axis), d = Vector3.Dot(p, s.down);
                    if (d <= RootDepth || t < FirstT - 0.04f) continue;
                    float fc = Mathf.Clamp((t - s.chainT[0]) / (s.chainT[1] - s.chainT[0]), 0f, Chains - 1);
                    int c0 = Mathf.Min(Mathf.FloorToInt(fc), Chains - 2), c1 = c0 + 1;
                    float tc = fc - c0;
                    float bottom = Mathf.Lerp(s.bottom[c0], s.bottom[c1], tc);
                    float f = Mathf.Clamp01((d - RootDepth) / Mathf.Max(0.01f, bottom - RootDepth));
                    float follow = Mathf.SmoothStep(0f, 1f, f / BlendDepth)
                                 * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(s.chainT[0] - 0.04f, s.chainT[0] + ShoulderRamp, t));
                    if (follow <= 0.001f) continue;
                    // 뼈 j는 마디 j→j+1 구간. 구간 중간점 사이를 선형 보간
                    float fj = f * (Joints - 1) - 0.5f;
                    int j0 = Mathf.Clamp(Mathf.FloorToInt(fj), 0, Joints - 2), j1 = Mathf.Min(j0 + 1, Joints - 2);
                    float tj = fj < 0f ? 0f : Mathf.Clamp01(fj - Mathf.Floor(fj));
                    int Bone(int c, int j) => baseCount + (s.firstChain + c) * Joints + j;
                    var list = new List<(int, float)>
                    {
                        (Bone(c0, j0), follow * (1 - tc) * (1 - tj)), (Bone(c0, j1), follow * (1 - tc) * tj),
                        (Bone(c1, j0), follow * tc * (1 - tj)),       (Bone(c1, j1), follow * tc * tj),
                    };
                    var w = srcW[i];
                    list.Add((w.boneIndex0, w.weight0 * (1 - follow))); list.Add((w.boneIndex1, w.weight1 * (1 - follow)));
                    list.Add((w.boneIndex2, w.weight2 * (1 - follow))); list.Add((w.boneIndex3, w.weight3 * (1 - follow)));
                    var merged = list.Where(x => x.Item2 > 0f).GroupBy(x => x.Item1).Select(g => (g.Key, g.Sum(x => x.Item2))).OrderByDescending(x => x.Item2).Take(4).ToList();
                    while (merged.Count < 4) merged.Add((0, 0f));
                    float sum = merged.Sum(x => x.Item2);
                    weights[i] = new BoneWeight
                    {
                        boneIndex0 = merged[0].Item1, weight0 = merged[0].Item2 / sum,
                        boneIndex1 = merged[1].Item1, weight1 = merged[1].Item2 / sum,
                        boneIndex2 = merged[2].Item1, weight2 = merged[2].Item2 / sum,
                        boneIndex3 = merged[3].Item1, weight3 = merged[3].Item2 / sum,
                    };
                    moved++;
                    break;
                }
            }

            // 4) 메시: 현재 자세를 새 기준 자세로 (정점은 SMR 로컬, bindpose = 뼈.W2L * SMR.L2W)
            var allBones = baseBones.Concat(joints).ToArray();
            var l2w = smr.transform.localToWorldMatrix; var w2l = smr.transform.worldToLocalMatrix;
            var mesh = Object.Instantiate(source);
            mesh.name = "NGH_OutterJeogori_Physics";
            mesh.vertices = wPos.Select(p => w2l.MultiplyPoint3x4(p)).ToArray();
            if (srcN.Length == nv) mesh.normals = wNrm.Select(n => w2l.MultiplyVector(n).normalized).ToArray();
            if (srcT.Length == nv) mesh.tangents = wTan.Select(t => { var v = w2l.MultiplyVector(t).normalized; return new Vector4(v.x, v.y, v.z, t.w); }).ToArray();
            mesh.boneWeights = weights;
            mesh.bindposes = allBones.Select(b => b.worldToLocalMatrix * l2w).ToArray();
            mesh.RecalculateBounds();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMeshPath);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
            else AssetDatabase.CreateAsset(mesh, OutputMeshPath);
            AssetDatabase.SaveAssets();

            smr.bones = allBones;
            smr.sharedMesh = mesh;

            // 5) 꺼 둔 Cloth는 정점 수가 달라지므로 제거하고 SleevePhysics 연결
            var cloth = smr.GetComponent<Cloth>();
            if (cloth) Object.DestroyImmediate(cloth);
            var phys = smr.GetComponent<SleevePhysics>();
            if (!phys) phys = smr.gameObject.AddComponent<SleevePhysics>();
            phys.joints = joints.ToArray();
            phys.chainCount = joints.Count / Joints;
            phys.jointsPerChain = Joints;
            phys.linkNext = linkNext.ToArray();
            var capsules = hero.GetComponentsInChildren<CapsuleCollider>(true);
            phys.colliders = BodyColliders.Select(n => capsules.FirstOrDefault(c => c.name == n)).Where(c => c).ToArray();
            // 허리~가슴 몸통 (조끼 포함 반폭 약 0.10~0.15m). 콜라이더 없이 데이터로만 둬서 게임 물리·판정에 영향 없음
            var chest = animator.GetBoneTransform(HumanBodyBones.UpperChest) ?? animator.GetBoneTransform(HumanBodyBones.Chest);
            phys.bodySegments = new[]
            {
                new SleevePhysics.BodySegment { from = animator.GetBoneTransform(HumanBodyBones.Hips), to = chest, radius = TorsoRadius, forwardOffset = 0.02f },
            };

            return string.Format("사슬 {0} × 마디 {1}, 소매 정점 {2}/{3}, 몸 캡슐 {4}, 배래 깊이 L[{5}] R[{6}]",
                phys.chainCount, Joints, moved, nv, phys.colliders.Length,
                string.Join(",", sleeves[0].bottom.Select(b => b.ToString("F2"))), string.Join(",", sleeves[1].bottom.Select(b => b.ToString("F2"))));
        }

        // 위팔·아래팔·손이 가장 큰 가중치인 정점 (= 소매)
        static bool IsArmVertex(BoneWeight w, Transform[] bones, Sleeve s)
        {
            int top = w.boneIndex0; float best = w.weight0;
            if (w.weight1 > best) { top = w.boneIndex1; best = w.weight1; }
            if (w.weight2 > best) { top = w.boneIndex2; best = w.weight2; }
            if (w.weight3 > best) { top = w.boneIndex3; }
            var t = bones[top];
            return t == s.upper || t == s.fore || t == s.hand;
        }

        static Matrix4x4 Blend(Matrix4x4[] skin, BoneWeight w)
        {
            Matrix4x4 m = new Matrix4x4();
            void Add(int idx, float wt) { if (wt <= 0) return; var s = skin[idx]; for (int k = 0; k < 16; k++) m[k] += s[k] * wt; }
            Add(w.boneIndex0, w.weight0); Add(w.boneIndex1, w.weight1); Add(w.boneIndex2, w.weight2); Add(w.boneIndex3, w.weight3);
            return m;
        }
    }
}
#endif
