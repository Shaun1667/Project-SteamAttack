#ifndef YPH_HERO_HAIR_SURFACE_INCLUDED
#define YPH_HERO_HAIR_SURFACE_INCLUDED

TEXTURE2D(_HairBakeMap);
SAMPLER(sampler_HairBakeMap);
TEXTURE2D(_HairFlowMap);
SAMPLER(sampler_HairFlowMap);
TEXTURE2D(_HairCoordinateMap);
SAMPLER(sampler_HairCoordinateMap);
TEXTURE2D(_HairDetailMap);
SAMPLER(sampler_HairDetailMap);

// 옆머리의 UV 조각 사이에서도 이어지는 곡선 좌표다. 좌표는 float로 복원한다.
// 모든 픽셀에서 미리 샘플링해 부분 마스크 경계에서도 Mipmap 미분을 유지한다.
void HairReadSurface(float2 uv, out float4 groom, out float4 flow)
{
    groom = SAMPLE_TEXTURE2D(_HairBakeMap, sampler_HairBakeMap, uv);
    flow = SAMPLE_TEXTURE2D(_HairFlowMap, sampler_HairFlowMap, uv);
    float4 packed = SAMPLE_TEXTURE2D(_HairCoordinateMap, sampler_HairCoordinateMap, uv);
    const float2 Decode = float2(65280.0, 255.0) / 65535.0;
    float2 fiberUV = float2(dot(packed.rg, Decode), dot(packed.ba, Decode));
    float4 fiber = SAMPLE_TEXTURE2D(_HairDetailMap, sampler_HairDetailMap, fiberUV);
    half blend = saturate(flow.a) * saturate(_DetailAlbedoMapScale);
    groom.b = lerp(groom.b, 0.10 + 0.30 * fiber.b, blend);
    groom.a = lerp(groom.a, saturate(0.25 + 0.70 * fiber.b), blend);
}

// 데이터 텍스처로 읽은 시안의 명암을 색 공간으로 복원한다.
// 어두운 바탕 위에 조명을 더하므로 밝은 결을 Emission으로 고정하지 않는다.
half3 HairAlbedo(half luminance)
{
    const half AlbedoScale = 0.45;
    return _BaseColor.rgb * pow(max(luminance, 0.001), 2.2) * AlbedoScale;
}

// Color 속성은 Linear 프로젝트에서 자동 변환된다. 미술용 강도 조절 값은
// Inspector의 RGB 0.2가 1배가 되도록 복원해 색 공간에 따른 밝기 차이를 막는다.
half3 HairSpecularTint()
{
    half3 tint = _SpecColor.rgb;
#if !defined(UNITY_COLORSPACE_GAMMA)
    tint = LinearToSRGB(tint);
#endif
    return tint * 5.0;
}

// Forward와 DepthNormals가 같은 결을 사용해 SSAO의 표면 방향도 일치시킨다.
half3 HairNormalTS(float2 packedNormal)
{
    float2 xy = (packedNormal * 2.0 - 1.0) * _BumpScale;
    return SafeNormalize(float3(xy, sqrt(saturate(1.0 - dot(xy, xy)))));
}

// 앞뒤가 같은 머리결을 2배 각도로 저장한다. Mipmap에서 반대 방향이
// 서로 지워지는 현상을 줄여 멀리서도 광택 띠의 방향을 유지한다.
float2 HairDirectionTS(float2 packedDirection)
{
    float2 doubled = packedDirection * 2.0 - 1.0;
    doubled *= rsqrt(max(dot(doubled, doubled), 0.0001));
    return float2(sqrt(saturate(0.5 + 0.5 * doubled.x)),
        (doubled.y < 0.0 ? -1.0 : 1.0) * sqrt(saturate(0.5 - 0.5 * doubled.x)));
}

#endif
