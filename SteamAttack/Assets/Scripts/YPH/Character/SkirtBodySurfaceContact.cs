using MagicaCloth2;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

// MagicaCloth가 렌더 메시를 쓰기 전에 피부 접촉을 보정한다.
namespace YPH
{
[DisallowMultipleComponent]
public sealed class SkirtBodySurfaceContact : MonoBehaviour
{
    [Tooltip("피부와의 접촉을 보정할 치마 MagicaCloth")]
    public MagicaCloth cloth;
    [Tooltip("충돌 표면의 본을 제공하는 바디 렌더러")]
    public SkinnedMeshRenderer body;
    [Tooltip("바디 본에 맞춰 변형할 충돌 전용 피부 메시")]
    public UnityEngine.Mesh collisionSurface;
    [Tooltip("물리 적용 전 치마 메시. 정점 순서를 유지해야 한다")]
    public UnityEngine.Mesh restClothMesh;
    [Tooltip("피부와 치마 사이 간격(m). 기본 0.006, 0이면 접촉 보정 중지")]
    [Min(0)] public float clearance = .006f;

    public int LastCorrectedParticles { get; private set; }
    public float LastMaximumCorrection { get; private set; }
    public double LastSolveMilliseconds { get; private set; }
    NativeArray<float3> rest, points, corrections;
    NativeArray<BoneWeight> skinWeights;
    NativeArray<float4x4> matrices;
    NativeArray<int3> triangles;
    NativeArray<ContactNode> hierarchy;
    NativeArray<int> triangleOrder;
    NativeArray<float3> clothRest;
    NativeArray<BoneWeight> clothWeights;
    NativeArray<float4x4> clothMatrices;
    NativeArray<float3> clothWorld;
    NativeArray<int3> clothTriangles;
    int2[] faceBatches;
    Matrix4x4[] clothBindposes;
    Transform[] clothBones;
    Matrix4x4[] bindposes;
    Transform[] bones;
    UnityEngine.Mesh initializedSurface;
    static readonly ProfilerMarker marker = new ProfilerMarker("Cloth.SkirtBodySurfaceContact");

    void OnEnable() => MagicaManager.OnPostSimulation += Solve;
    void OnDisable()
    {
        MagicaManager.OnPostSimulation -= Solve;
        RestoreFixedVertices();
        Release();
    }

    void Release()
    {
        if (rest.IsCreated) rest.Dispose();
        if (points.IsCreated) points.Dispose();
        if (corrections.IsCreated) corrections.Dispose();
        if (skinWeights.IsCreated) skinWeights.Dispose();
        if (matrices.IsCreated) matrices.Dispose();
        if (triangles.IsCreated) triangles.Dispose();
        if (hierarchy.IsCreated) hierarchy.Dispose();
        if (triangleOrder.IsCreated) triangleOrder.Dispose();
        if (clothRest.IsCreated) clothRest.Dispose();
        if (clothWeights.IsCreated) clothWeights.Dispose();
        if (clothMatrices.IsCreated) clothMatrices.Dispose();
        if (clothWorld.IsCreated) clothWorld.Dispose();
        if (clothTriangles.IsCreated) clothTriangles.Dispose();
        initializedSurface = null;
    }

    void RestoreFixedVertices()
    {
        if (!clothRest.IsCreated || !MagicaManager.IsPlaying() || !cloth || !cloth.IsValid() || !cloth.Process.IsRunning()) return;
        var teams = MagicaManager.Team;
        var vm = MagicaManager.VMesh;
        var render = MagicaManager.Render;
        var mappings = teams.teamMappingIndexArray[cloth.Process.TeamId];
        for (int i = 0; i < mappings.Length; i++)
        {
            var mapping = teams.mappingDataArray[mappings[i]];
            var work = render.renderDataWorkArray[mapping.renderDataWorkIndex];
            if (!work.UseCustomMesh) continue;
            for (int j = 0; j < mapping.VertexCount; j++)
            {
                int m = mapping.mappingCommonChunk.startIndex + j;
                int reference = vm.mappingReferenceIndices[m];
                if (vm.mappingAttributes[m].IsFixed()) render.renderMeshPositions[work.renderMeshPositionAndNormalChunk.startIndex + reference] = clothRest[reference];
            }
            work.flag.SetBits(RenderManager.RenderDataFlag_WritePositionNormal, true);
            render.renderDataWorkArray[mapping.renderDataWorkIndex] = work;
        }
    }

    void Initialize()
    {
        Release();
        var vertices = collisionSurface.vertices;
        var indices = collisionSurface.triangles;
        bindposes = collisionSurface.bindposes;
        bones = body.bones;
        if (bones.Length < bindposes.Length || indices.Length == 0)
            throw new System.InvalidOperationException("Body contact surface does not match body skeleton.");
        rest = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
        points = new NativeArray<float3>(vertices.Length, Allocator.Persistent);
        for (int i = 0; i < vertices.Length; i++) rest[i] = vertices[i];
        skinWeights = new NativeArray<BoneWeight>(collisionSurface.boneWeights, Allocator.Persistent);
        matrices = new NativeArray<float4x4>(bindposes.Length, Allocator.Persistent);
        triangles = new NativeArray<int3>(indices.Length / 3, Allocator.Persistent);
        for (int i = 0; i < triangles.Length; i++) triangles[i] = new int3(indices[i * 3], indices[i * 3 + 1], indices[i * 3 + 2]);
        BuildHierarchy(collisionSurface, out var nodes, out var order);
        hierarchy = new NativeArray<ContactNode>(nodes, Allocator.Persistent);
        triangleOrder = new NativeArray<int>(order, Allocator.Persistent);
        if (cloth.SerializeData.sourceRenderers.Count != 1 || !(cloth.SerializeData.sourceRenderers[0] is SkinnedMeshRenderer skirt))
            throw new System.InvalidOperationException("Skin contact requires one skinned skirt renderer.");
        clothBindposes = restClothMesh.bindposes;
        clothBones = skirt.bones;
        clothMatrices = new NativeArray<float4x4>(clothBindposes.Length, Allocator.Persistent);
        var cv = restClothMesh.vertices;
        clothRest = new NativeArray<float3>(cv.Length, Allocator.Persistent);
        for (int i = 0; i < cv.Length; i++) clothRest[i] = cv[i];
        clothWeights = new NativeArray<BoneWeight>(restClothMesh.boneWeights, Allocator.Persistent);
        clothWorld = new NativeArray<float3>(cv.Length, Allocator.Persistent);
        var ct = restClothMesh.triangles;
        var used = new System.Collections.Generic.HashSet<int>[cv.Length];
        for (int i = 0; i < used.Length; i++) used[i] = new System.Collections.Generic.HashSet<int>();
        var batches = new System.Collections.Generic.List<System.Collections.Generic.List<int3>>();
        for (int i = 0; i < ct.Length; i += 3)
        {
            var t = new int3(ct[i], ct[i + 1], ct[i + 2]); int color = 0;
            while (used[t.x].Contains(color) || used[t.y].Contains(color) || used[t.z].Contains(color)) color++;
            while (batches.Count <= color) batches.Add(new System.Collections.Generic.List<int3>());
            batches[color].Add(t); used[t.x].Add(color); used[t.y].Add(color); used[t.z].Add(color);
        }
        clothTriangles = new NativeArray<int3>(ct.Length / 3, Allocator.Persistent);
        faceBatches = new int2[batches.Count]; int cursor = 0;
        for (int i = 0; i < batches.Count; i++)
        {
            faceBatches[i] = new int2(cursor, batches[i].Count);
            var check = new System.Collections.Generic.HashSet<int>();
            foreach (var t in batches[i])
            {
                if (!check.Add(t.x) || !check.Add(t.y) || !check.Add(t.z)) throw new System.InvalidOperationException("Contact face batch shares a vertex.");
                clothTriangles[cursor++] = t;
            }
        }
        initializedSurface = collisionSurface;
    }

    void Solve()
    {
        if (!cloth || !body || !collisionSurface || !restClothMesh || !body.enabled || !body.gameObject.activeInHierarchy
            || !cloth.IsValid() || !cloth.Process.IsRunning() || clearance <= 0) return;
        var teams = MagicaManager.Team;
        var team = teams.teamDataArray[cloth.Process.TeamId];
        if (!team.IsProcess) return;
        using var sample = marker.Auto();
        long began = System.Diagnostics.Stopwatch.GetTimestamp();
        if (initializedSurface != collisionSurface) Initialize();
        for (int i = 0; i < bindposes.Length; i++)
            matrices[i] = bones[i] ? (float4x4)(bones[i].localToWorldMatrix * bindposes[i]) : float4x4.zero;
        for (int i = 0; i < clothBindposes.Length; i++)
            clothMatrices[i] = clothBones[i] ? (float4x4)(clothBones[i].localToWorldMatrix * clothBindposes[i]) : float4x4.zero;
        new SkinJob { rest = rest, weights = skinWeights, matrices = matrices, points = points }.Schedule(points.Length, 64).Complete();
        new RefitJob { points = points, triangles = triangles, order = triangleOrder, nodes = hierarchy }.Schedule().Complete();
        float3 lower = points[0], upper = lower;
        for (int i = 1; i < points.Length; i++) { lower = math.min(lower, points[i]); upper = math.max(upper, points[i]); }
        var surface = new Surface
        {
            points = points, triangles = triangles, lower = lower, upper = upper, clearance = clearance,
            nodes = hierarchy, order = triangleOrder,
            ray = math.normalize(new float3(.932f, .31f, .188f)) * (math.distance(lower, upper) + clearance * 2)
        };
        var proxy = cloth.Process.ProxyMeshContainer.shareVirtualMesh;
        if (!corrections.IsCreated || corrections.Length != proxy.VertexCount)
        {
            if (corrections.IsCreated) corrections.Dispose();
            corrections = new NativeArray<float3>(proxy.VertexCount, Allocator.Persistent);
        }
        var sim = MagicaManager.Simulation;
        new ParticleJob
        {
            surface = surface, start = team.particleChunk.startIndex, attributes = proxy.attributes.GetNativeArray(),
            corrections = corrections, oldPositions = sim.oldPosArray.GetNativeArray(), nextPositions = sim.nextPosArray.GetNativeArray(),
            displayPositions = sim.dispPosArray.GetNativeArray(), velocities = sim.velocityArray.GetNativeArray()
        }.Schedule(proxy.VertexCount, 32).Complete();
        var vm = MagicaManager.VMesh;
        var mappings = teams.teamMappingIndexArray[cloth.Process.TeamId];
        for (int i = 0; i < mappings.Length; i++)
        {
            var mapping = teams.mappingDataArray[mappings[i]];
            var work = MagicaManager.Render.renderDataWorkArray[mapping.renderDataWorkIndex];
            if (!work.UseCustomMesh) continue;
            new RenderJob
            {
                surface = surface, start = mapping.mappingCommonChunk.startIndex,
                renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toWorld = math.inverse(mapping.toMappingMatrix), toLocal = mapping.toMappingMatrix,
                attributes = vm.mappingAttributes.GetNativeArray(), references = vm.mappingReferenceIndices.GetNativeArray(),
                weights = vm.mappingBoneWeights.GetNativeArray(), corrections = corrections,
                rest = clothRest, skinWeights = clothWeights, matrices = clothMatrices,
                world = clothWorld,
                positions = MagicaManager.Render.renderMeshPositions.GetNativeArray()
            }.Schedule(mapping.VertexCount, 64).Complete();
            JobHandle contactHandle = default;
            for (int pass = 0; pass < 2; pass++)
                foreach (var batch in faceBatches)
                    contactHandle = new FaceContactJob { surface = surface, triangles = clothTriangles, world = clothWorld, start = batch.x }
                        .Schedule(batch.y, 16, contactHandle);
            new WriteContactJob
            {
                start = mapping.mappingCommonChunk.startIndex, renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toLocal = mapping.toMappingMatrix, attributes = vm.mappingAttributes.GetNativeArray(),
                references = vm.mappingReferenceIndices.GetNativeArray(), skinWeights = clothWeights, matrices = clothMatrices,
                world = clothWorld, positions = MagicaManager.Render.renderMeshPositions.GetNativeArray()
            }.Schedule(mapping.VertexCount, 64, contactHandle).Complete();
        }
        LastCorrectedParticles = 0; LastMaximumCorrection = 0;
        for (int i = 0; i < corrections.Length; i++)
        {
            float distance = math.length(corrections[i]);
            if (distance > .000001f) LastCorrectedParticles++;
            LastMaximumCorrection = math.max(LastMaximumCorrection, distance);
        }
        LastSolveMilliseconds = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
    }

    [BurstCompile]
    struct SkinJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> rest;
        [ReadOnly] public NativeArray<BoneWeight> weights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [WriteOnly] public NativeArray<float3> points;
        public void Execute(int i)
        {
            var w = weights[i]; var p = rest[i];
            points[i] = math.transform(matrices[w.boneIndex0], p) * w.weight0
                + math.transform(matrices[w.boneIndex1], p) * w.weight1
                + math.transform(matrices[w.boneIndex2], p) * w.weight2
                + math.transform(matrices[w.boneIndex3], p) * w.weight3;
        }
    }

    public struct Surface
    {
        [ReadOnly] public NativeArray<float3> points;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<ContactNode> nodes;
        [ReadOnly] public NativeArray<int> order;
        public float3 lower, upper, ray;
        public float clearance;
        public float3 Correction(float3 p)
        {
            if (!math.all(math.isfinite(p)) || math.any(p < lower - clearance) || math.any(p > upper + clearance)) return 0;
            float best = float.MaxValue; float3 nearest = p, normal = 0; int winding = 0;
            FixedList512Bytes<int> stack = default;
            stack.Add(0);
            while (stack.Length > 0)
            {
                int index = stack[stack.Length - 1]; stack.RemoveAt(stack.Length - 1);
                var node = nodes[index];
                if (BoxDistance(p, node) > best) continue;
                if (node.count == 0)
                {
                    bool leftFirst = BoxDistance(p, nodes[node.left]) < BoxDistance(p, nodes[node.right]);
                    stack.Add(leftFirst ? node.right : node.left); stack.Add(leftFirst ? node.left : node.right);
                    continue;
                }
                for (int j = node.start; j < node.start + node.count; j++)
                {
                    var t = triangles[order[j]]; var a = points[t.x]; var b = points[t.y]; var c = points[t.z];
                    var n = math.cross(b - a, c - a);
                    if (math.lengthsq(n) < 1e-14f) continue;
                    var q = MathUtility.ClosestPtPointTriangle(p, a, b, c, out _);
                    float d = math.distancesq(p, q);
                    if (d < best) { best = d; nearest = q; normal = math.normalizesafe(n); }
                }
            }
            stack.Add(0);
            float3 inverseRay = 1 / ray;
            while (stack.Length > 0)
            {
                int index = stack[stack.Length - 1]; stack.RemoveAt(stack.Length - 1);
                var node = nodes[index];
                float3 aa = (node.lower - p) * inverseRay, bb = (node.upper - p) * inverseRay;
                if (math.max(0, math.cmax(math.min(aa, bb))) > math.min(1, math.cmin(math.max(aa, bb)))) continue;
                if (node.count == 0) { stack.Add(node.left); stack.Add(node.right); continue; }
                for (int j = node.start; j < node.start + node.count; j++)
                {
                    var t = triangles[order[j]]; var a = points[t.x]; var b = points[t.y]; var c = points[t.z];
                    if (MathUtility.IntersectSegmentTriangle(p, p + ray, a, b, c, true, out _, out _, out _, out _))
                        winding += math.dot(math.cross(b - a, c - a), ray) > 0 ? 1 : -1;
                }
            }
            bool inside = winding != 0;
            float distance = math.sqrt(best);
            if (!inside && distance >= clearance) return 0;
            return math.normalizesafe(inside ? nearest - p : p - nearest, normal)
                * (inside ? distance + clearance : clearance - distance);
        }
        static float BoxDistance(float3 p, ContactNode n) => math.lengthsq(math.max(math.max(n.lower - p, p - n.upper), 0));
        public bool Overlaps(float3 minimum, float3 maximum)
        {
            minimum -= clearance; maximum += clearance;
            FixedList512Bytes<int> stack = default; stack.Add(0);
            while (stack.Length > 0)
            {
                var n = nodes[stack[stack.Length - 1]]; stack.RemoveAt(stack.Length - 1);
                if (math.any(n.upper < minimum) || math.any(n.lower > maximum)) continue;
                if (n.count > 0) return true;
                stack.Add(n.left); stack.Add(n.right);
            }
            return false;
        }
    }

    public struct ContactNode
    {
        public float3 lower, upper;
        public int left, right, start, count;
    }

    public static void BuildHierarchy(UnityEngine.Mesh mesh, out ContactNode[] result, out int[] triangleIndices)
    {
        var v = mesh.vertices; var t = mesh.triangles;
        var centers = new Vector3[t.Length / 3];
        var indices = new int[centers.Length];
        for (int i = 0; i < centers.Length; i++) { indices[i] = i; centers[i] = (v[t[i * 3]] + v[t[i * 3 + 1]] + v[t[i * 3 + 2]]) / 3; }
        var nodes = new System.Collections.Generic.List<ContactNode>();
        int Build(int start, int count)
        {
            float3 lower = new float3(float.MaxValue), upper = new float3(float.MinValue);
            for (int i = start; i < start + count; i++)
                for (int j = 0; j < 3; j++) { float3 p = v[t[indices[i] * 3 + j]]; lower = math.min(lower, p); upper = math.max(upper, p); }
            int index = nodes.Count;
            var node = new ContactNode { lower = lower, upper = upper, start = start, count = count };
            nodes.Add(node);
            if (count > 8)
            {
                float3 size = upper - lower; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
                System.Array.Sort(indices, start, count, System.Collections.Generic.Comparer<int>.Create((a, b) => centers[a][axis].CompareTo(centers[b][axis])));
                int half = count / 2; node.count = 0; node.left = Build(start, half); node.right = Build(start + half, count - half);
                nodes[index] = node;
            }
            return index;
        }
        Build(0, indices.Length); result = nodes.ToArray(); triangleIndices = indices;
    }

    [BurstCompile]
    struct RefitJob : IJob
    {
        [ReadOnly] public NativeArray<float3> points;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<int> order;
        public NativeArray<ContactNode> nodes;
        public void Execute()
        {
            for (int i = nodes.Length - 1; i >= 0; i--)
            {
                var n = nodes[i];
                if (n.count == 0) { n.lower = math.min(nodes[n.left].lower, nodes[n.right].lower); n.upper = math.max(nodes[n.left].upper, nodes[n.right].upper); }
                else
                {
                    n.lower = new float3(float.MaxValue); n.upper = new float3(float.MinValue);
                    for (int j = n.start; j < n.start + n.count; j++)
                    {
                        var t = triangles[order[j]];
                        n.lower = math.min(n.lower, math.min(points[t.x], math.min(points[t.y], points[t.z])));
                        n.upper = math.max(n.upper, math.max(points[t.x], math.max(points[t.y], points[t.z])));
                    }
                }
                nodes[i] = n;
            }
        }
    }

    [BurstCompile]
    struct ParticleJob : IJobParallelFor
    {
        public Surface surface;
        public int start;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [WriteOnly] public NativeArray<float3> corrections;
        [NativeDisableParallelForRestriction] public NativeArray<float3> oldPositions, nextPositions, displayPositions, velocities;
        public void Execute(int i)
        {
            corrections[i] = 0;
            if (attributes[i].IsInvalid()) return;
            int p = start + i;
            float3 delta = surface.Correction(oldPositions[p]);
            oldPositions[p] += delta; nextPositions[p] += delta;
            float3 normal = math.normalizesafe(delta);
            velocities[p] -= normal * math.min(0, math.dot(velocities[p], normal));
            delta = surface.Correction(displayPositions[p]);
            displayPositions[p] += delta; corrections[i] = delta;
        }
    }

    [BurstCompile]
    struct RenderJob : IJobParallelFor
    {
        public Surface surface;
        public int start, renderStart;
        public float4x4 toWorld, toLocal;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<VirtualMeshBoneWeight> weights;
        [ReadOnly] public NativeArray<float3> corrections;
        [ReadOnly] public NativeArray<float3> rest;
        [ReadOnly] public NativeArray<BoneWeight> skinWeights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [NativeDisableParallelForRestriction] public NativeArray<float3> world;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        public void Execute(int i)
        {
            int m = start + i;
            if (attributes[m].IsInvalid()) return;
            int reference = references[m];
            int r = renderStart + reference;
            if (attributes[m].IsFixed())
            {
                var skin = skinWeights[reference];
                float4x4 matrix = matrices[skin.boneIndex0] * skin.weight0 + matrices[skin.boneIndex1] * skin.weight1
                    + matrices[skin.boneIndex2] * skin.weight2 + matrices[skin.boneIndex3] * skin.weight3;
                float3 original = rest[reference];
                float3 position = math.transform(matrix, original);
                float3 contact = surface.Correction(position);
                world[reference] = position + contact;
                // Recompute from the original vertex each frame; keep the animated attachment without accumulating offsets.
                positions[r] = math.lengthsq(contact) > 0 && math.abs(math.determinant(matrix)) > 1e-12f
                    ? math.transform(math.inverse(matrix), position + contact) : original;
                return;
            }
            var w = weights[m]; float3 delta = 0;
            for (int k = 0; k < w.Count; k++) delta += corrections[w.boneIndices[k]] * w.weights[k];
            float3 p = math.transform(toWorld, positions[r]) + delta;
            // Mapping interpolation can put a rendered vertex back inside the skin between proxy vertices.
            world[reference] = p + surface.Correction(p);
            positions[r] = math.transform(toLocal, world[reference]);
        }
    }

    [BurstCompile]
    struct FaceContactJob : IJobParallelFor
    {
        public Surface surface;
        [ReadOnly] public NativeArray<int3> triangles;
        public int start;
        // Initialization checks that faces in the same batch share no vertices.
        [NativeDisableParallelForRestriction] public NativeArray<float3> world;
        public void Execute(int i)
        {
            // Vertex clearance alone lets long animated edges cut across a convex thigh.
            var t = triangles[start + i];
            // Vertices are already outside the skin; distant face bounds need no narrow-phase queries.
            var minimum = math.min(world[t.x], math.min(world[t.y], world[t.z]));
            var maximum = math.max(world[t.x], math.max(world[t.y], world[t.z]));
            if (!surface.Overlaps(minimum, maximum)) return;
            Project(t, new float3(1f / 3));
            Project(t, new float3(.5f, .5f, 0));
            Project(t, new float3(0, .5f, .5f));
            Project(t, new float3(.5f, 0, .5f));
        }
        void Project(int3 t, float3 weights)
        {
            var p = world[t.x] * weights.x + world[t.y] * weights.y + world[t.z] * weights.z;
            var delta = surface.Correction(p);
            if (math.lengthsq(delta) < 1e-14f) return;
            delta /= math.lengthsq(weights);
            world[t.x] += delta * weights.x; world[t.y] += delta * weights.y; world[t.z] += delta * weights.z;
        }
    }

    [BurstCompile]
    struct WriteContactJob : IJobParallelFor
    {
        public int start, renderStart;
        public float4x4 toLocal;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<BoneWeight> skinWeights;
        [ReadOnly] public NativeArray<float4x4> matrices;
        [ReadOnly] public NativeArray<float3> world;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        public void Execute(int i)
        {
            int m = start + i, reference = references[m];
            if (attributes[m].IsInvalid()) return;
            var matrix = toLocal;
            if (attributes[m].IsFixed())
            {
                var w = skinWeights[reference];
                var skin = matrices[w.boneIndex0] * w.weight0 + matrices[w.boneIndex1] * w.weight1
                    + matrices[w.boneIndex2] * w.weight2 + matrices[w.boneIndex3] * w.weight3;
                if (math.abs(math.determinant(skin)) < 1e-12f) return;
                matrix = math.inverse(skin);
            }
            positions[renderStart + reference] = math.transform(matrix, world[reference]);
        }
    }
}

}
