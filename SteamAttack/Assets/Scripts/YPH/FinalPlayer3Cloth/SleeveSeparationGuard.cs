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

// Limits an extra displacement relative to this frame's native cloth result, never to a stored world pose.
internal sealed class SleeveSeparationGuard : IDisposable
{
    public NativeArray<float3> before, after;
    public NativeArray<float3> reference;
    public NativeArray<float3> physicsBefore, physicsAfter;
    public NativeArray<byte> movable;
    public NativeArray<float> factors;
    NativeArray<int3> triangles;
    NativeArray<int> representatives, candidates;
    NativeArray<byte> capsuleMasks;
    NativeArray<float4> baseline, physicsBaseline;
    NativeArray<float3> contactSides;
    NativeArray<byte> trappedFrames;

    public SleeveSeparationGuard(int count, int3[] topology, bool includePhysics = false)
    {
        before = new NativeArray<float3>(count, Allocator.Persistent);
        after = new NativeArray<float3>(count, Allocator.Persistent);
        reference = new NativeArray<float3>(count, Allocator.Persistent);
        contactSides = new NativeArray<float3>(topology.Length * 4, Allocator.Persistent);
        trappedFrames = new NativeArray<byte>(topology.Length * 4, Allocator.Persistent);
        movable = new NativeArray<byte>(count, Allocator.Persistent);
        factors = new NativeArray<float>(count, Allocator.Persistent);
        triangles = new NativeArray<int3>(topology, Allocator.Persistent);
        baseline = new NativeArray<float4>(topology.Length, Allocator.Persistent);
        candidates = new NativeArray<int>(topology.Length, Allocator.Persistent);
        capsuleMasks = new NativeArray<byte>(topology.Length, Allocator.Persistent);
        physicsBaseline = new NativeArray<float4>(includePhysics ? topology.Length : 0, Allocator.Persistent);
        physicsBefore = new NativeArray<float3>(includePhysics ? count : 0, Allocator.Persistent);
        physicsAfter = new NativeArray<float3>(includePhysics ? count : 0, Allocator.Persistent);
        representatives = new NativeArray<int>(count, Allocator.Persistent);
        for (int i = 0; i < count; i++) representatives[i] = i;
    }

    public SleeveSeparationGuard(Mesh mesh) : this(mesh.vertexCount, Topology(mesh))
    {
        var vertices = mesh.vertices; var weights = mesh.boneWeights;
        var seams = new Dictionary<(Vector3, BoneWeight), int>();
        for (int i = 0; i < vertices.Length; i++)
        {
            var key = (vertices[i], weights[i]);
            if (seams.TryGetValue(key, out int first)) representatives[i] = first;
            else seams.Add(key, i);
        }
    }

    static int3[] Topology(Mesh mesh)
    {
        var indices = mesh.triangles; var result = new int3[indices.Length / 3];
        for (int i = 0; i < result.Length; i++) result[i] = new int3(indices[i * 3], indices[i * 3 + 1], indices[i * 3 + 2]);
        return result;
    }

    public void Limit(NGHSleeveSkirtSeparation.HandCapsule leftArm, NGHSleeveSkirtSeparation.HandCapsule rightArm,
        NGHSleeveSkirtSeparation.HandCapsule leftHand, NGHSleeveSkirtSeparation.HandCapsule rightHand, bool recoverPhysics = false)
    {
        new GuardJob { before = before, after = after, movable = movable, factors = factors,
            triangles = triangles, representatives = representatives, baseline = baseline,
            physicsBefore = physicsBefore, physicsAfter = physicsAfter, physicsBaseline = physicsBaseline,
            candidates = candidates, capsuleMasks = capsuleMasks,
            leftArm = leftArm, rightArm = rightArm, leftHand = leftHand, rightHand = rightHand, recoverPhysics = recoverPhysics }.Schedule().Complete();
    }

    public void Dispose()
    {
        before.Dispose(); after.Dispose(); movable.Dispose(); factors.Dispose();
        reference.Dispose(); contactSides.Dispose(); trappedFrames.Dispose();
        triangles.Dispose(); representatives.Dispose(); baseline.Dispose();
        physicsBefore.Dispose(); physicsAfter.Dispose(); physicsBaseline.Dispose();
        candidates.Dispose(); capsuleMasks.Dispose();
    }

    public void ResetContacts()
    {
        for (int i = 0; i < contactSides.Length; i++) { contactSides[i] = 0; trappedFrames[i] = 0; }
    }

    public void ResolveArmFaces(NGHSleeveSkirtSeparation.HandCapsule leftArm, NGHSleeveSkirtSeparation.HandCapsule rightArm,
        NGHSleeveSkirtSeparation.HandCapsule leftHand, NGHSleeveSkirtSeparation.HandCapsule rightHand, SkirtBodySurfaceContact.Surface skirt, float maximumDisplacement = .008f, bool recoverPhysics = false)
    {
        new FaceContactJob { before=before, after=after, factors=factors, movable=movable, triangles=triangles,
            representatives=representatives, candidates=candidates, skirt=skirt, reference=reference, contactSides=contactSides, trappedFrames=trappedFrames,
            leftArm=leftArm, rightArm=rightArm, leftHand=leftHand, rightHand=rightHand, maximumDisplacement=maximumDisplacement, recoverPhysics=recoverPhysics }.Schedule().Complete();
        Limit(leftArm,rightArm,leftHand,rightHand,recoverPhysics);
    }

    [BurstCompile]
    struct FaceContactJob : IJob
    {
        public NativeArray<float3> before, after;
        [ReadOnly] public NativeArray<float> factors;
        [ReadOnly] public NativeArray<byte> movable;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<int> representatives;
        public NativeArray<int> candidates;
        [ReadOnly] public NativeArray<float3> reference;
        public NativeArray<float3> contactSides;
        public NativeArray<byte> trappedFrames;
        float contactMaximum;
        public SkirtBodySurfaceContact.Surface skirt;
        public NGHSleeveSkirtSeparation.HandCapsule leftArm,rightArm,leftHand,rightHand;
        public float maximumDisplacement;
        public bool recoverPhysics;
        int Representative(int i)=>movable[i]==movable[representatives[i]]?representatives[i]:i;
        float3 Point(int i){int r=Representative(i);return before[i]+after[r]-before[r];}
        public void Execute()
        {
            for(int i=0;i<before.Length;i++) { if(factors[i]>0)before[i]=math.lerp(before[i],after[i],factors[i]);after[i]=before[i]; }
            int count=0;
            for(int i=0;i<triangles.Length;i++)
            {
                var t=triangles[i];if(movable[t.x]+movable[t.y]+movable[t.z]==0)continue;
                var a=before[t.x];var b=before[t.y];var c=before[t.z];
                RememberSide(i,t,leftArm,0);RememberSide(i,t,rightArm,1);RememberSide(i,t,leftHand,2);RememberSide(i,t,rightHand,3);
                if(NGHSleeveSkirtSeparation.FaceDepth(a,b,c,leftArm)>.0001f||NGHSleeveSkirtSeparation.FaceDepth(a,b,c,rightArm)>.0001f
                    ||NGHSleeveSkirtSeparation.FaceDepth(a,b,c,leftHand)>.0001f||NGHSleeveSkirtSeparation.FaceDepth(a,b,c,rightHand)>.0001f)candidates[count++]=i;
            }
            for(int pass=0;pass<4;pass++)
                for(int i=0;i<count;i++)
                {
                    var t=triangles[candidates[i]];
                    int face=candidates[i];
                    Solve(face,t,leftArm,0);Solve(face,t,rightArm,1);Solve(face,t,leftHand,2);Solve(face,t,rightHand,3);
                }
            for(int i=0;i<after.Length;i++)
            {
                if(Representative(i)!=i||math.distancesq(after[i],before[i])<1e-12f)continue;
                float original=math.lengthsq(skirt.Correction(before[i]));
                if(math.lengthsq(skirt.Correction(after[i]))>original+1e-10f
                    ||math.lengthsq(skirt.Correction((after[i]+before[i])*.5f))>original+1e-10f)after[i]=before[i];
            }
            for(int i=after.Length-1;i>=0;i--)after[i]=Point(i);
        }
        static quaternion Rotation(NGHSleeveSkirtSeparation.HandCapsule cap) => math.lengthsq(cap.rotation.value) > .5f ? cap.rotation : quaternion.identity;
        static float3 Radial(float3 point, NGHSleeveSkirtSeparation.HandCapsule cap)
        {
            var axis=cap.end-cap.start;
            return point-math.lerp(cap.start,cap.end,math.saturate(math.dot(point-cap.start,axis)/math.max(1e-12f,math.lengthsq(axis))));
        }
        void RememberSide(int face,int3 t,NGHSleeveSkirtSeparation.HandCapsule cap,int slot)
        {
            if(cap.startRadius<=0)return;
            int index=face*4+slot;
            float depth=NGHSleeveSkirtSeparation.FaceDepth(before[t.x],before[t.y],before[t.z],cap);
            // Increase recovery only for a threaded face whose corners are already outside the arm.
            bool threaded=depth>math.max(cap.startRadius,cap.endRadius)*.8f
                && math.lengthsq(cap.Correction(before[t.x]))<1e-10f
                && math.lengthsq(cap.Correction(before[t.y]))<1e-10f
                && math.lengthsq(cap.Correction(before[t.z]))<1e-10f;
            trappedFrames[index]=(byte)(threaded?math.min(32,trappedFrames[index]+1):0);
            var radial=Radial((before[t.x]+before[t.y]+before[t.z])/3,cap);
            if(depth<=.0001f&&math.lengthsq(radial)>1e-10f)
                contactSides[index]=math.mul(math.inverse(Rotation(cap)),math.normalize(radial));
            else if(math.lengthsq(contactSides[index])<.5f)
            {
                var authored=Radial((reference[t.x]+reference[t.y]+reference[t.z])/3,cap);
                var faceNormal=math.cross(reference[t.y]-reference[t.x],reference[t.z]-reference[t.x]);
                var normal=math.normalizesafe(authored,math.normalizesafe(faceNormal));
                contactSides[index]=math.mul(math.inverse(Rotation(cap)),normal);
            }
        }
        void Solve(int face,int3 t,NGHSleeveSkirtSeparation.HandCapsule cap,int slot)
        {
            contactMaximum=recoverPhysics?math.min(maximumDisplacement,.002f+math.max(0,trappedFrames[face*4+slot]-8)*.00025f):maximumDisplacement;
            float depth=NGHSleeveSkirtSeparation.FaceContact(Point(t.x),Point(t.y),Point(t.z),cap,out var normal,out var barycentric);
            // A capsule axis crossing the face has no closest-point normal; retain the incoming side in bone space.
            var remembered=math.mul(Rotation(cap),contactSides[face*4+slot]);
            if(depth>.0001f && (math.lengthsq(normal)<.5f || (recoverPhysics && math.lengthsq(remembered)>.5f && math.dot(normal,remembered)<.25f)))
            {
                normal=remembered;
                var p=Point(t.x)*barycentric.x+Point(t.y)*barycentric.y+Point(t.z)*barycentric.z;
                var axis=cap.end-cap.start;float ratio=math.saturate(math.dot(p-cap.start,axis)/math.max(1e-12f,math.lengthsq(axis)));
                depth=math.lerp(cap.startRadius,cap.endRadius,ratio)-math.dot(p-math.lerp(cap.start,cap.end,ratio),normal);
            }
            if(depth<=.0001f||math.lengthsq(normal)<.5f)return;
            var mass=new float3(movable[t.x],movable[t.y],movable[t.z]);
            float denominator=math.dot(barycentric*barycentric,mass);
            // Do not amplify a contact beside an immovable seam.
            if(denominator<.25f)return;
            var weights=barycentric*mass/denominator;
            float scale=math.min(depth+.0001f,.002f/math.max(.001f,math.cmax(weights)));
            Move(t.x,normal*(scale*weights.x));Move(t.y,normal*(scale*weights.y));Move(t.z,normal*(scale*weights.z));
        }
        void Move(int i,float3 delta)
        {
            i=Representative(i);
            var total=after[i]+delta-before[i];
            var target=before[i]+total*math.min(1,contactMaximum/math.max(.000001f,math.length(total)));
            after[i]=target;
        }
    }

    [BurstCompile]
    struct GuardJob : IJob
    {
        [ReadOnly] public NativeArray<float3> before, after;
        [ReadOnly] public NativeArray<float3> physicsBefore, physicsAfter;
        [ReadOnly] public NativeArray<byte> movable;
        [ReadOnly] public NativeArray<int3> triangles;
        [ReadOnly] public NativeArray<int> representatives;
        public NativeArray<float> factors;
        public NativeArray<float4> baseline, physicsBaseline;
        public NativeArray<int> candidates;
        public NativeArray<byte> capsuleMasks;
        public NGHSleeveSkirtSeparation.HandCapsule leftArm, rightArm, leftHand, rightHand;
        public bool recoverPhysics;

        int Representative(int i) => movable[i] == movable[representatives[i]] ? representatives[i] : i;
        float3 Point(int i, NativeArray<float3> from, NativeArray<float3> to, float time = 1) => factors[Representative(i)] > 0
            ? math.lerp(from[i], to[i], factors[Representative(i)] * time) : from[i];
        bool Active(int3 t) => movable[t.x] + movable[t.y] + movable[t.z] > 0;
        bool Changed(int3 t, NativeArray<float3> from, NativeArray<float3> to) =>
            math.distancesq(from[t.x],to[t.x]) + math.distancesq(from[t.y],to[t.y]) + math.distancesq(from[t.z],to[t.z]) > 1e-14f;
        float4 Depth(float3 a, float3 b, float3 c, byte mask) => new float4(
            (mask&1)!=0 ? NGHSleeveSkirtSeparation.FaceDepth(a,b,c,leftArm) : 0,
            (mask&2)!=0 ? NGHSleeveSkirtSeparation.FaceDepth(a,b,c,rightArm) : 0,
            (mask&4)!=0 ? NGHSleeveSkirtSeparation.FaceDepth(a,b,c,leftHand) : 0,
            (mask&8)!=0 ? NGHSleeveSkirtSeparation.FaceDepth(a,b,c,rightHand) : 0);
        byte Mask(int3 t, NativeArray<float3> from, NativeArray<float3> to)
        {
            var lo=math.min(math.min(from[t.x],from[t.y]),math.min(from[t.z],math.min(to[t.x],math.min(to[t.y],to[t.z]))));
            var hi=math.max(math.max(from[t.x],from[t.y]),math.max(from[t.z],math.max(to[t.x],math.max(to[t.y],to[t.z]))));
            return (byte)((Overlaps(lo,hi,leftArm)?1:0)|(Overlaps(lo,hi,rightArm)?2:0)|(Overlaps(lo,hi,leftHand)?4:0)|(Overlaps(lo,hi,rightHand)?8:0));
        }
        static bool Overlaps(float3 lo,float3 hi,NGHSleeveSkirtSeparation.HandCapsule cap)
        {
            float radius=math.max(cap.startRadius,cap.endRadius);
            return radius>0 && math.all(hi>=math.min(cap.start,cap.end)-radius) && math.all(lo<=math.max(cap.start,cap.end)+radius);
        }

        public void Execute()
        {
            bool invalid = false;
            for (int i = 0; i < factors.Length; i++)
            {
                factors[i] = movable[i] == 0 ? 0 : 1;
                invalid |= !math.all(math.isfinite(before[i])) || !math.all(math.isfinite(after[i]));
                if (physicsBefore.Length > 0) invalid |= !math.all(math.isfinite(physicsBefore[i])) || !math.all(math.isfinite(physicsAfter[i]));
            }
            if (invalid)
            {
                for (int i = 0; i < factors.Length; i++) factors[i] = 0;
                return;
            }
            int count=0;
            for (int i = 0; i < triangles.Length; i++)
            {
                var t = triangles[i];
                bool changed = Changed(t,before,after) || (physicsBefore.Length>0 && Changed(t,physicsBefore,physicsAfter));
                if(!Active(t)||!changed) continue;
                candidates[count++]=i;
                byte mask=Mask(t,before,after);
                if(physicsBefore.Length>0) mask|=Mask(t,physicsBefore,physicsAfter);
                capsuleMasks[i]=mask;
                baseline[i] = Depth(before[t.x], before[t.y], before[t.z],mask);
                if(physicsBefore.Length>0) physicsBaseline[i] = Depth(physicsBefore[t.x],physicsBefore[t.y],physicsBefore[t.z],mask);
            }
            bool valid = false;
            for (int pass = 0; pass < 8; pass++)
            {
                valid = true;
                for (int index = 0; index < count; index++)
                {
                    int i=candidates[index]; byte mask=capsuleMasks[i];
                    var t = triangles[i];
                    if (!Worse(t, baseline[i],before,after,mask) && (physicsBefore.Length==0 || !Worse(t,physicsBaseline[i],physicsBefore,physicsAfter,mask))) continue;
                    valid = false;
                    factors[Representative(t.x)] *= .5f;
                    factors[Representative(t.y)] *= .5f;
                    factors[Representative(t.z)] *= .5f;
                }
                if (valid) break;
            }
            // Cancel only the conflicting neighborhood so an elbow contact cannot freeze recovery of the other sleeve.
            for (int pass = 0; !valid && pass < 16; pass++)
            {
                valid = true;
                for (int index = 0; index < count; index++)
                {
                    int i=candidates[index]; var t=triangles[i]; byte mask=capsuleMasks[i];
                    if (!Worse(t,baseline[i],before,after,mask) && (physicsBefore.Length==0 || !Worse(t,physicsBaseline[i],physicsBefore,physicsAfter,mask))) continue;
                    valid=false; factors[Representative(t.x)]=0; factors[Representative(t.y)]=0; factors[Representative(t.z)]=0;
                }
            }
            // shortcut: an unresolved connected conflict retains the native pose; it does not certify that pose as collision-free.
            if (!valid) for (int i=0;i<factors.Length;i++) factors[i]=0;
            else for (int i=0;i<factors.Length;i++) factors[i]=factors[Representative(i)];
        }

        bool Worse(int3 t, float4 original, NativeArray<float3> from, NativeArray<float3> to, byte mask)
        {
            var a = Point(t.x,from,to); var b = Point(t.y,from,to); var c = Point(t.z,from,to);
            if (math.distancesq(a,from[t.x]) + math.distancesq(b,from[t.y]) + math.distancesq(c,from[t.z]) < 1e-14f) return false;
            if (EdgeChanged(t.x,t.y,a,b,from) || EdgeChanged(t.y,t.z,b,c,from) || EdgeChanged(t.z,t.x,c,a,from)) return true;
            var oldNormal = math.cross(from[t.y]-from[t.x],from[t.z]-from[t.x]);
            var newNormal = math.cross(b-a,c-a);
            if (math.dot(oldNormal,newNormal) < .9f * math.sqrt(math.lengthsq(oldNormal)*math.lengthsq(newNormal))) return true;
            if(mask==0) return false;
            var limit=original+.00001f;
            // Returning to the incoming side may cross the capsule center; only already trapped contacts may do so.
            if(recoverPhysics)limit=math.select(limit,new float4(float.MaxValue),original>.001f);
            if (math.any(Depth(a,b,c,mask) > limit)) return true;
            return math.any(Depth(Point(t.x,from,to,.5f),Point(t.y,from,to,.5f),Point(t.z,from,to,.5f),mask) > limit);
        }
        bool EdgeChanged(int x, int y, float3 a, float3 b, NativeArray<float3> from)
        {
            float length = math.distance(from[x],from[y]);
            return math.abs(math.distance(a,b)-length) > math.max(.001f,length*.1f);
        }
    }

#if UNITY_EDITOR
    internal static void Validate()
    {
        using var guard = new SleeveSeparationGuard(3, new[] { new int3(0,1,2) });
        var cap = new NGHSleeveSkirtSeparation.HandCapsule { start = new float3(0,-.2f,0), end = new float3(0,.2f,0), startRadius=.05f, endRadius=.05f };
        using var skirtPoints = new NativeArray<float3>(1,Allocator.TempJob);
        using var skirtTriangles = new NativeArray<int3>(1,Allocator.TempJob);
        using var skirtNodes = new NativeArray<SkirtBodySurfaceContact.ContactNode>(1,Allocator.TempJob);
        using var skirtOrder = new NativeArray<int>(1,Allocator.TempJob);
        var skirt = new SkirtBodySurfaceContact.Surface { points=skirtPoints,triangles=skirtTriangles,nodes=skirtNodes,order=skirtOrder,lower=new float3(-10),upper=new float3(-10) };
        guard.before.CopyFrom(new[]{new float3(.08f,-.1f,-.08f),new float3(.08f,.1f,-.08f),new float3(.08f,0,.08f)});
        guard.after.CopyFrom(new[]{new float3(float.NaN),new float3(float.NaN),new float3(float.NaN)});
        guard.movable.CopyFrom(new byte[]{1,1,1});guard.factors.CopyFrom(new[]{0f,0f,0f});
        guard.ResolveArmFaces(cap,default,default,default,skirt);
        for(int i=0;i<3;i++)if(!math.all(math.isfinite(guard.after[i]))||math.distance(guard.before[i],guard.after[i])>1e-6f)
            throw new InvalidOperationException("Rejected nonfinite displacement leaked into face contact.");
        guard.before.CopyFrom(new[]{new float3(.04f,-.1f,-.08f),new float3(.04f,.1f,-.08f),new float3(.04f,0,.08f)});
        guard.after.CopyFrom(guard.before);guard.movable.CopyFrom(new byte[]{1,1,1});guard.factors.CopyFrom(new[]{1f,1f,1f});
        guard.ResolveArmFaces(cap,default,default,default,skirt);
        float3 p0=math.lerp(guard.before[0],guard.after[0],guard.factors[0]),p1=math.lerp(guard.before[1],guard.after[1],guard.factors[1]),p2=math.lerp(guard.before[2],guard.after[2],guard.factors[2]);
        if(NGHSleeveSkirtSeparation.FaceDepth(p0,p1,p2,cap)>.009f)throw new InvalidOperationException("Local face contact did not reduce face-only penetration.");
        guard.before.CopyFrom(new[] { new float3(.06f,-.1f,-.08f), new float3(.06f,.1f,-.08f), new float3(.06f,0,.08f) });
        guard.movable.CopyFrom(new byte[] { 1,1,1 });
        for (int i=0;i<3;i++) guard.after[i]=guard.before[i]-new float3(.02f,0,0);
        guard.Limit(cap,default,default,default);
        var a=math.lerp(guard.before[0],guard.after[0],guard.factors[0]);
        var b=math.lerp(guard.before[1],guard.after[1],guard.factors[1]);
        var c=math.lerp(guard.before[2],guard.after[2],guard.factors[2]);
        if(NGHSleeveSkirtSeparation.FaceDepth(a,b,c,cap)>.000011f) throw new InvalidOperationException("Face guard introduced contact.");
        for(int i=0;i<3;i++) guard.after[i]=guard.before[i]+new float3(.02f,0,0);
        guard.Limit(cap,default,default,default);
        for(int i=0;i<3;i++) if(guard.factors[i]!=1) throw new InvalidOperationException("Safe motion limited.");
        guard.movable[0]=0;
        guard.Limit(cap,default,default,default);
        if(guard.factors[0]!=0) throw new InvalidOperationException("Fixed vertex changed.");
        guard.after[1]=new float3(float.NaN);
        guard.Limit(cap,default,default,default);
        if(guard.factors[1]!=0) throw new InvalidOperationException("Nonfinite correction accepted.");

        using var recovery = new SleeveSeparationGuard(3,new[]{new int3(0,1,2)});
        var authored=new[]{new float3(.06f,-.1f,-.08f),new float3(.06f,.1f,-.08f),new float3(.06f,0,.08f)};
        recovery.reference.CopyFrom(authored);recovery.before.CopyFrom(authored);recovery.after.CopyFrom(authored);
        recovery.movable.CopyFrom(new byte[]{1,1,1});recovery.factors.CopyFrom(new[]{1f,1f,1f});
        recovery.ResolveArmFaces(cap,default,default,default,skirt);
        for(int i=0;i<3;i++)recovery.before[i]=authored[i]-new float3(.06f,0,0);
        for(int pass=0;pass<32;pass++)
        {
            recovery.after.CopyFrom(recovery.before);recovery.factors.CopyFrom(new[]{1f,1f,1f});
            recovery.ResolveArmFaces(cap,default,default,default,skirt);
            for(int i=0;i<3;i++)recovery.before[i]=math.lerp(recovery.before[i],recovery.after[i],recovery.factors[i]);
        }
        if(NGHSleeveSkirtSeparation.FaceDepth(recovery.before[0],recovery.before[1],recovery.before[2],cap)>.001f)
            throw new InvalidOperationException("Axis-crossed face did not recover across physical steps.");
        if(recovery.before[0].x<0||recovery.before[1].x<0||recovery.before[2].x<0)
            throw new InvalidOperationException("Recovery lost the incoming side.");
        // The physical path must escape on the remembered side after the arm turns.
        for(int turn=0;turn<2;turn++)
        {
            recovery.ResetContacts();
            cap.rotation=quaternion.RotateY(turn*math.PI*.5f);
            for(int i=0;i<3;i++)recovery.reference[i]=recovery.before[i]=recovery.after[i]=math.mul(cap.rotation,authored[i]);
            recovery.factors.CopyFrom(new[]{1f,1f,1f});
            recovery.ResolveArmFaces(cap,default,default,default,skirt,.008f,true);
            for(int i=0;i<3;i++)recovery.before[i]=math.mul(cap.rotation,authored[i]-new float3(.065f,0,0));
            for(int step=0;step<64;step++)
            {
                recovery.after.CopyFrom(recovery.before);recovery.factors.CopyFrom(new[]{1f,1f,1f});
                recovery.ResolveArmFaces(cap,default,default,default,skirt,.008f,true);
                for(int i=0;i<3;i++)
                {
                    var next=math.lerp(recovery.before[i],recovery.after[i],recovery.factors[i]);
                    if(math.distance(next,recovery.before[i])>.00801f)throw new InvalidOperationException("Recovery exceeded its contact budget.");
                    recovery.before[i]=next;
                }
            }
            if(NGHSleeveSkirtSeparation.FaceDepth(recovery.before[0],recovery.before[1],recovery.before[2],cap)>.001f)
                throw new InvalidOperationException("Rotated physical contact failed to escape.");
            for(int i=0;i<3;i++)if(math.mul(math.inverse(cap.rotation),recovery.before[i]).x<0)
                throw new InvalidOperationException("Physical contact escaped on the wrong side.");
            recovery.movable[0]=0;var fixedPoint=recovery.before[0];
            recovery.after.CopyFrom(recovery.before);recovery.factors.CopyFrom(new[]{1f,1f,1f});
            recovery.ResolveArmFaces(cap,default,default,default,skirt,.008f,true);
            if(recovery.factors[0]!=0||math.distance(recovery.before[0],fixedPoint)>0)
                throw new InvalidOperationException("Physical recovery moved a fixed vertex.");
            recovery.movable[0]=1;
        }
        recovery.ResetContacts();
        for(int i=0;i<recovery.contactSides.Length;i++)if(math.lengthsq(recovery.contactSides[i])>0)
            throw new InvalidOperationException("Reset retained contact history.");
    }
#endif
}

}
