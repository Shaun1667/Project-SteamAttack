#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace NGH
{
    /// <summary>
    /// Tools/NGH/Hero/5. Build Skirt Physics Bones
    /// NGH_Player의 치마(OutterChima)에서 Unity Cloth를 끄고, 치마 둘레에 세로 뼈 사슬을 심어 SkirtPhysics로 움직이게 바꿉니다.
    ///  - 치마 모양은 NGH_OutterChima_Narrow(좁힌 치마)를 그대로 사용
    ///  - 뼈는 엉덩이(Hips) 아래 "SkirtPhys_Cxx_Jy" 이름으로 생성, 메시는 NGH_OutterChima_Physics.asset 으로 새로 저장
    ///  - 다시 실행하면 기존 치마 뼈를 지우고 새로 만듦 (사슬 수·마디 수를 바꿀 때)
    /// </summary>
    public static class SkirtPhysicsBuilder
    {
        const string PlayerPrefabPath = "Assets/Prefabs/NGH/NGH_Player.prefab";
        const string SourceMeshPath = "Assets/Prefabs/NGH/Meshes/NGH_OutterChima_Narrow.asset";
        const string OutputMeshPath = "Assets/Prefabs/NGH/Meshes/NGH_OutterChima_Physics.asset";
        const string SkirtName = "OutterChima";
        const string BonePrefix = "SkirtPhys_";
        const int Chains = 24;
        const int Joints = 6;            // 사슬당 마디 수 (허리 1 + 아래로 5)
        const float WaistDrop = 0.03f;   // 치마 맨 위에서 이만큼 아래가 허리 마디 (그 위는 엉덩이에 고정)
        const float FoldSmoothing = 1f;  // 메시에 잡힌 주름을 펴는 정도 (0 = 원래 모양, 1 = 주름 모두 폄)

        [MenuItem("Tools/NGH/Hero/5. Build Skirt Physics Bones")]
        public static void Build()
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            try
            {
                string log = BuildIn(root);
                PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
                Debug.Log("[NGH] 치마 물리 생성: " + log);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }

        static string BuildIn(GameObject root)
        {
            var smr = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).First(s => s.name == SkirtName);
            var hero = smr.transform.parent;
            var animator = hero.GetComponent<Animator>();
            var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            var source = AssetDatabase.LoadAssetAtPath<Mesh>(SourceMeshPath);
            if (!source.isReadable)
            {
                var so = new SerializedObject(source);
                so.FindProperty("m_IsReadable").boolValue = true;
                so.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
            }

            // 기존 치마 뼈 제거 (원래 뼈 순서는 원본 메시의 bindpose 개수만큼)
            var baseBones = smr.bones.Take(source.bindposes.Length).ToArray();
            foreach (var t in hips.GetComponentsInChildren<Transform>(true).Where(t => t && t.name.StartsWith(BonePrefix) && t.parent == hips).ToArray())
                Object.DestroyImmediate(t.gameObject);

            // 1) 현재 자세에서 치마 정점의 월드 위치·노멀 (원본 메시 + 원래 뼈로 직접 스키닝)
            var srcV = source.vertices; var srcN = source.normals; var srcT = source.tangents;
            var srcW = source.boneWeights; var srcBind = source.bindposes;
            var skin = new Matrix4x4[baseBones.Length];
            for (int b = 0; b < baseBones.Length; b++) skin[b] = baseBones[b].localToWorldMatrix * srcBind[b];
            int nv = srcV.Length;
            var wPos = new Vector3[nv]; var wNrm = new Vector3[nv]; var wTan = new Vector4[nv];
            for (int i = 0; i < nv; i++)
            {
                var w = srcW[i];
                Matrix4x4 m = Blend(skin, w);
                wPos[i] = m.MultiplyPoint3x4(srcV[i]);
                if (srcN.Length == nv) wNrm[i] = m.MultiplyVector(srcN[i]).normalized;
                if (srcT.Length == nv) { var tv = m.MultiplyVector(srcT[i]).normalized; wTan[i] = new Vector4(tv.x, tv.y, tv.z, srcT[i].w); }
            }

            // 1.5) 주름 펴기: 바깥면의 둘레 방향 굴곡을 이웃 평균으로 펴서 치마가 아래로 매끈하게 떨어지게 함 (안감과의 두께는 유지)
            if (FoldSmoothing > 0f) SmoothFolds(wPos, hero, hips.position);

            // 2) 치마 축 기준 원통 좌표 (hero 기준: 위 = hero.up, 중심 = 엉덩이)
            Vector3 Local(Vector3 p) => hero.InverseTransformPoint(p);
            Vector3 hipL = Local(hips.position);
            var loc = wPos.Select(Local).ToArray();
            float top = loc.Max(p => p.y), bot = loc.Min(p => p.y);
            float yWaist = top - WaistDrop;
            float Angle(Vector3 p) { float a = Mathf.Atan2(p.z - hipL.z, p.x - hipL.x); return a < 0 ? a + 2 * Mathf.PI : a; }
            float Radius(Vector3 p) => new Vector2(p.x - hipL.x, p.z - hipL.z).magnitude;
            float JointY(int j) => Mathf.Lerp(yWaist, bot, j / (float)(Joints - 1));

            // 3) 마디 위치: 각 각도·높이에서 치마 바깥면 반지름
            float sector = 2 * Mathf.PI / Chains;
            var radius = new float[Chains, Joints];
            for (int c = 0; c < Chains; c++)
                for (int j = 0; j < Joints; j++)
                {
                    float ang = c * sector, y = JointY(j);
                    float best = -1;
                    for (float band = 0.03f; best < 0 && band <= 0.12f; band += 0.03f)
                        for (int i = 0; i < nv; i++)
                        {
                            var p = loc[i];
                            if (Mathf.Abs(p.y - y) > band) continue;
                            if (Mathf.Abs(Mathf.DeltaAngle(Angle(p) * Mathf.Rad2Deg, ang * Mathf.Rad2Deg)) > sector * Mathf.Rad2Deg) continue;
                            best = Mathf.Max(best, Radius(p));
                        }
                    radius[c, j] = best;
                }
            for (int c = 0; c < Chains; c++)   // 비어 있는 곳은 양옆 평균
                for (int j = 0; j < Joints; j++)
                    if (radius[c, j] < 0)
                        radius[c, j] = Mathf.Max(0.05f, (Mathf.Max(0, radius[(c + Chains - 1) % Chains, j]) + Mathf.Max(0, radius[(c + 1) % Chains, j])) * 0.5f);

            // 4) 뼈 생성 (허리 마디는 Hips 아래, 나머지는 위 마디 아래)
            var joints = new Transform[Chains * Joints];
            for (int c = 0; c < Chains; c++)
            {
                Transform parent = hips;
                for (int j = 0; j < Joints; j++)
                {
                    float ang = c * sector;
                    var lp = new Vector3(hipL.x + Mathf.Cos(ang) * radius[c, j], JointY(j), hipL.z + Mathf.Sin(ang) * radius[c, j]);
                    var go = new GameObject(string.Format("{0}C{1:00}_J{2}", BonePrefix, c, j));
                    go.transform.SetParent(parent, false);
                    go.transform.SetPositionAndRotation(hero.TransformPoint(lp), hero.rotation);
                    joints[c * Joints + j] = go.transform;
                    parent = go.transform;
                }
            }

            // 5) 가중치: 각도로 양옆 사슬, 높이로 위아래 뼈(마디 사이 중간점 기준)를 섞음. 허리 위는 Hips
            int hipsIndex = System.Array.IndexOf(baseBones, hips);
            int baseCount = baseBones.Length;
            var weights = new BoneWeight[nv];
            for (int i = 0; i < nv; i++)
            {
                var p = loc[i];
                if (p.y >= yWaist) { weights[i] = new BoneWeight { boneIndex0 = hipsIndex, weight0 = 1f }; continue; }
                float fc = Angle(p) / sector;
                int c0 = Mathf.FloorToInt(fc) % Chains, c1 = (c0 + 1) % Chains;
                float tc = fc - Mathf.Floor(fc);
                // 뼈 j는 마디 j→j+1 구간. 구간 중간점 사이를 선형 보간
                float seg = (yWaist - bot) / (Joints - 1);
                float fj = (yWaist - p.y) / seg - 0.5f;
                int j0 = Mathf.Clamp(Mathf.FloorToInt(fj), 0, Joints - 2), j1 = Mathf.Min(j0 + 1, Joints - 2);
                float tj = Mathf.Clamp01(fj - Mathf.Floor(fj));
                if (fj < 0) { j0 = j1 = 0; tj = 0; }
                var list = new List<(int, float)>
                {
                    (baseCount + c0 * Joints + j0, (1 - tc) * (1 - tj)),
                    (baseCount + c0 * Joints + j1, (1 - tc) * tj),
                    (baseCount + c1 * Joints + j0, tc * (1 - tj)),
                    (baseCount + c1 * Joints + j1, tc * tj),
                };
                var merged = list.GroupBy(x => x.Item1).Select(g => (g.Key, g.Sum(x => x.Item2))).OrderByDescending(x => x.Item2).ToList();
                while (merged.Count < 4) merged.Add((0, 0f));
                float sum = merged.Sum(x => x.Item2);
                weights[i] = new BoneWeight
                {
                    boneIndex0 = merged[0].Item1, weight0 = merged[0].Item2 / sum,
                    boneIndex1 = merged[1].Item1, weight1 = merged[1].Item2 / sum,
                    boneIndex2 = merged[2].Item1, weight2 = merged[2].Item2 / sum,
                    boneIndex3 = merged[3].Item1, weight3 = merged[3].Item2 / sum,
                };
            }

            // 6) 메시: 현재 자세를 새 기준 자세로 (정점은 SMR 로컬, bindpose = 뼈.W2L * SMR.L2W)
            var allBones = baseBones.Concat(joints).ToArray();
            var l2w = smr.transform.localToWorldMatrix; var w2l = smr.transform.worldToLocalMatrix;
            var mesh = Object.Instantiate(source);
            mesh.name = "NGH_OutterChima_Physics";
            mesh.vertices = wPos.Select(p => w2l.MultiplyPoint3x4(p)).ToArray();
            if (srcN.Length == nv) mesh.normals = wNrm.Select(n => w2l.MultiplyVector(n).normalized).ToArray();   // 스키닝이 노멀에 같은 행렬을 곱하므로 역행렬로 되돌림
            if (srcT.Length == nv) mesh.tangents = wTan.Select(t => { var v = w2l.MultiplyVector(t).normalized; return new Vector4(v.x, v.y, v.z, t.w); }).ToArray();
            if (FoldSmoothing > 0f) RecalculateWeldedNormals(mesh);   // 모양이 바뀌었으니 노멀도 새 표면 기준으로
            mesh.boneWeights = weights;
            mesh.bindposes = allBones.Select(b => b.worldToLocalMatrix * l2w).ToArray();
            mesh.RecalculateBounds();
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(OutputMeshPath);
            if (existing) { EditorUtility.CopySerialized(mesh, existing); Object.DestroyImmediate(mesh); mesh = existing; EditorUtility.SetDirty(mesh); }
            else AssetDatabase.CreateAsset(mesh, OutputMeshPath);
            AssetDatabase.SaveAssets();

            smr.bones = allBones;
            smr.sharedMesh = mesh;

            // 7) Cloth 끄고 SkirtPhysics 연결 (다리·엉덩이 캡슐은 Cloth에 쓰던 것 그대로)
            var cloth = smr.GetComponent<Cloth>();
            CapsuleCollider[] capsules = new CapsuleCollider[0];
            if (cloth)
            {
                capsules = cloth.capsuleColliders.Where(c => c && (c.name.Contains("Thigh") || c.name.Contains("Shin"))).ToArray();
                cloth.enabled = false;
            }
            var phys = smr.GetComponent<SkirtPhysics>() ?? smr.gameObject.AddComponent<SkirtPhysics>();
            phys.joints = joints;
            phys.chainCount = Chains;
            phys.jointsPerChain = Joints;
            if (capsules.Length > 0) phys.colliders = capsules;

            return string.Format("사슬 {0} × 마디 {1}, 정점 {2}, 캡슐 {3}, 허리 높이 {4:F2}, 아랫단 {5:F2}", Chains, Joints, nv, phys.colliders.Length, yWaist, bot);
        }

        // 엉덩이 축 기준 원통 좌표로 바깥면 반지름 지도(둘레 2.5° × 높이 2cm)를 만들고,
        // 지도를 둘레·높이 방향으로 흐리게 한 값과의 차이(= 주름)만큼 같은 칸의 정점을 모두 안팎으로 옮김
        static void SmoothFolds(Vector3[] wPos, Transform hero, Vector3 hipsPos)
        {
            const int A = 144; const float H = 0.02f; const int BlurA = 8, BlurH = 2;
            Vector3 c = hero.InverseTransformPoint(hipsPos);
            int n = wPos.Length;
            var ang = new float[n]; var hgt = new float[n]; var rad = new float[n];
            float yMin = float.MaxValue, yMax = float.MinValue;
            for (int i = 0; i < n; i++)
            {
                Vector3 p = hero.InverseTransformPoint(wPos[i]) - c;
                float a = Mathf.Atan2(p.z, p.x); if (a < 0f) a += 2f * Mathf.PI;
                ang[i] = a / (2f * Mathf.PI) * A; hgt[i] = p.y; rad[i] = new Vector2(p.x, p.z).magnitude;
                yMin = Mathf.Min(yMin, p.y); yMax = Mathf.Max(yMax, p.y);
            }
            int nh = Mathf.CeilToInt((yMax - yMin) / H) + 1;
            var outer = new float[A, nh];
            for (int a = 0; a < A; a++) for (int h = 0; h < nh; h++) outer[a, h] = -1f;
            for (int i = 0; i < n; i++)
            {
                int a = Mathf.FloorToInt(ang[i]) % A, h = Mathf.FloorToInt((hgt[i] - yMin) / H);
                outer[a, h] = Mathf.Max(outer[a, h], rad[i]);
            }
            // 빈 칸은 같은 높이의 양옆 값으로 채움
            for (int h = 0; h < nh; h++)
            {
                if (!Enumerable.Range(0, A).Any(a => outer[a, h] >= 0f)) continue;
                var row = new float[A];
                for (int a = 0; a < A; a++)
                {
                    if (outer[a, h] >= 0f) { row[a] = outer[a, h]; continue; }
                    int l = 1, r = 1;
                    while (outer[(a - l + A) % A, h] < 0f) l++;
                    while (outer[(a + r) % A, h] < 0f) r++;
                    row[a] = Mathf.Lerp(outer[(a - l + A) % A, h], outer[(a + r) % A, h], l / (float)(l + r));
                }
                for (int a = 0; a < A; a++) outer[a, h] = row[a];
            }
            var ripple = new float[A, nh];
            for (int a = 0; a < A; a++)
                for (int h = 0; h < nh; h++)
                {
                    if (outer[a, h] < 0f) continue;
                    float sum = 0f, wsum = 0f;
                    for (int da = -BlurA; da <= BlurA; da++)
                        for (int dh = -BlurH; dh <= BlurH; dh++)
                        {
                            int hh = h + dh; if (hh < 0 || hh >= nh) continue;
                            float v = outer[(a + da + A) % A, hh]; if (v < 0f) continue;
                            float w = Mathf.Exp(-(da * da) / (2f * BlurA * BlurA / 4f) - (dh * dh) / (2f * BlurH * BlurH / 4f + 1e-3f));
                            sum += v * w; wsum += w;
                        }
                    ripple[a, h] = outer[a, h] - sum / wsum;
                }
            float Ripple(float a, float y)
            {
                float fh = Mathf.Clamp((y - yMin) / H - 0.5f, 0f, nh - 1);
                int h0 = Mathf.FloorToInt(fh), h1 = Mathf.Min(h0 + 1, nh - 1);
                float fa = a - 0.5f; if (fa < 0f) fa += A;
                int a0 = Mathf.FloorToInt(fa) % A, a1 = (a0 + 1) % A;
                float ta = fa - Mathf.Floor(fa), th = fh - h0;
                return Mathf.Lerp(Mathf.Lerp(ripple[a0, h0], ripple[a1, h0], ta), Mathf.Lerp(ripple[a0, h1], ripple[a1, h1], ta), th);
            }
            for (int i = 0; i < n; i++)
            {
                float keep = Mathf.Clamp01((yMax - WaistDrop - hgt[i]) / 0.03f);   // 허리선 근처는 그대로 (엉덩이에 붙는 부분)
                float r = Mathf.Max(0.02f, rad[i] - FoldSmoothing * keep * Ripple(ang[i], hgt[i]));
                float a = ang[i] / A * 2f * Mathf.PI;
                wPos[i] = hero.TransformPoint(c + new Vector3(Mathf.Cos(a) * r, hgt[i], Mathf.Sin(a) * r));
            }
        }

        // 노멀 재계산 후 같은 위치(UV 이음매)의 정점끼리 평균내서 이음매가 보이지 않게 함
        static void RecalculateWeldedNormals(Mesh mesh)
        {
            mesh.RecalculateNormals();
            var v = mesh.vertices; var nrm = mesh.normals;
            foreach (var g in Enumerable.Range(0, v.Length).GroupBy(i => v[i]))
            {
                if (g.Count() < 2) continue;
                Vector3 s = Vector3.zero; foreach (var i in g) s += nrm[i];
                if (s.sqrMagnitude < 1e-8f) continue;   // 앞뒤 면이 붙은 곳은 그대로
                s.Normalize(); foreach (var i in g) nrm[i] = s;
            }
            mesh.normals = nrm;
            mesh.RecalculateTangents();
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
