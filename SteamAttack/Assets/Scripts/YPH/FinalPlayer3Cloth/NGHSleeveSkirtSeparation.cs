namespace YPH.FinalPlayer3Cloth
{
using MagicaCloth2;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Profiling;
using UnityEngine;

// FullMesh preserves the side of an existing contact, including a sleeve already inside the skirt.
[DisallowMultipleComponent]
public sealed class NGHSleeveSkirtSeparation : MonoBehaviour
{
    public MagicaCloth sleeves;
    public MagicaCloth skirt;
    [Min(0)] public float clearance = 0.018f;
    public MagicaCapsuleCollider leftHandCollider;
    public MagicaCapsuleCollider rightHandCollider;
    public MagicaCapsuleCollider leftForearmCollider;
    public MagicaCapsuleCollider rightForearmCollider;

    NativeArray<float3> skirtPoints;
    NativeArray<float3> displayCorrections;
    NativeArray<float3> physicsCorrections;
    SleeveSeparationGuard proxyGuard, renderGuard, armGuard;
    bool quitting;
    VirtualMesh sleeveProxy;
    Mesh sleeveMesh;
    NativeArray<int3> closedTriangles;
    NativeArray<SkirtBodySurfaceContact.ContactNode> hierarchy;
    NativeArray<int> triangleOrder;
    readonly List<int[]> boundaryLoops = new List<int[]>();
    VirtualMesh sourceMesh;
    static readonly ProfilerMarker separationMarker = new ProfilerMarker("NGH.SleeveSkirtSeparation");

    void OnEnable()
    {
        quitting=false;
        MagicaManager.OnPreSimulation += CheckReset;
        MagicaManager.OnPostSimulation += Separate;
    }
    void OnApplicationQuit() => quitting=true;
    void CheckReset()
    {
        if(quitting || !isActiveAndEnabled || !Application.isPlaying || !MagicaManager.IsPlaying()) return;
        if (!sleeves || !sleeves.IsValid() || !sleeves.Process.IsRunning()) return;
        var team = MagicaManager.Team.teamDataArray[sleeves.Process.TeamId];
        if (!team.IsReset && !team.IsKeepReset) return;
        armGuard?.ResetContacts(); renderGuard?.ResetContacts();
    }
    void OnDisable()
    {
        MagicaManager.OnPostSimulation -= Separate;
        MagicaManager.OnPreSimulation -= CheckReset;
        if (skirtPoints.IsCreated) skirtPoints.Dispose();
        if (displayCorrections.IsCreated) displayCorrections.Dispose();
        if (physicsCorrections.IsCreated) physicsCorrections.Dispose();
        proxyGuard?.Dispose(); proxyGuard = null;
        renderGuard?.Dispose(); renderGuard = null;
        armGuard?.Dispose(); armGuard = null;
        sleeveProxy = null; sleeveMesh = null;
        if (closedTriangles.IsCreated) closedTriangles.Dispose();
        if (hierarchy.IsCreated) hierarchy.Dispose();
        if (triangleOrder.IsCreated) triangleOrder.Dispose();
        sourceMesh = null;
    }

    void Separate()
    {
        if(quitting || !isActiveAndEnabled || !Application.isPlaying || !MagicaManager.IsPlaying()) return;
        if (!sleeves || !skirt || !sleeves.IsValid() || !skirt.IsValid()
            || !sleeves.Process.IsRunning() || !skirt.Process.IsRunning()) return;
        var teams = MagicaManager.Team;
        var st = teams.teamDataArray[sleeves.Process.TeamId];
        var kt = teams.teamDataArray[skirt.Process.TeamId];
        if (!st.IsProcess || !kt.IsProcess) return;
        var attached = sleeves.GetComponent<AttachedSleeveSkinContact>();
        // Finish skin fitting first; its later callback must not undo the arm contacts.
        if (!attached || !attached.ApplySkinContact()) return;
        var mesh = attached ? attached.restMesh : null;
        var mappings = teams.teamMappingIndexArray[sleeves.Process.TeamId];
        if (!mesh || mappings.Length != 1 || !attached.CurrentClothMatrices.IsCreated) return;
        using var marker = separationMarker.Auto();
        var sm = sleeves.Process.ProxyMeshContainer.shareVirtualMesh;
        var km = skirt.Process.ProxyMeshContainer.shareVirtualMesh;
        var sim = MagicaManager.Simulation;
        var vm = MagicaManager.VMesh;
        if (sourceMesh != km) BuildClosedSurface(km);
        if (sleeveProxy != sm)
        {
            if (displayCorrections.IsCreated) displayCorrections.Dispose();
            if (physicsCorrections.IsCreated) physicsCorrections.Dispose();
            proxyGuard?.Dispose();
            armGuard?.Dispose();
            renderGuard?.Dispose(); renderGuard = null; sleeveMesh = null;
            displayCorrections = new NativeArray<float3>(sm.VertexCount, Allocator.Persistent);
            physicsCorrections = new NativeArray<float3>(sm.VertexCount, Allocator.Persistent);
            proxyGuard = new SleeveSeparationGuard(sm.VertexCount, sm.triangles.ToArray(), true);
            armGuard = new SleeveSeparationGuard(sm.VertexCount, sm.triangles.ToArray());
            sleeveProxy = sm;
        }
        NativeArray<float3>.Copy(sim.dispPosArray.GetNativeArray(), kt.particleChunk.startIndex,
            skirtPoints, 0, km.VertexCount);
        for (int i = 0; i < boundaryLoops.Count; i++)
        {
            float3 center = 0;
            foreach (int v in boundaryLoops[i]) center += skirtPoints[v];
            skirtPoints[km.VertexCount + i] = center / boundaryLoops[i].Length;
        }
        if (!hierarchy.IsCreated) BuildHierarchy();
        new SkirtBodySurfaceContact.RefitJob
        {
            points = skirtPoints, triangles = closedTriangles, order = triangleOrder, nodes = hierarchy
        }.Schedule().Complete();
        float3 lower = skirtPoints[0], upper = lower;
        for (int i = 1; i < km.VertexCount; i++)
        {
            lower = math.min(lower, skirtPoints[i]);
            upper = math.max(upper, skirtPoints[i]);
        }
        var surface = new Surface
        {
            leftHand = HandContact(leftHandCollider), rightHand = HandContact(rightHandCollider),
            leftForearm = HandContact(leftForearmCollider), rightForearm = HandContact(rightForearmCollider),
            query = new SkirtBodySurfaceContact.Surface
            {
                points = skirtPoints, triangles = closedTriangles, lower = lower, upper = upper,
                nodes = hierarchy, order = triangleOrder,
                ray = math.normalize(new float3(0.932f, 0.31f, 0.188f)) * (math.distance(lower, upper) + clearance * 2),
                clearance = clearance
            }
        };
        new ParticleJob
        {
            surface = surface, start = st.particleChunk.startIndex,
            attributes = sm.attributes.GetNativeArray(), depths = sm.vertexDepths,
            physics = physicsCorrections, before = proxyGuard.before, after = proxyGuard.after, movable = proxyGuard.movable,
            physicsBefore = proxyGuard.physicsBefore, physicsAfter = proxyGuard.physicsAfter,
            oldPositions = sim.oldPosArray.GetNativeArray(), displayPositions = sim.dispPosArray.GetNativeArray()
        }.Schedule(sm.VertexCount, 32).Complete();
        proxyGuard.Limit(surface.leftForearm, surface.rightForearm, surface.leftHand, surface.rightHand);
        new ApplyParticleJob
        {
            start = st.particleChunk.startIndex, before = proxyGuard.before, after = proxyGuard.after,
            factors = proxyGuard.factors, physics = physicsCorrections, corrections = displayCorrections,
            oldPositions = sim.oldPosArray.GetNativeArray(),
            nextPositions = sim.nextPosArray.GetNativeArray(), displayPositions = sim.dispPosArray.GetNativeArray(),
            velocities = sim.velocityArray.GetNativeArray()
        }.Schedule(sm.VertexCount, 32).Complete();

        new PrepareArmRecoveryJob
        {
            start = st.particleChunk.startIndex, attributes = sm.attributes.GetNativeArray(),
            positions = sim.oldPosArray.GetNativeArray(), animated = sim.basePosArray.GetNativeArray(),
            before = armGuard.before, after = armGuard.after, reference = armGuard.reference,
            movable = armGuard.movable, factors = armGuard.factors
        }.Schedule(sm.VertexCount, 32).Complete();
        armGuard.ResolveArmFaces(surface.leftForearm, surface.rightForearm, surface.leftHand, surface.rightHand, surface.query, .008f, true);
        new ApplyArmRecoveryJob
        {
            start = st.particleChunk.startIndex, before = armGuard.before, after = armGuard.after, factors = armGuard.factors,
            oldPositions = sim.oldPosArray.GetNativeArray(), nextPositions = sim.nextPosArray.GetNativeArray(),
            displayPositions = sim.dispPosArray.GetNativeArray(), velocityPositions = sim.velocityPosArray.GetNativeArray(),
            velocities = sim.velocityArray.GetNativeArray(), realVelocities = sim.realVelocityArray.GetNativeArray(), corrections = displayCorrections
        }.Schedule(sm.VertexCount, 32).Complete();

        if (sleeveMesh != mesh)
        {
            renderGuard?.Dispose(); renderGuard = null;
            sleeveMesh = mesh;
            if (mesh) renderGuard = new SleeveSeparationGuard(mesh);
        }

        // Reuse proxy weights so skirt separation does not independently flatten the fabric layers.
        // shortcut: this preserves the mapped thickness; independently folding lining still needs a valid shared cloth surface.
        for (int i = 0; i < mappings.Length; i++)
        {
            var mapping = teams.mappingDataArray[mappings[i]];
            var work = MagicaManager.Render.renderDataWorkArray[mapping.renderDataWorkIndex];
            if (!work.UseCustomMesh) continue;
            // This character's final mesh topology comes from its existing skin-contact binding.
            new RenderJob
            {
                start = mapping.mappingCommonChunk.startIndex,
                renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toWorld = math.inverse(mapping.toMappingMatrix), toLocal = mapping.toMappingMatrix,
                attributes = vm.mappingAttributes.GetNativeArray(),
                references = vm.mappingReferenceIndices.GetNativeArray(),
                weights = vm.mappingBoneWeights.GetNativeArray(), corrections = displayCorrections,
                leftHand = surface.leftHand, rightHand = surface.rightHand,
                leftForearm = surface.leftForearm, rightForearm = surface.rightForearm,
                before = renderGuard.before, after = renderGuard.after, movable = renderGuard.movable,
                reference = renderGuard.reference, rest = attached.CurrentClothRest,
                skinWeights = attached.CurrentClothWeights, skinMatrices = attached.CurrentClothMatrices,
                positions = MagicaManager.Render.renderMeshPositions.GetNativeArray()
            }.Schedule(mapping.VertexCount, 64).Complete();
            renderGuard.Limit(surface.leftForearm, surface.rightForearm, surface.leftHand, surface.rightHand);
            renderGuard.ResolveArmFaces(surface.leftForearm, surface.rightForearm, surface.leftHand, surface.rightHand, surface.query);
            new WriteRenderJob
            {
                start = mapping.mappingCommonChunk.startIndex, renderStart = work.renderMeshPositionAndNormalChunk.startIndex,
                toLocal = mapping.toMappingMatrix, references = vm.mappingReferenceIndices.GetNativeArray(),
                movable = renderGuard.movable, before = renderGuard.before, after = renderGuard.after, factors = renderGuard.factors,
                positions = MagicaManager.Render.renderMeshPositions.GetNativeArray()
            }.Schedule(mapping.VertexCount, 64).Complete();
        }
    }

    void BuildClosedSurface(VirtualMesh mesh)
    {
        if (hierarchy.IsCreated) hierarchy.Dispose();
        if (triangleOrder.IsCreated) triangleOrder.Dispose();
        if (skirtPoints.IsCreated) skirtPoints.Dispose();
        if (closedTriangles.IsCreated) closedTriangles.Dispose();
        boundaryLoops.Clear();
        var triangles = new List<int3>(mesh.triangles.ToArray());
        var edges = new Dictionary<ulong, int2>();
        foreach (var t in triangles)
        {
            AddEdge(t.x, t.y); AddEdge(t.y, t.z); AddEdge(t.z, t.x);
        }
        void AddEdge(int a, int b)
        {
            ulong key = ((ulong)(uint)math.min(a, b) << 32) | (uint)math.max(a, b);
            if (!edges.Remove(key)) edges.Add(key, new int2(a, b));
        }
        var next = new Dictionary<int, int>();
        foreach (var edge in edges.Values) next.Add(edge.x, edge.y);
        while (next.Count > 0)
        {
            int first = 0;
            foreach (int v in next.Keys) { first = v; break; }
            int current = first;
            var loop = new List<int>();
            do
            {
                loop.Add(current);
                int end = next[current];
                next.Remove(current);
                triangles.Add(new int3(end, current, mesh.VertexCount + boundaryLoops.Count));
                current = end;
            } while (current != first);
            boundaryLoops.Add(loop.ToArray());
        }
        // Collision-only caps close the waist and hem; the visible skirt mesh is untouched.
        skirtPoints = new NativeArray<float3>(mesh.VertexCount + boundaryLoops.Count, Allocator.Persistent);
        closedTriangles = new NativeArray<int3>(triangles.ToArray(), Allocator.Persistent);
        sourceMesh = mesh;
    }

    void BuildHierarchy()
    {
        var mesh = new Mesh();
        try
        {
            var vertices = new Vector3[skirtPoints.Length];
            var indices = new int[closedTriangles.Length * 3];
            for (int i = 0; i < vertices.Length; i++) vertices[i] = skirtPoints[i];
            for (int i = 0; i < closedTriangles.Length; i++)
            {
                var t = closedTriangles[i]; indices[i * 3] = t.x; indices[i * 3 + 1] = t.y; indices[i * 3 + 2] = t.z;
            }
            mesh.vertices = vertices; mesh.triangles = indices;
            SkirtBodySurfaceContact.BuildHierarchy(mesh, out var nodes, out var order);
            hierarchy = new NativeArray<SkirtBodySurfaceContact.ContactNode>(nodes, Allocator.Persistent);
            triangleOrder = new NativeArray<int>(order, Allocator.Persistent);
        }
        finally { Destroy(mesh); }
    }

    // shortcut: palm capsules approximate the fingers; finger-level contact needs articulated finger colliders.
    static HandCapsule HandContact(MagicaCapsuleCollider collider)
    {
        if (!collider || !collider.enabled || !collider.gameObject.activeInHierarchy) return default;
        var size = collider.GetSize() * collider.GetScale();
        var axis = collider.transform.TransformDirection(collider.GetLocalDir()).normalized;
        var center = collider.transform.TransformPoint(collider.center);
        float startLength = collider.alignedOnCenter ? size.z * .5f - size.x : -size.x;
        float endLength = collider.alignedOnCenter ? size.z * .5f - size.y : size.z - size.x - size.y;
        return new HandCapsule
        {
            start = center + axis * Mathf.Max(0, startLength), end = center - axis * Mathf.Max(0, endLength),
            startRadius = size.x + .003f, endRadius = size.y + .003f, rotation = collider.transform.rotation
        };
    }

    public struct HandCapsule
    {
        public float3 start, end;
        public float startRadius, endRadius;
        public quaternion rotation;

        public float3 Correction(float3 point)
        {
            if (startRadius <= 0 || endRadius <= 0 || !math.all(math.isfinite(point))) return 0;
            var axis = end - start;
            float lengthSq = math.lengthsq(axis);
            float t = lengthSq > 1e-12f ? math.saturate(math.dot(point - start, axis) / lengthSq) : 0;
            var offset = point - math.lerp(start, end, t);
            float distance = math.length(offset), radius = math.lerp(startRadius, endRadius, t);
            if (distance >= radius) return 0;
            var fallback = math.cross(axis, math.up());
            fallback = math.normalizesafe(fallback, math.right());
            return math.normalizesafe(offset, fallback) * (radius - distance);
        }

        public float3 Slide(float3 origin, float3 target)
        {
            float radius = math.max(startRadius, endRadius);
            if (radius <= 0 || math.distancesq(origin, target) < 1e-12f) return target;
            MathUtility.ClosestPtSegmentSegment(origin, target, start, end, out _, out _, out var path, out var axis);
            if (math.distancesq(path, axis) >= radius * radius) return target;
            float t = math.saturate(math.dot(origin - start, end - start) / math.max(1e-12f, math.lengthsq(end - start)));
            var center = math.lerp(start, end, t);
            var normal = math.normalizesafe(origin - center, math.normalizesafe(math.cross(end - start, math.up()), math.right()));
            // Preserve the incoming side: projecting only the destination can teleport cloth through the arm.
            return target + normal * math.max(0, math.lerp(startRadius, endRadius, t) - math.dot(target - center, normal));
        }


    }

    public static float3 ProjectArmContact(float3 point, HandCapsule leftForearm, HandCapsule rightForearm,
        HandCapsule leftHand, HandCapsule rightHand)
    {
        // Recheck overlapping wrist capsules without feeding render-only offsets back into the simulation.
        for (int pass = 0; pass < 4; pass++)
        {
            var previous = point;
            point += leftForearm.Correction(point);
            point += rightForearm.Correction(point);
            point += leftHand.Correction(point);
            point += rightHand.Correction(point);
            if (math.distancesq(previous, point) < 1e-12f) break;
        }
        return point;
    }

    public static float3 SlideArmContact(float3 origin, float3 target, HandCapsule leftForearm, HandCapsule rightForearm,
        HandCapsule leftHand, HandCapsule rightHand)
    {
        for (int pass = 0; pass < 4; pass++)
        {
            var previous = target;
            target = leftForearm.Slide(origin, target);
            target = rightForearm.Slide(origin, target);
            target = leftHand.Slide(origin, target);
            target = rightHand.Slide(origin, target);
            if (math.distancesq(previous, target) < 1e-12f) break;
        }
        return target;
    }

    public static float FaceDepth(float3 a, float3 b, float3 c, HandCapsule cap)
    {
        return FaceContact(a, b, c, cap, out _, out _);
    }

    internal static float FaceContact(float3 a, float3 b, float3 c, HandCapsule cap, out float3 normal, out float3 barycentric)
    {
        normal = barycentric = 0;
        float radius = math.max(cap.startRadius, cap.endRadius);
        if (radius <= 0 || math.any(math.max(math.max(a, b), c) < math.min(cap.start, cap.end) - radius)
            || math.any(math.min(math.min(a, b), c) > math.max(cap.start, cap.end) + radius)) return 0;
        float3 point = MathUtility.ClosestPtPointTriangle(cap.start, a, b, c, out _), axis = cap.start;
        float best = math.distancesq(point, axis);
        var end = MathUtility.ClosestPtPointTriangle(cap.end, a, b, c, out _);
        if (math.distancesq(end, cap.end) < best) { point = end; axis = cap.end; best = math.distancesq(point, axis); }
        if (MathUtility.IntersectSegmentTriangle(cap.start, cap.end, a, b, c, true, out _, out _, out _, out float t))
        { point = axis = math.lerp(cap.start, cap.end, t); best = 0; }
        ClosestEdge(a, b, cap, ref best, ref point, ref axis);
        ClosestEdge(b, c, cap, ref best, ref point, ref axis);
        ClosestEdge(c, a, cap, ref best, ref point, ref axis);
        float axisLength = math.lengthsq(cap.end - cap.start);
        float ratio = axisLength > 1e-12f ? math.saturate(math.dot(axis - cap.start, cap.end - cap.start) / axisLength) : 0;
        radius = math.lerp(cap.startRadius, cap.endRadius, ratio);
        if (best >= radius * radius || !math.isfinite(best)) return 0;
        MathUtility.ClosestPtPointTriangle(point, a, b, c, out barycentric);
        normal = math.normalizesafe(point - axis);
        return radius - math.sqrt(best);
    }

    static void ClosestEdge(float3 a, float3 b, HandCapsule cap, ref float best, ref float3 point, ref float3 axis)
    {
        MathUtility.ClosestPtSegmentSegment(a, b, cap.start, cap.end, out _, out _, out var p, out var q);
        float d = math.distancesq(p, q);
        if (d < best) { best = d; point = p; axis = q; }
    }

#if UNITY_EDITOR
    public static void ValidateSeparationGuard() => SleeveSeparationGuard.Validate();
#endif

    struct Surface
    {
        public SkirtBodySurfaceContact.Surface query;
        public HandCapsule leftHand, rightHand, leftForearm, rightForearm;
        public float3 Correction(float3 p, float depth)
        {
            if (depth <= 0.02f) return 0;
            // Keep the sewn attachment fixed, blending only the short transition into the free fabric.
            var target = p;
            for (int pass = 0; pass < 3; pass++)
            {
                var previous = target;
                var delta = query.Correction(target) * math.smoothstep(0.02f, 0.08f, depth);
                if (math.lengthsq(delta) < 1e-12f) break;
                target += delta;
                target = SlideArmContact(p, target, leftForearm, rightForearm, leftHand, rightHand);
                if (math.distancesq(previous, target) < 1e-12f) break;
            }
            return target - p;
        }
    }

    [BurstCompile]
    struct ParticleJob : IJobParallelFor
    {
        public Surface surface;
        public int start;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<float> depths;
        [ReadOnly] public NativeArray<float3> oldPositions, displayPositions;
        public NativeArray<float3> physics, before, after, physicsBefore, physicsAfter;
        public NativeArray<byte> movable;
        public void Execute(int i)
        {
            int p = start + i;
            movable[i] = (byte)(attributes[i].IsMove() ? 1 : 0);
            before[i] = displayPositions[p]; after[i] = before[i]; physics[i] = 0;
            physicsBefore[i] = oldPositions[p]; physicsAfter[i] = oldPositions[p];
            if (movable[i] == 0) return;
            physics[i] = surface.Correction(oldPositions[p], depths[i]);
            physicsAfter[i] += physics[i];
            after[i] += surface.Correction(before[i], depths[i]);
        }
    }

    [BurstCompile]
    struct ApplyParticleJob : IJobParallelFor
    {
        public int start;
        [ReadOnly] public NativeArray<float3> physics, before, after;
        [ReadOnly] public NativeArray<float> factors;
        public NativeArray<float3> corrections;
        [NativeDisableParallelForRestriction] public NativeArray<float3> oldPositions, nextPositions, displayPositions, velocities;
        public void Execute(int i)
        {
            int p = start + i;
            var delta = factors[i] > 0 && math.all(math.isfinite(physics[i])) ? physics[i] * factors[i] : 0;
            oldPositions[p] += delta; nextPositions[p] += delta;
            corrections[i] = factors[i] > 0 ? (after[i] - before[i]) * factors[i] : 0;
            displayPositions[p] += corrections[i];
            var normal = math.normalizesafe(delta);
            velocities[p] -= normal * math.min(0, math.dot(velocities[p], normal));
        }
    }

    [BurstCompile]
    struct PrepareArmRecoveryJob : IJobParallelFor
    {
        public int start;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<float3> positions, animated;
        public NativeArray<float3> before, after, reference;
        public NativeArray<byte> movable;
        public NativeArray<float> factors;
        public void Execute(int i)
        {
            before[i] = after[i] = positions[start+i]; reference[i] = animated[start+i];
            movable[i] = (byte)(attributes[i].IsMove() ? 1 : 0); factors[i] = movable[i];
        }
    }

    [BurstCompile]
    struct ApplyArmRecoveryJob : IJobParallelFor
    {
        public int start;
        [ReadOnly] public NativeArray<float3> before, after;
        [ReadOnly] public NativeArray<float> factors;
        public NativeArray<float3> corrections;
        [NativeDisableParallelForRestriction] public NativeArray<float3> oldPositions, nextPositions, displayPositions, velocityPositions, velocities, realVelocities;
        public void Execute(int i)
        {
            var delta = factors[i] > 0 ? (after[i]-before[i])*factors[i] : 0;
            if (!math.all(math.isfinite(delta))) return;
            int p=start+i;
            oldPositions[p]+=delta; nextPositions[p]+=delta; displayPositions[p]+=delta; velocityPositions[p]+=delta;
            corrections[i]+=delta;
            var normal=math.normalizesafe(delta);
            velocities[p]-=normal*math.min(0,math.dot(velocities[p],normal));
            realVelocities[p]-=normal*math.min(0,math.dot(realVelocities[p],normal));
        }
    }

    [BurstCompile]
    struct RenderJob : IJobParallelFor
    {
        public int start, renderStart;
        public float4x4 toWorld, toLocal;
        public HandCapsule leftHand, rightHand;
        public HandCapsule leftForearm, rightForearm;
        [ReadOnly] public NativeArray<VertexAttribute> attributes;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<VirtualMeshBoneWeight> weights;
        [ReadOnly] public NativeArray<BoneWeight> skinWeights;
        [ReadOnly] public NativeArray<float4x4> skinMatrices;
        [ReadOnly] public NativeArray<float3> corrections;
        [ReadOnly] public NativeArray<float3> positions;
        [ReadOnly] public NativeArray<float3> rest;
        [NativeDisableParallelForRestriction] public NativeArray<float3> reference;
        [NativeDisableParallelForRestriction] public NativeArray<float3> before, after;
        [NativeDisableParallelForRestriction] public NativeArray<byte> movable;
        public void Execute(int i)
        {
            int m = start + i;
            int reference = references[m];
            movable[reference] = (byte)(attributes[m].IsMove() ? 1 : 0);
            var sw = skinWeights[reference];
            var animatedSkin = skinMatrices[sw.boneIndex0]*sw.weight0 + skinMatrices[sw.boneIndex1]*sw.weight1
                + skinMatrices[sw.boneIndex2]*sw.weight2 + skinMatrices[sw.boneIndex3]*sw.weight3;
            this.reference[reference] = math.transform(animatedSkin, rest[reference]);
            var skin = toWorld;
            if (movable[reference] == 0)
            {
                var w = skinWeights[reference];
                skin = skinMatrices[w.boneIndex0] * w.weight0 + skinMatrices[w.boneIndex1] * w.weight1
                    + skinMatrices[w.boneIndex2] * w.weight2 + skinMatrices[w.boneIndex3] * w.weight3;
            }
            before[reference] = math.transform(skin, positions[renderStart + reference]);
            if (movable[reference] != 0) before[reference] = ProjectArmContact(before[reference], leftForearm, rightForearm, leftHand, rightHand);
            after[reference] = before[reference];
            if (!attributes[m].IsMove()) return;
            var bw = weights[m];
            float3 delta = 0;
            for (int k = 0; k < bw.Count; k++) delta += corrections[bw.boneIndices[k]] * bw.weights[k];
            int r = renderStart + references[m];
            float3 p = before[reference];
            p = SlideArmContact(p, p + delta, leftForearm, rightForearm, leftHand, rightHand);
            // Proxy interpolation can put the visible cloth back inside an otherwise resolved forearm contact.
            p = ProjectArmContact(p, leftForearm, rightForearm, leftHand, rightHand);
            after[reference] = p;
        }
    }

    [BurstCompile]
    struct WriteRenderJob : IJobParallelFor
    {
        public int start, renderStart;
        public float4x4 toLocal;
        [ReadOnly] public NativeArray<int> references;
        [ReadOnly] public NativeArray<byte> movable;
        [ReadOnly] public NativeArray<float3> before, after;
        [ReadOnly] public NativeArray<float> factors;
        [NativeDisableParallelForRestriction] public NativeArray<float3> positions;
        public void Execute(int i)
        {
            int r = references[start + i];
            if (movable[r] == 0) return;
            var point = factors[r] > 0 ? math.lerp(before[r], after[r], factors[r]) : before[r];
            positions[renderStart + r] = math.transform(toLocal, point);
        }
    }
}

}
