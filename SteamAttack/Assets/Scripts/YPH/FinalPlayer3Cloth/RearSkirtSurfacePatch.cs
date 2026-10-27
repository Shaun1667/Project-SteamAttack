namespace YPH.FinalPlayer3Cloth
{
using System;
using System.Collections.Generic;
using MagicaCloth2;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

// Refines only the visible rear seam; MagicaCloth retains the original simulation mesh.
internal sealed class RearSkirtSurfacePatch : IDisposable
{
    readonly SkinnedMeshRenderer source;
    readonly Mesh rest;
    readonly Transform root;
    readonly Mesh patch;
    readonly GameObject view;
    readonly int[][] originalIndices, retainedIndices;
    readonly NativeArray<int3> references;
    readonly NativeArray<float3> barycentrics;
    readonly NativeArray<byte> boundary;
    readonly NativeArray<float3> positions, normals;
    Mesh modifiedMesh;

    public RearSkirtSurfacePatch(SkinnedMeshRenderer source, Mesh rest, Transform root, float[] mask, NativeArray<int> representatives)
    {
        this.source = source; this.rest = rest; this.root = root;
        var vertices = rest.vertices; var uv = rest.uv;
        var local = new Vector3[vertices.Length];
        for (int i = 0; i < local.Length; i++) local[i] = root.InverseTransformPoint(source.transform.TransformPoint(vertices[i]));
        var faces = new List<(int3 vertices, int material)>();
        var edgeCounts = new Dictionary<(int, int), int>();
        (int, int) Edge(int a, int b) { a = representatives[a]; b = representatives[b]; return a < b ? (a, b) : (b, a); }
        bool Seam(int3 t) => (local[t.x].z + local[t.y].z + local[t.z].z) / 3 < -.02f
            && math.cmin(new float3(mask[t.x], mask[t.y], mask[t.z])) < .999f
            && math.cmax(new float3(mask[t.x], mask[t.y], mask[t.z])) > 0;
        var seamEdges = new HashSet<(int, int)>();
        var all = rest.triangles;
        for (int k = 0; k < all.Length; k += 3)
        {
            var t = new int3(all[k], all[k + 1], all[k + 2]); if (!Seam(t)) continue;
            for (int j = 0; j < 3; j++) seamEdges.Add(Edge(t[j], t[(j + 1) % 3]));
        }
        originalIndices = new int[rest.subMeshCount][]; retainedIndices = new int[rest.subMeshCount][];
        for (int s = 0; s < rest.subMeshCount; s++)
        {
            originalIndices[s] = rest.GetTriangles(s); var retained = new List<int>();
            var indices = originalIndices[s];
            for (int k = 0; k < indices.Length; k += 3)
            {
                var t = new int3(indices[k], indices[k + 1], indices[k + 2]);
                // Keep the join under the vest, rather than at its visible lower edge.
                bool selected = Seam(t) || (math.cmin(new float3(mask[t.x], mask[t.y], mask[t.z])) >= .999f
                    && (seamEdges.Contains(Edge(t.x, t.y)) || seamEdges.Contains(Edge(t.y, t.z)) || seamEdges.Contains(Edge(t.z, t.x))));
                if (!selected) { retained.Add(t.x); retained.Add(t.y); retained.Add(t.z); continue; }
                faces.Add((t, s));
                for (int j = 0; j < 3; j++) { var e = Edge(t[j], t[(j + 1) % 3]); edgeCounts.TryGetValue(e, out int count); edgeCounts[e] = count + 1; }
            }
            retainedIndices[s] = retained.ToArray();
        }
        if (faces.Count == 0) throw new InvalidOperationException("No rear skirt/vest boundary faces.");
        var refs = new List<int3>(); var bary = new List<float3>(); var locked = new List<byte>(); var uvs = new List<Vector2>();
        var submeshes = new List<int>[rest.subMeshCount]; for (int s = 0; s < submeshes.Length; s++) submeshes[s] = new List<int>();
        foreach (var face in faces)
        {
            var t = face.vertices;
            // Equal subdivisions keep shared edges watertight as the surface bends.
            const int divisions = 12;
            var map = new int[divisions + 1, divisions + 1];
            bool ab = edgeCounts[Edge(t.x, t.y)] == 1, ac = edgeCounts[Edge(t.x, t.z)] == 1, bc = edgeCounts[Edge(t.y, t.z)] == 1;
            for (int x = 0; x <= divisions; x++) for (int y = 0; y <= divisions - x; y++)
            {
                var w = new float3(1 - (x + y) / (float)divisions, x / (float)divisions, y / (float)divisions);
                map[x, y] = refs.Count; refs.Add(t); bary.Add(w);
                locked.Add((byte)((y == 0 && ab) || (x == 0 && ac) || (x + y == divisions && bc) ? 1 : 0));
                uvs.Add(uv[t.x] * w.x + uv[t.y] * w.y + uv[t.z] * w.z);
            }
            var output = submeshes[face.material];
            for (int x = 0; x < divisions; x++) for (int y = 0; y < divisions - x; y++)
            {
                output.Add(map[x, y]); output.Add(map[x + 1, y]); output.Add(map[x, y + 1]);
                if (x + y < divisions - 1) { output.Add(map[x + 1, y]); output.Add(map[x + 1, y + 1]); output.Add(map[x, y + 1]); }
            }
        }
        references = new NativeArray<int3>(refs.ToArray(), Allocator.Persistent);
        barycentrics = new NativeArray<float3>(bary.ToArray(), Allocator.Persistent);
        boundary = new NativeArray<byte>(locked.ToArray(), Allocator.Persistent);
        positions = new NativeArray<float3>(refs.Count, Allocator.Persistent); normals = new NativeArray<float3>(refs.Count, Allocator.Persistent);
        patch = new Mesh { name = "Rear skirt contact surface (runtime)", hideFlags = HideFlags.DontSave };
        patch.MarkDynamic(); patch.SetVertices(positions); patch.SetUVs(0, uvs); patch.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++) patch.SetTriangles(submeshes[s], s);
        view = new GameObject("RearSkirtContactSurface") { hideFlags = HideFlags.DontSave, layer = source.gameObject.layer };
        view.transform.SetParent(root, false); view.AddComponent<MeshFilter>().sharedMesh = patch;
        var renderer = view.AddComponent<MeshRenderer>(); renderer.sharedMaterials = source.sharedMaterials;
        renderer.shadowCastingMode = source.shadowCastingMode; renderer.receiveShadows = source.receiveShadows;
        renderer.renderingLayerMask = source.renderingLayerMask; renderer.lightProbeUsage = source.lightProbeUsage;
    }

    public void Update(NativeArray<float3> world, NativeArray<float3> worldNormals, SkirtBodySurfaceContact.Surface skin,
        NativeArray<float3> vest, NativeArray<int3> vestTriangles, NativeArray<SkirtBodySurfaceContact.ContactNode> nodes, NativeArray<int> order)
    {
        // Keep the existing skin clearance across the newly sampled seam faces.
        new ContactJob { source = world, sourceNormals = worldNormals, references = references, bary = barycentrics, boundary = boundary,
            skin = skin, vest = vest, triangles = vestTriangles, nodes = nodes, order = order,
            toLocal = root.worldToLocalMatrix, normalToLocal = math.transpose((float3x3)(float4x4)root.localToWorldMatrix), positions = positions, normals = normals
        }.Schedule(positions.Length, 32).Complete();
        patch.SetVertices(positions); patch.SetNormals(normals); patch.RecalculateBounds(); patch.RecalculateTangents();
        if (modifiedMesh != source.sharedMesh)
        {
            Restore();
            if (source.sharedMesh == rest || source.sharedMesh.vertexCount != rest.vertexCount)
                throw new InvalidOperationException("Rear contact requires MagicaCloth's runtime mesh.");
            modifiedMesh = source.sharedMesh;
            for (int s = 0; s < retainedIndices.Length; s++) modifiedMesh.SetTriangles(retainedIndices[s], s, false);
        }
    }
    void Restore()
    {
        if (modifiedMesh) for (int s = 0; s < originalIndices.Length; s++) modifiedMesh.SetTriangles(originalIndices[s], s, false);
        modifiedMesh = null;
    }
#if UNITY_EDITOR
    internal void ValidateBoundary(NativeArray<float3> world)
    {
        for (int i = 0; i < positions.Length; i++)
        {
            if (!math.all(math.isfinite(positions[i])) || !math.all(math.isfinite(normals[i]))) throw new InvalidOperationException("Nonfinite rear surface.");
            if (boundary[i] == 0) continue;
            var t = references[i]; var w = barycentrics[i];
            var expected = world[t.x] * w.x + world[t.y] * w.y + world[t.z] * w.z;
            // Unity와 Burst의 좌표 변환 오차를 고려해 월드 거리 0.01mm까지 허용합니다.
            if (math.distance(root.TransformPoint(positions[i]), expected) > 1e-5f) throw new InvalidOperationException("Rear surface boundary moved.");
        }
        for (int s = 0; s < originalIndices.Length; s++)
            if (modifiedMesh.GetIndexCount(s) != retainedIndices[s].Length) throw new InvalidOperationException("Unexpected source index changes.");
    }
#endif
    public void Dispose()
    {
        Restore();
        if (view) UnityEngine.Object.Destroy(view); if (patch) UnityEngine.Object.Destroy(patch);
        if (references.IsCreated) references.Dispose(); if (barycentrics.IsCreated) barycentrics.Dispose();
        if (boundary.IsCreated) boundary.Dispose(); if (positions.IsCreated) positions.Dispose(); if (normals.IsCreated) normals.Dispose();
    }

    [BurstCompile] struct ContactJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<float3> source, sourceNormals, bary, vest;
        [ReadOnly] public NativeArray<int3> references, triangles;
        [ReadOnly] public NativeArray<byte> boundary;
        [ReadOnly] public NativeArray<SkirtBodySurfaceContact.ContactNode> nodes;
        [ReadOnly] public NativeArray<int> order;
        public SkirtBodySurfaceContact.Surface skin;
        public float4x4 toLocal; public float3x3 normalToLocal;
        public NativeArray<float3> positions, normals;
        public void Execute(int i)
        {
            var t = references[i]; var w = bary[i];
            var p = source[t.x] * w.x + source[t.y] * w.y + source[t.z] * w.z;
            if (boundary[i] == 0)
            {
                var query = skin; query.clearance = .04f;
                var outward = math.normalizesafe(query.Correction(p));
                if (FindVest(p, outward, out var target)) p = target;
                else
                {
                    p += skin.Correction(p);
                    if (FindVest(p, outward, out target)) p = target;
                }
            }
            positions[i] = math.transform(toLocal, p);
            normals[i] = math.normalizesafe(math.mul(normalToLocal, sourceNormals[t.x] * w.x + sourceNormals[t.y] * w.y + sourceNormals[t.z] * w.z));
        }
        bool FindVest(float3 p, float3 direction, out float3 target)
        {
            target = p; if (math.lengthsq(direction) < .5f) return false;
            var start = p - direction * .06f; var end = p + direction * .06f; var inverse = 1 / (end - start);
            float best = .06f; bool found = false; FixedList512Bytes<int> stack = default; stack.Add(0);
            while (stack.Length > 0)
            {
                var node = nodes[stack[stack.Length - 1]]; stack.RemoveAt(stack.Length - 1);
                var aa = (node.lower - start) * inverse; var bb = (node.upper - start) * inverse;
                if (math.max(0, math.cmax(math.min(aa, bb))) > math.min(1, math.cmin(math.max(aa, bb)))) continue;
                if (node.count == 0) { stack.Add(node.left); stack.Add(node.right); continue; }
                for (int j = node.start; j < node.start + node.count; j++)
                {
                    var t = triangles[order[j]]; var a = vest[t.x]; var b = vest[t.y]; var c = vest[t.z];
                    var normal = math.normalizesafe(math.cross(b - a, c - a));
                    if (math.dot(normal, direction) < .3f || !MathUtility.IntersectSegmentTriangle(start, end, a, b, c, true, out _, out _, out _, out float along)) continue;
                    float distance = math.abs(along * .12f - .06f); if (distance >= best) continue;
                    best = distance; target = math.lerp(start, end, along) - normal * .010f; found = true;
                }
            }
            return found;
        }
    }
}

}
