#ifndef YPH_HERO_HAIR_DETAIL_FORWARD_INCLUDED
#define YPH_HERO_HAIR_DETAIL_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"

#include "Assets/Shaders/YPH/HeroHairSurface.hlsl"

// 방향 맵은 앞뒤가 같은 선을 저장한다. 이동시킨 반사 띠도 두 부호를 평균해
// 복원 방향이 뒤집히는 경계에서 광택이 갑자기 바뀌지 않도록 한다.
half SymmetricStrandLobe(float3 fiberWS, half3 normalWS, float3 halfDirection,
    float shift, float exponent)
{
    float tangentDot = dot(fiberWS, halfDirection);
    float normalDot = dot(normalWS, halfDirection);
    float normalization = rsqrt(1.0 + shift * shift);
    float positive = (tangentDot + normalDot * shift) * normalization;
    float negative = (-tangentDot + normalDot * shift) * normalization;
    return 0.5 * (pow(saturate(1.0 - positive * positive), exponent)
        + pow(saturate(1.0 - negative * negative), exponent));
}

// Kajiya-Kay의 두 반사 띠를 결 방향에 맞춰 계산한다.
// 참고: github.com/vurimad/Hair-rendering-in-unity (MIT).
half3 GroomDirectLight(Light light, half3 albedo, half3 normalWS, float3 fiberWS,
    half3 viewWS, half fiberVariation, half smoothness)
{
    half nl = saturate(dot(normalWS, light.direction));
    half attenuation = light.distanceAttenuation * light.shadowAttenuation;
    half3 diffuse = albedo * nl;
    half3 specular = 0;
#if !defined(_SPECULARHIGHLIGHTS_OFF)
    float3 h = SafeNormalize(float3(light.direction) + float3(viewWS));
    float shift = (fiberVariation - 0.5) * 0.16;
    half primaryLobe = SymmetricStrandLobe(fiberWS, normalWS, h,
        0.10 + shift, lerp(24.0, 150.0, smoothness));
    half secondaryLobe = SymmetricStrandLobe(fiberWS, normalWS, h,
        -0.18 + shift, lerp(8.0, 40.0, smoothness));
    half visibility = nl * smoothstep(0.0, 0.14, saturate(dot(normalWS, viewWS)));
    half fiberReflectance = lerp(0.38, 1.15, fiberVariation);
    specular = (primaryLobe * half3(0.34, 0.34, 0.34)
        + secondaryLobe * half3(0.11, 0.085, 0.065)) * visibility * fiberReflectance;
    // 기존 Lit의 색상 값을 재사용해 SRP Batcher의 상수 버퍼 구성을 유지한다.
    specular *= HairSpecularTint();
#endif
    return light.color * attenuation * (diffuse + specular);
}

void HairPassFragment(
    Varyings input,
    out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

    SurfaceData surfaceData = (SurfaceData)0;
    float4 groom, flow;
    HairReadSurface(input.uv, groom, flow);

    // 시안의 명암을 기존 UV에 맞춰 쓰고, 결 방향의 실시간 광택을 별도로 더한다.
    surfaceData.albedo = HairAlbedo(groom.b);
    surfaceData.normalTS = HairNormalTS(groom.rg);
    surfaceData.smoothness = _Smoothness;
    surfaceData.specular = half3(0.04, 0.04, 0.04);
    surfaceData.alpha = 1.0;
    surfaceData.occlusion = lerp(0.80, 1.0, flow.b);
    surfaceData.emission = 0;

#ifdef LOD_FADE_CROSSFADE
    LODFadeCrossFade(input.positionCS);
#endif

    InputData inputData;
    InitializeInputData(input, surfaceData.normalTS, inputData);
    SETUP_DEBUG_TEXTURE_DATA(inputData, UNDO_TRANSFORM_TEX(input.uv, _BaseMap));
#if defined(_DBUFFER)
    ApplyDecalToSurfaceData(input.positionCS, surfaceData, inputData);
#endif
    InitializeBakedGIData(input, inputData);

    float3 normalWS = inputData.normalWS;
    float3 geometricNormal = SafeNormalize(input.normalWS);
    float3 fiberWS = SafeNormalize(cross(normalWS, TransformObjectToWorldDir(float3(1, 0, 0))));
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    // 스키닝된 TBN에 결을 실어 머리가 움직일 때 함께 회전시킨다.
    float2 directionTS = HairDirectionTS(flow.rg);
    float3 tangentWS = SafeNormalize(input.tangentWS.xyz);
    float3 bitangentWS = input.tangentWS.w * cross(geometricNormal, tangentWS);
    float3 mappedFiber = tangentWS * directionTS.x + bitangentWS * directionTS.y;
    float3 projectedFiber = mappedFiber - normalWS * dot(mappedFiber, normalWS);
    if (dot(projectedFiber, projectedFiber) > 0.0001)
        fiberWS = SafeNormalize(projectedFiber);
#endif

    half4 shadowMask = CalculateShadowMask(inputData);
    AmbientOcclusionFactor aoFactor = CreateAmbientOcclusionFactor(inputData, surfaceData);
    uint renderingLayers = GetMeshRenderingLayer();
    Light mainLight = GetMainLight(inputData, shadowMask, aoFactor);
    MixRealtimeAndBakedGI(mainLight, normalWS, inputData.bakedGI);
    half3 color = inputData.bakedGI * surfaceData.albedo
        * surfaceData.occlusion * aoFactor.indirectAmbientOcclusion;

#ifdef _LIGHT_LAYERS
    if (IsMatchingLightLayer(mainLight.layerMask, renderingLayers))
#endif
        color += GroomDirectLight(mainLight, surfaceData.albedo, normalWS, fiberWS,
            inputData.viewDirectionWS, groom.a, _Smoothness);

#if defined(_ADDITIONAL_LIGHTS_VERTEX)
    color += inputData.vertexLighting * surfaceData.albedo;
#endif
#if defined(_ADDITIONAL_LIGHTS)
    uint pixelLightCount = GetAdditionalLightsCount();
#if USE_CLUSTER_LIGHT_LOOP
    [loop] for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); lightIndex++)
    {
        CLUSTER_LIGHT_LOOP_SUBTRACTIVE_LIGHT_CHECK
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, renderingLayers))
#endif
            color += GroomDirectLight(light, surfaceData.albedo, normalWS, fiberWS,
                inputData.viewDirectionWS, groom.a, _Smoothness);
    }
#endif
    LIGHT_LOOP_BEGIN(pixelLightCount)
        Light light = GetAdditionalLight(lightIndex, inputData, shadowMask, aoFactor);
#ifdef _LIGHT_LAYERS
        if (IsMatchingLightLayer(light.layerMask, renderingLayers))
#endif
            color += GroomDirectLight(light, surfaceData.albedo, normalWS, fiberWS,
                inputData.viewDirectionWS, groom.a, _Smoothness);
    LIGHT_LOOP_END
#endif

#if !defined(_ENVIRONMENTREFLECTIONS_OFF) && !defined(_SPECULARHIGHLIGHTS_OFF)
    // 갓의 그림자 안에서도 실제 환경광을 반사한다. 직접광의 그림자는 유지한다.
    float3 viewWS = inputData.viewDirectionWS;
    float3 reflectionNormal = SafeNormalize(viewWS - fiberWS * dot(viewWS, fiberWS));
    reflectionNormal = SafeNormalize(lerp(normalWS, reflectionNormal, 0.60));
    float3 reflectionDirection = reflect(-viewWS, reflectionNormal);
    half roughness = lerp(0.52, 0.24, _Smoothness);
    half3 reflected = GlossyEnvironmentReflection(reflectionDirection, inputData.positionWS,
        roughness, surfaceData.occlusion, inputData.normalizedScreenSpaceUV);

    half fresnel = 0.045 + 0.25 * Pow4(1.0 - saturate(dot(normalWS, viewWS)));
    float3 skyHalf = SafeNormalize(float3(mainLight.direction) + viewWS);
    half skyLobe = SymmetricStrandLobe(fiberWS, normalWS, skyHalf, 0.06, 12.0);
    half skyFacing = 0.25 + 0.75 * saturate(dot(normalWS, mainLight.direction));
    // 갓 아래 넓은 반사는 주변광과 태양 방향을 이용한 미술적 근사다.
    // 직접광의 그림자는 유지하고, 태양을 끄면 이 띠도 사라지게 한다.
    half3 specularTint = HairSpecularTint();
    half sunLayerWeight = 1.0;
#ifdef _LIGHT_LAYERS
    sunLayerWeight = IsMatchingLightLayer(mainLight.layerMask, renderingLayers) ? 1.0 : 0.0;
#endif
    color += inputData.bakedGI * skyLobe * skyFacing * 0.24 * saturate(Luminance(mainLight.color))
        * lerp(0.12, 1.8, groom.a) * surfaceData.occlusion
        * aoFactor.indirectAmbientOcclusion * specularTint * sunLayerWeight;
    color += reflected * fresnel * lerp(0.65, 1.25, groom.a)
        * surfaceData.occlusion * aoFactor.indirectAmbientOcclusion * specularTint;
#endif

    color = MixFog(color, inputData.fogCoord);
    outColor = half4(color, 1.0);
#ifdef _WRITE_RENDERING_LAYERS
    outRenderingLayers = EncodeMeshRenderingLayer();
#endif
}

#endif
