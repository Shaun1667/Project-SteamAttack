using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace YPH
{
    /// <summary>
    /// Blender의 머리 보강 데이터를 Unity Mesh로 옮기는 편집기 전용 도구다.
    /// 원본 삼각형의 뼈 가중치를 보간해 기존 캐릭터의 리깅을 그대로 사용한다.
    /// </summary>
    public static class HairGroomAssetBuilder
    {
        private struct GroomVertex
        {
            public Vector3 Position;
            public Vector3 Normal;
            public Vector2 UV;
            public int Face;
            public float WeightA;
            public float WeightB;
        }

        /// <summary>
        /// 원본의 갓과 장식은 보존하고 머리카락 삼각형만 보강본으로 교체한다.
        /// 반환된 임시 Mesh는 호출자가 저장하거나 제거한다.
        /// </summary>
        public static Mesh BuildBase(Mesh source, byte[] bytes)
        {
            return Build(source, bytes, true);
        }

        /// <summary>
        /// 원본과 같은 Bind Pose를 가진 별도의 Hair Cards Mesh를 만든다.
        /// 반환된 임시 Mesh는 호출자가 저장하거나 제거한다.
        /// </summary>
        public static Mesh BuildCards(Mesh source, byte[] bytes)
        {
            Mesh mesh = Build(source, bytes, false);
            mesh.RecalculateTangents();
            return mesh;
        }

        /// <summary>
        /// 바이너리의 범위를 검사하고 위치·UV·법선·리깅 데이터를 함께 변환한다.
        /// 원본 FBX와 원본 Mesh 객체에는 값을 쓰지 않는다.
        /// </summary>
        private static Mesh Build(Mesh source, byte[] bytes, bool retainOriginal)
        {
            if (source == null || source.subMeshCount < 2 || source.blendShapeCount != 0)
            {
                throw new ArgumentException("Blend Shape가 없는 원본 머리 Mesh가 필요합니다.");
            }

            using var reader = new BinaryReader(new MemoryStream(bytes, false));
            int count = reader.ReadInt32();
            int indexCount = reader.ReadInt32();
            if (count < 1 || indexCount < 3 || indexCount % 3 != 0 || bytes.Length != 8L + count * 44L + indexCount * 4L)
            {
                throw new InvalidDataException("머리 보강 데이터의 길이가 올바르지 않습니다.");
            }

            int[] sourceTriangles = source.GetTriangles(1);
            var data = new GroomVertex[count];
            for (int i = 0; i < count; i++)
            {
                data[i] = new GroomVertex
                {
                    Position = ReadVector(reader),
                    Normal = ReadVector(reader),
                    UV = new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                    Face = reader.ReadInt32(),
                    WeightA = reader.ReadSingle(),
                    WeightB = reader.ReadSingle()
                };
                GroomVertex vertex = data[i];
                if (vertex.Face < 0 || vertex.Face >= sourceTriangles.Length / 3
                    || !float.IsFinite(vertex.Position.sqrMagnitude) || !float.IsFinite(vertex.Normal.sqrMagnitude)
                    || !float.IsFinite(vertex.UV.sqrMagnitude) || !float.IsFinite(vertex.WeightA)
                    || !float.IsFinite(vertex.WeightB) || vertex.WeightA < -.002f || vertex.WeightB < -.002f
                    || vertex.WeightA + vertex.WeightB > 1.002f || vertex.Normal.sqrMagnitude < .5f)
                {
                    throw new InvalidDataException("머리 보강 데이터에 잘못된 정점이 있습니다.");
                }
            }

            int offset = retainOriginal ? source.vertexCount : 0;
            int total = offset + count;
            var positions = new Vector3[total];
            var normals = new Vector3[total];
            var tangents = new Vector4[total];
            var uv = new Vector2[total];
            var weights = new BoneWeight[total];
            BoneWeight[] sourceWeights = source.boneWeights;
            Vector4[] sourceTangents = source.tangents;
            if (sourceWeights.Length != source.vertexCount || sourceTangents.Length != source.vertexCount)
            {
                throw new InvalidDataException("원본 Mesh의 뼈 가중치 또는 Tangent가 없습니다.");
            }
            if (retainOriginal)
            {
                Array.Copy(source.vertices, positions, offset);
                Array.Copy(source.normals, normals, offset);
                Array.Copy(sourceTangents, tangents, offset);
                Array.Copy(source.uv, uv, offset);
                Array.Copy(sourceWeights, weights, offset);
            }

            for (int i = 0; i < count; i++)
            {
                GroomVertex vertex = data[i];
                int face = vertex.Face * 3;
                int a = sourceTriangles[face];
                int b = sourceTriangles[face + 1];
                int c = sourceTriangles[face + 2];
                float wa = Mathf.Clamp01(vertex.WeightA);
                float wb = Mathf.Clamp(vertex.WeightB, 0f, 1f - wa);
                float wc = 1f - wa - wb;
                int target = offset + i;
                positions[target] = vertex.Position;
                normals[target] = vertex.Normal.normalized;
                uv[target] = vertex.UV;
                Vector4 tangent = sourceTangents[a] * wa + sourceTangents[b] * wb + sourceTangents[c] * wc;
                Vector3 direction = Vector3.ProjectOnPlane(new Vector3(tangent.x, tangent.y, tangent.z), normals[target]).normalized;
                tangents[target] = new Vector4(direction.x, direction.y, direction.z, tangent.w < 0f ? -1f : 1f);
                weights[target] = InterpolateWeights(sourceWeights[a], sourceWeights[b], sourceWeights[c], wa, wb, wc);
            }

            int[] triangles = new int[indexCount];
            for (int i = 0; i < indexCount; i++)
            {
                int index = reader.ReadInt32();
                if (index < 0 || index >= count)
                {
                    throw new InvalidDataException("머리 보강 삼각형이 정점 범위를 벗어났습니다.");
                }
                triangles[i] = index + offset;
            }

            // 필요한 속성만 명시적으로 저장해 가져오기 전용 데이터에 의존하지 않는다.
            Mesh mesh = new Mesh();
            mesh.name = retainOriginal ? "GatHatHairRefined" : "GatHatHairCards";
            mesh.indexFormat = total > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.vertices = positions;
            mesh.normals = normals;
            mesh.tangents = tangents;
            mesh.uv = uv;
            mesh.boneWeights = weights;
            mesh.bindposes = source.bindposes;
            if (retainOriginal)
            {
                mesh.subMeshCount = source.subMeshCount;
                for (int i = 0; i < source.subMeshCount; i++)
                {
                    if (i != 1)
                    {
                        mesh.SetTriangles(source.GetTriangles(i), i, false);
                    }
                }
            }
            mesh.SetTriangles(triangles, retainOriginal ? 1 : 0, false);
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>연속된 세 실수를 읽어 위치 또는 법선 벡터로 묶는다.</summary>
        private static Vector3 ReadVector(BinaryReader reader)
        {
            return new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        }

        /// <summary>
        /// 원본 삼각형에서 영향이 큰 뼈 네 개를 골라 가중치 합을 1로 맞춘다.
        /// 같은 뼈의 기여도를 먼저 합해 세분화 경계가 갈라지는 것을 막는다.
        /// </summary>
        private static BoneWeight InterpolateWeights(BoneWeight a, BoneWeight b, BoneWeight c, float wa, float wb, float wc)
        {
            var merged = new Dictionary<int, float>();
            AddWeights(merged, a, wa);
            AddWeights(merged, b, wb);
            AddWeights(merged, c, wc);
            KeyValuePair<int, float>[] top = merged.OrderByDescending(pair => pair.Value).Take(4).ToArray();
            float sum = top.Sum(pair => pair.Value);
            var indices = new int[4];
            var values = new float[4];
            for (int i = 0; i < top.Length; i++)
            {
                indices[i] = top[i].Key;
                values[i] = top[i].Value / sum;
            }
            return new BoneWeight
            {
                boneIndex0 = indices[0], boneIndex1 = indices[1], boneIndex2 = indices[2], boneIndex3 = indices[3],
                weight0 = values[0], weight1 = values[1], weight2 = values[2], weight3 = values[3]
            };
        }

        /// <summary>각 뼈의 기여도를 더해 겹치는 뼈 번호를 하나로 모은다.</summary>
        private static void AddWeights(Dictionary<int, float> merged, BoneWeight weight, float factor)
        {
            int[] indices = { weight.boneIndex0, weight.boneIndex1, weight.boneIndex2, weight.boneIndex3 };
            float[] values = { weight.weight0, weight.weight1, weight.weight2, weight.weight3 };
            for (int i = 0; i < 4; i++)
            {
                if (values[i] * factor <= 0f)
                {
                    continue;
                }
                merged.TryGetValue(indices[i], out float previous);
                merged[indices[i]] = previous + values[i] * factor;
            }
        }
    }
}
