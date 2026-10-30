namespace YPH.FinalPlayer3Cloth
{
using System;
using System.Collections.Generic;
using MagicaCloth2;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Only the skirt originally covered by the vest follows its outer surface, after existing corrections.
[DisallowMultipleComponent]
public sealed class SkirtVestLayerContact : MonoBehaviour
{
    public MagicaCloth cloth;
    public Mesh restMesh;
    public SkinnedMeshRenderer vest;
    public int[] vestBinding;
    public float[] vestNormalSign, vestMask;
    public double LastSolveMilliseconds { get; private set; }
    public int LastCorrectedVertices { get; private set; }
    public float LastLayerError { get; private set; }
    public double LastRearContactMilliseconds { get; private set; }

    MeshState skirt;
    Mesh bakedVest;
    readonly List<Vector3> vestVertices = new List<Vector3>();
    NativeArray<float3> vestWorld;
    NativeArray<int3> vestTriangles, binding;
    NativeArray<SkirtBodySurfaceContact.ContactNode> vestNodes;
    NativeArray<int> vestOrder;
    NativeArray<float> signs, mask, metrics;
    bool started;
    RearSkirtSurfacePatch rearPatch;
    SkirtBodySurfaceContact bodyContact;
#if UNITY_EDITOR
    public void ValidateRearContactIsolation()
    {
        if (rearPatch == null || !bodyContact.TryGetCurrentSurface(out var surface)) throw new InvalidOperationException("Rear contact not ready.");
        var before = skirt.world.ToArray(); var beforeNormals = skirt.worldNormals.ToArray(); var vestBefore = vest.sharedMesh.vertices;
        rearPatch.Update(skirt.world, skirt.worldNormals, surface, vestWorld, vestTriangles, vestNodes, vestOrder);
        for (int i = 0; i < before.Length; i++) if (math.any(before[i] != skirt.world[i]) || math.any(beforeNormals[i] != skirt.worldNormals[i])) throw new InvalidOperationException("Rear contact modified simulation input.");
        var vestAfter = vest.sharedMesh.vertices;
        for (int i = 0; i < vestBefore.Length; i++) if (vestBefore[i] != vestAfter[i]) throw new InvalidOperationException("Rear contact modified vest.");
        rearPatch.ValidateBoundary(skirt.world);
    }
#endif

    void Start()
    {
        try { Initialize(); started = true; Register(); }
        catch (Exception e) { Release(); enabled = false; Debug.LogException(e, this); }
    }
    void OnEnable() { if (started) Register(); }
    void Register()
    {
        // Start runs after the existing components subscribe in OnEnable.
        MagicaManager.OnPostSimulation -= Solve;
        MagicaManager.OnPostSimulation += Solve;
    }
    void OnDisable() { MagicaManager.OnPostSimulation -= Solve; rearPatch?.Dispose(); rearPatch = null; }
    void OnDestroy() { MagicaManager.OnPostSimulation -= Solve; Release(); }
    void Release()
    {
        rearPatch?.Dispose(); rearPatch = null;
        skirt?.Dispose(); skirt = null;
        if (bakedVest) Destroy(bakedVest);
        if (vestWorld.IsCreated) vestWorld.Dispose();
        if (vestTriangles.IsCreated) vestTriangles.Dispose();
        if (vestNodes.IsCreated) vestNodes.Dispose();
        if (vestOrder.IsCreated) vestOrder.Dispose();
        if (binding.IsCreated) binding.Dispose();
        if (signs.IsCreated) signs.Dispose();
        if (mask.IsCreated) mask.Dispose();
        if (metrics.IsCreated) metrics.Dispose();
    }
    void Initialize()
    {
        if (!cloth || !restMesh || !vest || !vest.sharedMesh || cloth.SerializeData.sourceRenderers.Count != 1
            || !(cloth.SerializeData.sourceRenderers[0] is SkinnedMeshRenderer renderer)
            || renderer.sharedMesh.vertexCount != restMesh.vertexCount
            || vestBinding == null || vestMask == null || vestNormalSign == null
            || vestBinding.Length != restMesh.vertexCount * 3
            || vestMask.Length != restMesh.vertexCount || vestNormalSign.Length != vestMask.Length)
            throw new InvalidOperationException("Skirt/vest binding must match one skirt renderer and its rest mesh.");
        var vestMesh = vest.sharedMesh;
        int vestCount = vestMesh.vertexCount;
        for (int i = 0; i < vestMask.Length; i++)
        {
            var t = new int3(vestBinding[i * 3], vestBinding[i * 3 + 1], vestBinding[i * 3 + 2]);
            if (!math.isfinite(vestMask[i]) || vestMask[i] < 0 || vestMask[i] > 1
                || (vestMask[i] > 0 && (math.any(t < 0) || math.any(t >= vestCount) || math.abs(vestNormalSign[i]) != 1)))
                throw new InvalidOperationException("Invalid vest triangle binding.");
        }
        skirt = new MeshState(cloth, restMesh);
        bodyContact = cloth.GetComponent<SkirtBodySurfaceContact>();
        binding = new NativeArray<int3>(vestMask.Length, Allocator.Persistent);
        for (int i = 0; i < binding.Length; i++) binding[i] = new int3(vestBinding[i * 3], vestBinding[i * 3 + 1], vestBinding[i * 3 + 2]);
        signs = new NativeArray<float>(vestNormalSign, Allocator.Persistent);
        mask = new NativeArray<float>(vestMask, Allocator.Persistent);
        vestWorld = new NativeArray<float3>(vestCount, Allocator.Persistent);
        var indices = vestMesh.triangles;
        vestTriangles = new NativeArray<int3>(indices.Length / 3, Allocator.Persistent);
        for (int i = 0; i < vestTriangles.Length; i++) vestTriangles[i] = new int3(indices[i * 3], indices[i * 3 + 1], indices[i * 3 + 2]);
        SkirtBodySurfaceContact.BuildHierarchy(vestMesh, out var nodes, out var order);
        vestNodes = new NativeArray<SkirtBodySurfaceContact.ContactNode>(nodes, Allocator.Persistent);
        vestOrder = new NativeArray<int>(order, Allocator.Persistent);
        bakedVest = new Mesh { name = "Skirt vest overlap query" };
        metrics = new NativeArray<float>(2, Allocator.Persistent);
    }
    void Solve()
    {
        if (!cloth || !vest || !cloth.IsValid() || !cloth.Process.IsRunning()) return;
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        if (!skirt.Read()) { rearPatch?.Dispose(); rearPatch = null; return; }
        vest.BakeMesh(bakedVest, false); bakedVest.GetVertices(vestVertices);
        if (vestVertices.Count != vestWorld.Length) throw new InvalidOperationException("Vest topology changed; rebuild overlap binding.");
        var transform = Matrix4x4.TRS(vest.transform.position, vest.transform.rotation, Vector3.one);
        for (int i = 0; i < vestVertices.Count; i++) vestWorld[i] = transform.MultiplyPoint3x4(vestVertices[i]);
        new SkirtBodySurfaceContact.RefitJob { points = vestWorld, triangles = vestTriangles, nodes = vestNodes, order = vestOrder }.Schedule().Complete();
        new LayerJob { world = skirt.world, original = skirt.original, vest = vestWorld, binding = binding,
            signs = signs, mask = mask, metrics = metrics, triangles = vestTriangles, nodes = vestNodes, order = vestOrder }.Schedule().Complete();
        if (metrics[0] > 0) skirt.Write();
        if (bodyContact && bodyContact.TryGetCurrentSurface(out var surface))
        {
            long rearBegan = System.Diagnostics.Stopwatch.GetTimestamp();
            if (rearPatch == null) rearPatch = new RearSkirtSurfacePatch((SkinnedMeshRenderer)cloth.SerializeData.sourceRenderers[0], restMesh, this.transform, vestMask, skirt.representatives);
            rearPatch.Update(skirt.world, skirt.worldNormals, surface, vestWorld, vestTriangles, vestNodes, vestOrder);
            LastRearContactMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - rearBegan) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        }
        else { rearPatch?.Dispose(); rearPatch = null; }
        LastCorrectedVertices = (int)metrics[0]; LastLayerError = metrics[1];
        LastSolveMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    sealed class MeshState : IDisposable
    {
        readonly MagicaCloth cloth;
        readonly Transform[] bones;
        readonly Matrix4x4[] bindposes;
        readonly NativeArray<BoneWeight> weights;
        NativeArray<float4x4> matrices;
        public NativeArray<float3> world, original, normals;
        public NativeArray<float3> worldNormals;
        public NativeArray<int> representatives;
        public NativeArray<int3> triangles;
        TeamManager.MappingData mapping;
        RenderManager.RenderDataWork work;
        public MeshState(MagicaCloth cloth, Mesh rest)
        {
            this.cloth = cloth;
            var renderer = (SkinnedMeshRenderer)cloth.SerializeData.sourceRenderers[0];
            // Cache before MagicaCloth removes unused renderer bone references.
            bones = renderer.bones;
            bindposes = rest.bindposes;
            weights = new NativeArray<BoneWeight>(rest.boneWeights, Allocator.Persistent);
            matrices = new NativeArray<float4x4>(bindposes.Length, Allocator.Persistent);
            world = new NativeArray<float3>(rest.vertexCount, Allocator.Persistent);
            original = new NativeArray<float3>(rest.vertexCount, Allocator.Persistent);
            normals = new NativeArray<float3>(rest.vertexCount, Allocator.Persistent);
            worldNormals = new NativeArray<float3>(rest.vertexCount, Allocator.Persistent);
            representatives = new NativeArray<int>(rest.vertexCount, Allocator.Persistent);
            var groups = new Dictionary<(Vector3, BoneWeight), int>();
            var vertices = rest.vertices; var bw = rest.boneWeights;
            for (int i = 0; i < vertices.Length; i++)
            {
                var key = (vertices[i], bw[i]);
                if (!groups.TryGetValue(key, out int r)) { r = i; groups.Add(key, r); }
                representatives[i] = r;
            }
            var indices = rest.triangles;
            triangles = new NativeArray<int3>(indices.Length / 3, Allocator.Persistent);
            for (int i = 0; i < triangles.Length; i++) triangles[i] = new int3(representatives[indices[i * 3]], representatives[indices[i * 3 + 1]], representatives[indices[i * 3 + 2]]);
        }
        public bool Read()
        {
            var teams = MagicaManager.Team;
            if (!teams.teamDataArray[cloth.Process.TeamId].IsProcess) return false;
            var mappings = teams.teamMappingIndexArray[cloth.Process.TeamId];
            if (mappings.Length != 1) throw new InvalidOperationException("Final contact requires one renderer per cloth.");
            mapping = teams.mappingDataArray[mappings[0]];
            work = MagicaManager.Render.renderDataWorkArray[mapping.renderDataWorkIndex];
            if (!work.UseCustomMesh) return false;
            for (int i = 0; i < matrices.Length; i++) matrices[i] = bones[i] ? (float4x4)(bones[i].localToWorldMatrix * bindposes[i]) : float4x4.zero;
            new ReadJob { start = mapping.mappingCommonChunk.startIndex, renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toWorld = math.inverse(mapping.toMappingMatrix), attributes = MagicaManager.VMesh.mappingAttributes.GetNativeArray(),
                references = MagicaManager.VMesh.mappingReferenceIndices.GetNativeArray(), positions = MagicaManager.Render.renderMeshPositions.GetNativeArray(),
                weights = weights, matrices = matrices, world = world, original = original,
                sourceNormals = MagicaManager.Render.renderMeshNormals.GetNativeArray(), worldNormals = worldNormals
            }.Schedule(mapping.VertexCount, 64).Complete();
            return true;
        }
        public void Write()
        {
            new NormalJob { world = world, triangles = triangles, normals = normals }.Schedule().Complete();
            for (int i = 0; i < world.Length; i++) if (math.any(world[i] != original[i])) worldNormals[i] = math.normalizesafe(normals[representatives[i]]);
            new WriteJob { start = mapping.mappingCommonChunk.startIndex, renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toLocal = mapping.toMappingMatrix, attributes = MagicaManager.VMesh.mappingAttributes.GetNativeArray(),
                references = MagicaManager.VMesh.mappingReferenceIndices.GetNativeArray(), positions = MagicaManager.Render.renderMeshPositions.GetNativeArray(),
                weights = weights, matrices = matrices, world = world, original = original, representatives = representatives,
                normals = normals, renderNormals = MagicaManager.Render.renderMeshNormals.GetNativeArray()
            }.Schedule(mapping.VertexCount, 64).Complete();
            work.flag.SetBits(RenderManager.RenderDataFlag_WritePositionNormal, true);
            MagicaManager.Render.renderDataWorkArray[mapping.renderDataWorkIndex] = work;
        }
        public void Dispose()
        {
            weights.Dispose(); matrices.Dispose(); world.Dispose(); original.Dispose(); normals.Dispose();
            worldNormals.Dispose();
            representatives.Dispose(); triangles.Dispose();
        }
    }
    static float4x4 Skin(BoneWeight w, NativeArray<float4x4> m) => m[w.boneIndex0] * w.weight0 + m[w.boneIndex1] * w.weight1 + m[w.boneIndex2] * w.weight2 + m[w.boneIndex3] * w.weight3;
    [BurstCompile] struct ReadJob : IJobParallelFor
    {
        public int start, renderStart; public float4x4 toWorld;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> sourceNormals;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> world, original;
        [NativeDisableParallelForRestriction] public NativeArray<float3> worldNormals;
        public void Execute(int i)
        {
            int m = start + i, r = references[m];
            var matrix = attributes[m].IsFixed() ? Skin(weights[r], matrices) : toWorld;
            var p = math.transform(matrix, positions[renderStart + r]);
            world[r] = original[r] = p;
            worldNormals[r] = math.normalizesafe(math.mul(math.transpose(math.inverse(new float3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz))), sourceNormals[renderStart + r]));
        }
    }
    [BurstCompile] struct WriteJob : IJobParallelFor
    {
        public int start, renderStart; public float4x4 toLocal;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references, representatives;
        [ReadOnly] public NativeArray<float3> world, original, normals;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positions, renderNormals;
        public void Execute(int i)
        {
            int m = start + i, r = references[m]; var attribute = attributes[m];
            if (attribute.IsInvalid()) return;
            float3 p = world[r];
            if (math.all(p == original[r])) return;
            var matrix = attribute.IsFixed() ? Skin(weights[r], matrices) : math.inverse(toLocal);
            if (math.abs(math.determinant(matrix)) < 1e-12f || !math.all(math.isfinite(p))) return;
            positions[renderStart + r] = math.transform(math.inverse(matrix), p);
            if (math.distancesq(p, original[r]) > 1e-10f && math.lengthsq(normals[representatives[r]]) > 1e-12f)
                renderNormals[renderStart + r] = math.normalizesafe(math.mul(math.transpose(new float3x3(matrix.c0.xyz, matrix.c1.xyz, matrix.c2.xyz)), normals[representatives[r]]));
        }
    }

    [BurstCompile] struct NormalJob : IJob
    {
        [ReadOnly] public NativeArray<float3> world;
        [ReadOnly] public NativeArray<int3> triangles;
        public NativeArray<float3> normals;
       
        public void Execute()
        {
            for (int i = 0; i < normals.Length; i++) normals[i] = 0;
            foreach (var t in triangles)
            {
                var n = math.cross(world[t.y] - world[t.x], world[t.z] - world[t.x]);
                normals[t.x] += n; normals[t.y] += n; normals[t.z] += n;
            }
        }
    }

    [BurstCompile] internal struct LayerJob : IJob
    {
        public NativeArray<float3> world;
        [ReadOnly] public NativeArray<float3> original, vest;
        [ReadOnly] public NativeArray<int3> binding;
        [ReadOnly] public NativeArray<float> signs, mask;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<SkirtBodySurfaceContact.ContactNode> nodes;
        [ReadOnly] public NativeArray<int> order;
        public NativeArray<float> metrics;
        public void Execute()
        {
            metrics[0] = metrics[1] = 0;
            for (int i = 0; i < world.Length; i++)
            {
                var t = binding[i]; if (t.x < 0 || mask[i] <= 0) continue;
                var p = original[i];
                var direction = math.normalizesafe(math.cross(vest[t.y] - vest[t.x], vest[t.z] - vest[t.x])) * signs[i];
                if (!FindCoveredSurface(p, direction, out var hit, out var normal)) continue;
                // The vest surface takes priority over skin contact in the covered region.
                // shortcut: only covered vertices within 60mm are attached; face crossings need separate treatment.
                var target = hit - normal * .010f;
                p = math.lerp(p, target, mask[i]);
                if (math.distancesq(p, original[i]) > 1e-12f) metrics[0]++;
                if (mask[i] >= .999f) metrics[1] = math.max(metrics[1], math.distance(p, target));
                world[i] = p;
            }
        }
        bool FindCoveredSurface(float3 p, float3 direction, out float3 hit, out float3 normal)
        {
            hit = normal = 0; float best = .06f;
            if (math.lengthsq(direction) < .5f) return false;
            var start = p - direction * .06f; var end = p + direction * .06f;
            var inverse = 1 / (end - start);
            FixedList512Bytes<int> stack = default; stack.Add(0); bool found = false;
            while (stack.Length > 0)
            {
                var node = nodes[stack[stack.Length - 1]]; stack.RemoveAt(stack.Length - 1);
                var aa = (node.lower - start) * inverse; var bb = (node.upper - start) * inverse;
                if (math.max(0, math.cmax(math.min(aa, bb))) > math.min(1, math.cmin(math.max(aa, bb)))) continue;
                if (node.count == 0) { stack.Add(node.left); stack.Add(node.right); continue; }
                for (int j = node.start; j < node.start + node.count; j++)
                {
                    var t = triangles[order[j]]; var a = vest[t.x]; var b = vest[t.y]; var c = vest[t.z];
                    if (!MathUtility.IntersectSegmentTriangle(start, end, a, b, c, true, out _, out _, out _, out float along)) continue;
                    float distance = math.abs(along * .12f - .06f);
                    if (distance >= best) continue;
                    var n = math.normalizesafe(math.cross(b - a, c - a));
                    // Ignore inward-facing lining, but prefer nearby outer cloth over distant belts/buckles.
                    if (math.dot(n, direction) < .3f) continue;
                    best = distance; hit = math.lerp(start, end, along); normal = n; found = true;
                }
            }
            return found;
        }
    }
}

}
