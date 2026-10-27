namespace YPH.FinalPlayer3Cloth
{
using MagicaCloth2;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class AttachedSleeveSkinContact : MonoBehaviour
{
    public MagicaCloth cloth;
    public SkinnedMeshRenderer body;
    public Mesh restMesh;
    public int[] bodyTriangleIndices;
    public SkinnedMeshRenderer vest;
    public int[] vestTriangleIndices;
    [Min(0)] public float clearance = .006f;
    NativeArray<float3> bodyRest, clothRest;
    NativeArray<BoneWeight> bodyWeights, clothWeights;
    NativeArray<float4x4> bodyMatrices, clothMatrices;
    NativeArray<int3> triangles;
    Matrix4x4[] bodyBind, clothBind;
    Transform[] bodyBones, clothBones;
    NativeArray<float3> clothWorld, vestRest, vestPositions, vestCorrections, vestSmoothed;
    NativeArray<int2> vestNeighborRanges;
    NativeArray<int> vestNeighbors;
    NativeArray<BoneWeight> vestWeights;
    NativeArray<float4x4> vestMatrices;
    NativeArray<int3> vestTriangles;
    Matrix4x4[] vestBind;
    Transform[] vestBones;
    Mesh originalVest, runtimeVest;
    int skinContactFrame = -1;
    internal NativeArray<BoneWeight> CurrentClothWeights => clothWeights;
    internal NativeArray<float3> CurrentClothRest => clothRest;
    internal NativeArray<float4x4> CurrentClothMatrices => clothMatrices;
    static readonly ProfilerMarker marker = new ProfilerMarker("Cloth.AttachedSleeveSkinContact");

    void OnEnable() => MagicaManager.OnPostSimulation += Solve;
    void OnDisable()
    {
        skinContactFrame = -1;
        MagicaManager.OnPostSimulation -= Solve;
        if (clothRest.IsCreated && MagicaManager.IsPlaying() && cloth && cloth.IsValid() && cloth.Process.IsRunning())
        {
            var teams = MagicaManager.Team; var vm = MagicaManager.VMesh; var render = MagicaManager.Render;
            foreach (int index in teams.teamMappingIndexArray[cloth.Process.TeamId])
            {
                var mapping = teams.mappingDataArray[index]; var work = render.renderDataWorkArray[mapping.renderDataWorkIndex];
                if (!work.UseCustomMesh) continue;
                for (int i = 0; i < mapping.VertexCount; i++)
                {
                    int m = mapping.mappingCommonChunk.startIndex + i, r = vm.mappingReferenceIndices[m];
                    if (triangles[r].x >= 0 && vm.mappingAttributes[m].IsFixed()) render.renderMeshPositions[work.renderMeshPositionAndNormalChunk.startIndex + r] = clothRest[r];
                }
                work.flag.SetBits(RenderManager.RenderDataFlag_WritePositionNormal, true); render.renderDataWorkArray[mapping.renderDataWorkIndex] = work;
            }
        }
        if (bodyRest.IsCreated) bodyRest.Dispose(); if (clothRest.IsCreated) clothRest.Dispose();
        if (bodyWeights.IsCreated) bodyWeights.Dispose(); if (clothWeights.IsCreated) clothWeights.Dispose();
        if (bodyMatrices.IsCreated) bodyMatrices.Dispose(); if (clothMatrices.IsCreated) clothMatrices.Dispose();
        if (triangles.IsCreated) triangles.Dispose();
        if (clothWorld.IsCreated) clothWorld.Dispose();
        if (vestRest.IsCreated) vestRest.Dispose();
        if (vestPositions.IsCreated) vestPositions.Dispose();
        if (vestWeights.IsCreated) vestWeights.Dispose();
        if (vestMatrices.IsCreated) vestMatrices.Dispose();
        if (vestTriangles.IsCreated) vestTriangles.Dispose();
        if (vestCorrections.IsCreated) vestCorrections.Dispose();
        if (vestSmoothed.IsCreated) vestSmoothed.Dispose();
        if (vestNeighborRanges.IsCreated) vestNeighborRanges.Dispose();
        if (vestNeighbors.IsCreated) vestNeighbors.Dispose();
        if (runtimeVest) { if (vest && vest.sharedMesh == runtimeVest) vest.sharedMesh = originalVest; Destroy(runtimeVest); }
    }

    void Initialize()
    {
        if (cloth.SerializeData.sourceRenderers.Count != 1 || !(cloth.SerializeData.sourceRenderers[0] is SkinnedMeshRenderer renderer)
            || bodyTriangleIndices == null || bodyTriangleIndices.Length != restMesh.vertexCount * 3)
            throw new System.InvalidOperationException("Invalid attached sleeve contact binding.");
        var mesh = body.sharedMesh;
        foreach (int index in bodyTriangleIndices) if (index < -1 || index >= mesh.vertexCount) throw new System.InvalidOperationException("Invalid body triangle index.");
        bodyBind = mesh.bindposes; clothBind = restMesh.bindposes; bodyBones = body.bones; clothBones = renderer.bones;
        bodyRest = new NativeArray<float3>(mesh.vertexCount, Allocator.Persistent); clothRest = new NativeArray<float3>(restMesh.vertexCount, Allocator.Persistent);
        var b = mesh.vertices; var c = restMesh.vertices;
        for (int i = 0; i < b.Length; i++) bodyRest[i] = b[i]; for (int i = 0; i < c.Length; i++) clothRest[i] = c[i];
        bodyWeights = new NativeArray<BoneWeight>(mesh.boneWeights, Allocator.Persistent); clothWeights = new NativeArray<BoneWeight>(restMesh.boneWeights, Allocator.Persistent);
        bodyMatrices = new NativeArray<float4x4>(bodyBind.Length, Allocator.Persistent); clothMatrices = new NativeArray<float4x4>(clothBind.Length, Allocator.Persistent);
        triangles = new NativeArray<int3>(restMesh.vertexCount, Allocator.Persistent);
        for (int i = 0; i < triangles.Length; i++) triangles[i] = new int3(bodyTriangleIndices[i * 3], bodyTriangleIndices[i * 3 + 1], bodyTriangleIndices[i * 3 + 2]);
        if (vest)
        {
            originalVest = vest.sharedMesh;
            if (vestTriangleIndices == null || vestTriangleIndices.Length != originalVest.vertexCount * 3) throw new System.InvalidOperationException("Invalid vest contact binding.");
            foreach (int index in vestTriangleIndices) if (index < -1 || index >= restMesh.vertexCount) throw new System.InvalidOperationException("Invalid shirt triangle index.");
            vestBind = originalVest.bindposes; vestBones = vest.bones;
            clothWorld = new NativeArray<float3>(restMesh.vertexCount, Allocator.Persistent);
            vestRest = new NativeArray<float3>(originalVest.vertexCount, Allocator.Persistent);
            vestPositions = new NativeArray<float3>(originalVest.vertexCount, Allocator.Persistent);
            vestCorrections = new NativeArray<float3>(originalVest.vertexCount, Allocator.Persistent);
            vestSmoothed = new NativeArray<float3>(originalVest.vertexCount, Allocator.Persistent);
            var vv = originalVest.vertices; for (int i = 0; i < vv.Length; i++) vestRest[i] = vv[i];
            vestWeights = new NativeArray<BoneWeight>(originalVest.boneWeights, Allocator.Persistent);
            vestMatrices = new NativeArray<float4x4>(vestBind.Length, Allocator.Persistent);
            vestTriangles = new NativeArray<int3>(originalVest.vertexCount, Allocator.Persistent);
            for (int i = 0; i < vestTriangles.Length; i++) vestTriangles[i] = new int3(vestTriangleIndices[i * 3], vestTriangleIndices[i * 3 + 1], vestTriangleIndices[i * 3 + 2]);
            var adjacent = new System.Collections.Generic.HashSet<int>[vv.Length];
            for (int i = 0; i < vv.Length; i++) adjacent[i] = new System.Collections.Generic.HashSet<int>();
            var vt = originalVest.triangles;
            for (int i = 0; i < vt.Length; i += 3) for (int j = 0; j < 3; j++) { int a = vt[i + j], neighbor = vt[i + (j + 1) % 3]; adjacent[a].Add(neighbor); adjacent[neighbor].Add(a); }
            var seams = new System.Collections.Generic.Dictionary<Vector3Int, System.Collections.Generic.List<int>>();
            for (int i = 0; i < vv.Length; i++)
            {
                var key = Vector3Int.RoundToInt(vv[i] * 100000);
                if (!seams.TryGetValue(key, out var group)) seams.Add(key, group = new System.Collections.Generic.List<int>());
                group.Add(i);
            }
            foreach (var group in seams.Values)
            {
                if (group.Count < 2) continue;
                var union = new System.Collections.Generic.HashSet<int>(group); foreach (int i in group) union.UnionWith(adjacent[i]);
                foreach (int i in group) adjacent[i] = union;
            }
            vestNeighborRanges = new NativeArray<int2>(vv.Length, Allocator.Persistent); var neighbors = new System.Collections.Generic.List<int>();
            for (int i = 0; i < adjacent.Length; i++) { vestNeighborRanges[i] = new int2(neighbors.Count, adjacent[i].Count); neighbors.AddRange(adjacent[i]); }
            vestNeighbors = new NativeArray<int>(neighbors.ToArray(), Allocator.Persistent);
            runtimeVest = Instantiate(originalVest); runtimeVest.name = originalVest.name + " (runtime contact)"; runtimeVest.MarkDynamic(); vest.sharedMesh = runtimeVest;
        }
    }

    internal bool ApplySkinContact()
    {
        if (!Application.isPlaying || !MagicaManager.IsPlaying() || !isActiveAndEnabled || !cloth || !body || !restMesh || clearance <= 0 || !cloth.IsValid() || !cloth.Process.IsRunning()) return false;
        if (skinContactFrame == Time.frameCount) return true;
        using var sample = marker.Auto(); if (!triangles.IsCreated) Initialize();
        for (int i = 0; i < bodyBind.Length; i++) bodyMatrices[i] = bodyBones[i] ? (float4x4)(bodyBones[i].localToWorldMatrix * bodyBind[i]) : float4x4.zero;
        for (int i = 0; i < clothBind.Length; i++) clothMatrices[i] = clothBones[i] ? (float4x4)(clothBones[i].localToWorldMatrix * clothBind[i]) : float4x4.zero;
        var teams = MagicaManager.Team; var vm = MagicaManager.VMesh; var render = MagicaManager.Render;
        foreach (int index in teams.teamMappingIndexArray[cloth.Process.TeamId])
        {
            var mapping = teams.mappingDataArray[index]; var work = render.renderDataWorkArray[mapping.renderDataWorkIndex];
            if (!work.UseCustomMesh) continue;
            new ContactJob { start = mapping.mappingCommonChunk.startIndex, renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toWorld = math.inverse(mapping.toMappingMatrix), toLocal = mapping.toMappingMatrix, clearance = clearance,
                attributes = vm.mappingAttributes.GetNativeArray(), references = vm.mappingReferenceIndices.GetNativeArray(), triangles = triangles,
                bodyRest = bodyRest, clothRest = clothRest, bodyWeights = bodyWeights, clothWeights = clothWeights,
                bodyMatrices = bodyMatrices, clothMatrices = clothMatrices, positions = render.renderMeshPositions.GetNativeArray()
            }.Schedule(mapping.VertexCount, 64).Complete();
            work.flag.SetBits(RenderManager.RenderDataFlag_WritePositionNormal, true); render.renderDataWorkArray[mapping.renderDataWorkIndex] = work;
        }
        skinContactFrame = Time.frameCount;
        return true;
    }

    void Solve()
    {
        if (!ApplySkinContact()) return;
        using var sample = marker.Auto();
        var teams = MagicaManager.Team; var vm = MagicaManager.VMesh; var render = MagicaManager.Render;
        foreach (int index in teams.teamMappingIndexArray[cloth.Process.TeamId])
        {
            var mapping = teams.mappingDataArray[index]; var work = render.renderDataWorkArray[mapping.renderDataWorkIndex];
            if (!work.UseCustomMesh) continue;
            if (vest)
            {
                new WorldJob { start = mapping.mappingCommonChunk.startIndex, renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                    toWorld = math.inverse(mapping.toMappingMatrix), attributes = vm.mappingAttributes.GetNativeArray(), references = vm.mappingReferenceIndices.GetNativeArray(),
                    weights = clothWeights, matrices = clothMatrices, positions = render.renderMeshPositions.GetNativeArray(), world = clothWorld
                }.Schedule(mapping.VertexCount, 64).Complete();
                for (int i = 0; i < vestBind.Length; i++) vestMatrices[i] = vestBones[i] ? (float4x4)(vestBones[i].localToWorldMatrix * vestBind[i]) : float4x4.zero;
                var bodyContact = new VestBodyContact { rest = bodyRest, weights = bodyWeights, matrices = bodyMatrices, triangles = triangles };
                new VestJob { rest = vestRest, corrections = vestCorrections, weights = vestWeights, matrices = vestMatrices, triangles = vestTriangles, shirt = clothWorld, body = bodyContact
                }.Schedule(vestRest.Length, 64).Complete();
                // Spread contact displacement across neighboring cloth vertices without smoothing the authored mesh.
                for (int pass = 0; pass < 4; pass++)
                {
                    new SmoothJob { source = vestCorrections, target = vestSmoothed, ranges = vestNeighborRanges, neighbors = vestNeighbors }.Schedule(vestRest.Length, 64).Complete();
                    var swap = vestCorrections; vestCorrections = vestSmoothed; vestSmoothed = swap;
                }
                new WriteVestJob { rest = vestRest, corrections = vestCorrections, positions = vestPositions, weights = vestWeights, matrices = vestMatrices, triangles = vestTriangles, body = bodyContact }.Schedule(vestRest.Length, 64).Complete();
                runtimeVest.SetVertices(vestPositions);
            }
            work.flag.SetBits(RenderManager.RenderDataFlag_WritePositionNormal, true); render.renderDataWorkArray[mapping.renderDataWorkIndex] = work;
        }
    }

    // shortcut: mapped triangles handle local arm contacts; deep or unrelated body crossings need a wider surface query.
    public static float3 Correction(float3 p, float3 a, float3 b, float3 c, float gap)
    {
        if (!math.all(math.isfinite(p))) return 0;
        var n = math.normalizesafe(math.cross(b - a, c - a));
        var q = MathUtility.ClosestPtPointTriangle(p, a, b, c, out _);
        float signed = math.dot(p - q, n);
        if (math.lengthsq(n) < .5f || !math.all(math.isfinite(q)) || math.distancesq(p, q) > .06f * .06f || signed >= gap) return 0;
        return n * math.min(.04f, gap - signed);
    }

    static float4x4 SkinMatrix(BoneWeight w, NativeArray<float4x4> m) => m[w.boneIndex0] * w.weight0 + m[w.boneIndex1] * w.weight1 + m[w.boneIndex2] * w.weight2 + m[w.boneIndex3] * w.weight3;

    public static float3 VestCorrection(float3 p, float3 a, float3 b, float3 c, float3 outward, float gap)
    {
        var n = math.normalizesafe(math.cross(b - a, c - a));
        if (math.dot(n, outward) < 0) n = -n;
        var q = MathUtility.ClosestPtPointTriangle(p, a, b, c, out _);
        if (!math.all(math.isfinite(q)) || math.lengthsq(n) < .5f || math.distancesq(p, q) > .06f * .06f) return 0;
        return outward * (math.max(0, math.dot(n, outward)) * math.clamp(gap - math.dot(p - q, n), 0, .04f));
    }

    static float3 OutwardOnly(float3 delta, float3 outward) => outward * math.max(0, math.dot(delta, outward));

    public static void ValidateVestContact()
    {
        float3 a = 0, b = new float3(1, 0, 0), c = new float3(0, 1, 0), outward = new float3(0, 0, 1);
        var p = new float3(.2f, .2f, .001f);
        var expected = new float3(0, 0, .005f);
        if (math.distance(VestCorrection(p, a, b, c, outward, .006f), expected) > 1e-6f
            || math.distance(VestCorrection(p, a, c, b, outward, .006f), expected) > 1e-6f)
            throw new System.InvalidOperationException("Folded shirt winding changed vest contact direction.");
        if (math.lengthsq(VestCorrection(p + outward * .02f, a, c, b, outward, .006f)) > 1e-12f
            || math.lengthsq(OutwardOnly(new float3(.03f, 0, -.02f), outward)) > 1e-12f)
            throw new System.InvalidOperationException("Vest contact moved an outside vertex inward or sideways.");
        p.z = -.01f;
        if (math.abs((p + Correction(p, a, b, c, .002f)).z - .002f) > 1e-6f)
            throw new System.InvalidOperationException("Final vest skin clearance failed.");
    }

    struct VestBodyContact
    {
        [ReadOnly] public NativeArray<float3> rest;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [ReadOnly] public NativeArray<int3> triangles;
        float3 Point(int i) => math.transform(SkinMatrix(weights[i], matrices), rest[i]);
        public bool TryGet(float3 p, int3 shirt, out float3 a, out float3 b, out float3 c)
        {
            a = b = c = 0; float best = float.MaxValue;
            if (shirt.x < 0) return false;
            // Reuse the bound torso skin, so a nearby arm cannot redirect the vest correction.
            for (int i = 0; i < 3; i++)
            {
                var t = triangles[shirt[i]]; if (t.x < 0) continue;
                var aa = Point(t.x); var bb = Point(t.y); var cc = Point(t.z);
                if (math.lengthsq(math.cross(bb - aa, cc - aa)) < 1e-14f) continue;
                var q = MathUtility.ClosestPtPointTriangle(p, aa, bb, cc, out _);
                float distance = math.distancesq(p, q);
                if (distance >= best) continue;
                best = distance; a = aa; b = bb; c = cc;
            }
            return best < .06f * .06f;
        }
    }

    public static void ValidateSmoothing()
    {
        using var source = new NativeArray<float3>(new[] { new float3(.01f), new float3(.01f), new float3(.01f) }, Allocator.TempJob);
        using var target = new NativeArray<float3>(3, Allocator.TempJob);
        using var ranges = new NativeArray<int2>(new[] { new int2(0, 2), new int2(2, 2), new int2(4, 2) }, Allocator.TempJob);
        using var neighbors = new NativeArray<int>(new[] { 1, 2, 0, 2, 0, 1 }, Allocator.TempJob);
        new SmoothJob { source = source, target = target, ranges = ranges, neighbors = neighbors }.Schedule(3, 1).Complete();
        for (int i = 0; i < 3; i++) if (math.distance(target[i], source[i]) > 1e-7f) throw new System.InvalidOperationException("Uniform correction changed.");
        source.CopyFrom(new[] { new float3(.04f, 0, 0), float3.zero, float3.zero });
        new SmoothJob { source = source, target = target, ranges = ranges, neighbors = neighbors }.Schedule(3, 1).Complete();
        if (math.abs(target[0].x - .02f) > 1e-7f || math.abs(target[1].x - .01f) > 1e-7f || math.abs(target[2].x - .01f) > 1e-7f) throw new System.InvalidOperationException("Contact spread failed.");
    }

    [BurstCompile] struct WorldJob : IJobParallelFor
    {
        public int start, renderStart; public float4x4 toWorld;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [ReadOnly] public NativeArray<float3> positions;
        [NativeDisableParallelForRestriction] public NativeArray<float3> world;
        public void Execute(int i)
        {
            int m = start + i, r = references[m];
            world[r] = math.transform(attributes[m].IsFixed() ? SkinMatrix(weights[r], matrices) : toWorld, positions[renderStart + r]);
        }
    }

    [BurstCompile] struct VestJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> rest, shirt;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [ReadOnly] public NativeArray<int3> triangles;
        public VestBodyContact body;
        public NativeArray<float3> corrections;
        public void Execute(int i)
        {
            corrections[i] = 0; var t = triangles[i]; if (t.x < 0) return;
            var skin = SkinMatrix(weights[i], matrices); var p = math.transform(skin, rest[i]);
            if (!body.TryGet(p, t, out var a, out var b, out var c)) return;
            var outward = math.normalizesafe(math.cross(b - a, c - a));
            corrections[i] = VestCorrection(p, shirt[t.x], shirt[t.y], shirt[t.z], outward, .006f);
        }
    }

    [BurstCompile] struct SmoothJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> source;
        [ReadOnly] public NativeArray<int2> ranges;
        [ReadOnly] public NativeArray<int> neighbors;
        public NativeArray<float3> target;
        public void Execute(int i)
        {
            var range = ranges[i]; float3 sum = 0;
            for (int j = 0; j < range.y; j++) sum += source[neighbors[range.x + j]];
            target[i] = range.y > 0 ? math.lerp(source[i], sum / range.y, .5f) : source[i];
        }
    }

    [BurstCompile] struct WriteVestJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> rest, corrections;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [ReadOnly] public NativeArray<int3> triangles;
        public VestBodyContact body;
        public NativeArray<float3> positions;
        public void Execute(int i)
        {
            positions[i] = rest[i];
            var skin = SkinMatrix(weights[i], matrices);
            var p = math.transform(skin, rest[i]);
            if (!body.TryGet(p, triangles[i], out var a, out var b, out var c)) return;
            var outward = math.normalizesafe(math.cross(b - a, c - a));
            var delta = OutwardOnly(corrections[i], outward);
            // Recheck skin after smoothing, without pulling the authored vest inward.
            delta += Correction(p + delta, a, b, c, .002f);
            delta = OutwardOnly(delta, outward);
            if (math.lengthsq(delta) < 1e-12f) return;
            var corrected = rest[i] + math.mul(math.inverse(skin), new float4(delta, 0)).xyz;
            if (math.all(math.isfinite(corrected))) positions[i] = corrected;
        }
    }

    [BurstCompile] struct ContactJob : IJobParallelFor
    {
        public int start, renderStart; public float4x4 toWorld, toLocal; public float clearance;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<float3> bodyRest, clothRest;
        [ReadOnly] public NativeArray<BoneWeight> bodyWeights, clothWeights;
        [ReadOnly] public NativeArray<float4x4> bodyMatrices, clothMatrices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        static float4x4 Matrix(BoneWeight w, NativeArray<float4x4> m) => m[w.boneIndex0] * w.weight0 + m[w.boneIndex1] * w.weight1 + m[w.boneIndex2] * w.weight2 + m[w.boneIndex3] * w.weight3;
        float3 BodyPoint(int i) => math.transform(Matrix(bodyWeights[i], bodyMatrices), bodyRest[i]);
        public void Execute(int i)
        {
            int m = start + i, reference = references[m]; var t = triangles[reference]; if (t.x < 0) return;
            int r = renderStart + reference; bool fixedVertex = attributes[m].IsFixed();
            var skin = fixedVertex ? Matrix(clothWeights[reference], clothMatrices) : toWorld;
            var p = math.transform(skin, fixedVertex ? clothRest[reference] : positions[r]);
            var correction = Correction(p, BodyPoint(t.x), BodyPoint(t.y), BodyPoint(t.z), clearance);
            if (fixedVertex) positions[r] = clothRest[reference];
            if (math.lengthsq(correction) < 1e-12f) return;
            var corrected = math.transform(fixedVertex ? math.inverse(skin) : toLocal, p + correction);
            if (math.all(math.isfinite(corrected))) positions[r] = corrected;
        }
    }
}

}
