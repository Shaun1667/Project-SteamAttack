// 원본: SemiReal-Ink-Character-Shader (jes79) 의 ShaderTest/SemiRealInk 를 가져와 이름만 NGH/SemiRealInk 로 바꿈
// 세미리얼 캐릭터 쉐이더 (URP)
// - 키 라이트: 장면의 메인 디렉셔널 라이트 (부드러운 명암 경계 + 그림자)
// - 필 라이트: 카메라 기준 방향의 보조광 (쉐이더 안에서 계산)
// - 림 라이트: 카메라 기준 뒤쪽에서 오는 역광 (윤곽 강조)
// - 재질: 머티리얼 타입(일반/피부 SSS/천) + 텍스처 색으로 금·은·보석 부분을 픽셀 단위로 자동 판별
//         (마스크 텍스처를 넣으면 자동 판별 대신 그걸 쓴다: R = 금속, G = 유리·보석)
// - 먹 아웃라인: 노멀 방향으로 부풀린 뒷면을 반투명 먹색으로 그림, 굵기·농도가 붓처럼 들쭉날쭉
// 기본 색은 "얼어붙은 세상" 기준: 그늘·보조광·역광이 차갑고, 피부 혈색(SSS)만 따뜻하다
Shader "NGH/SemiRealInk"
{
    Properties
    {
        [Header(Surface)]
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        [Normal][NoScaleOffset] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0, 2)) = 1
        _Smoothness ("Smoothness", Range(0, 1)) = 0.35
        _SpecularStrength ("Specular Strength", Range(0, 2)) = 1
        _ReflectionStrength ("Environment Reflection", Range(0, 2)) = 1

        [Header(Material Type)]
        [KeywordEnum(Standard, Skin, Cloth)] _MaterialType ("Material Type", Float) = 0

        [Header(Skin Subsurface Scattering)]
        _SubsurfaceColor ("Subsurface Color", Color) = (0.85, 0.22, 0.12, 1)
        _ScatterWidth ("Scatter Width", Range(0, 1)) = 0.55
        _DiffuseNormalBlur ("Diffuse Normal Blur", Range(0, 1)) = 0.5
        _TransmissionStrength ("Transmission (Back Light)", Range(0, 2)) = 0.35
        _TransmissionPower ("Transmission Focus", Range(1, 16)) = 4
        _TransmissionDistortion ("Transmission Distortion", Range(0, 1)) = 0.35

        [Header(Cloth)]
        _SheenColor ("Sheen Color", Color) = (0.72, 0.84, 1, 1)
        _SheenIntensity ("Sheen Intensity", Range(0, 2)) = 0.45
        _SheenPower ("Sheen Falloff", Range(1, 8)) = 4
        _ClothSpecular ("Cloth Specular Scale", Range(0, 1)) = 0.3

        [Header(Auto Detect Gold Brass Copper)]
        _MetalDetect ("Metal Detect", Range(0, 1)) = 0
        _MetalHue ("Hue Center", Range(0, 1)) = 0.115
        _MetalHueWidth ("Hue Width", Range(0.01, 0.3)) = 0.055
        _MetalSatMin ("Saturation Min", Range(0, 1)) = 0.35
        _MetalValMin ("Brightness Min", Range(0, 1)) = 0.42
        _MetalSmoothness ("Metal Smoothness", Range(0, 1)) = 0.78
        _MetalBoost ("Metal Shine Boost", Range(0, 3)) = 1
        _MetalSaturation ("Metal Color Saturation", Range(0, 3)) = 0

        [Header(Auto Detect Silver Steel Mirror)]
        _SilverDetect ("Silver Detect", Range(0, 1)) = 0
        _SilverSatMax ("Saturation Max", Range(0, 1)) = 0.12
        _SilverValMin ("Brightness Min", Range(0, 1)) = 0.7

        [Header(Auto Detect Glass Gem)]
        _GemDetect ("Gem Detect", Range(0, 1)) = 0
        _GemHue ("Hue Center", Range(0, 1)) = 0.42
        _GemHueWidth ("Hue Width", Range(0.01, 0.3)) = 0.09
        _GemSatMin ("Saturation Min", Range(0, 1)) = 0.4
        _GemSmoothness ("Gem Smoothness", Range(0, 1)) = 0.95
        _GemGlow ("Gem Inner Glow", Range(0, 2)) = 0.6

        [Header(Mask Map Optional)]
        // 마스크 맵: R = 금속(금·황동·금실), G = 유리·보석, B = 은·거울. 위 Detect 슬라이더가 각 채널 세기가 된다
        [Toggle(_MASKMAP)] _UseMaskMap ("Use Mask Map (R Metal, G Gem, B Silver)", Float) = 0
        [NoScaleOffset] _MaskMap ("Mask Map", 2D) = "black" {}

        [Header(Two Sided)]
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Render Face (Off = 2-Sided)", Float) = 2
        _BackFaceColor ("Back Face Color (A = Amount)", Color) = (0.55, 0.1, 0.1, 0)

        [Header(Key Light)]
        _Realism ("Realism (0 Stylized, 1 Real)", Range(0, 1)) = 0.5
        _ShadeSoftness ("Shade Edge Softness", Range(0.01, 1)) = 0.35
        _ShadeOffset ("Shade Edge Offset", Range(-1, 1)) = 0
        _ShadowTint ("Shadow Tint", Color) = (0.55, 0.63, 0.8, 1)

        [Header(Fill Light)]
        _FillColor ("Fill Color", Color) = (0.55, 0.7, 1, 1)
        _FillIntensity ("Fill Intensity", Range(0, 2)) = 0.35
        _FillDirection ("Fill Direction (Camera Space)", Vector) = (-0.7, 0.2, 0.6, 0)

        [Header(Rim Light)]
        _RimColor ("Rim Color", Color) = (0.78, 0.9, 1, 1)
        _RimIntensity ("Rim Intensity", Range(0, 3)) = 0.8
        _RimWidth ("Rim Width", Range(0.01, 1)) = 0.35
        _RimSoftness ("Rim Softness", Range(0.01, 1)) = 0.3
        _RimDirection ("Rim Direction (Camera Space)", Vector) = (0.6, 0.5, -0.6, 0)

        [Header(Ambient)]
        _AmbientStrength ("Ambient Strength", Range(0, 2)) = 1

        [Header(Ink Outline)]
        _OutlineColor ("Ink Color", Color) = (0.06, 0.07, 0.1, 1)
        _OutlineWidth ("Width (Pixels)", Range(0, 8)) = 1.6
        _OutlineOpacity ("Ink Opacity", Range(0, 1)) = 0.5
        _OutlineBaseBlend ("Blend With Base Color", Range(0, 1)) = 0.35
        _OutlineNoiseScale ("Brush Noise Scale", Range(0, 60)) = 18
        _OutlineNoiseAmount ("Brush Variation", Range(0, 1)) = 0.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);    SAMPLER(sampler_BaseMap);
        TEXTURE2D(_BumpMap);    SAMPLER(sampler_BumpMap);
        TEXTURE2D(_MaskMap);    SAMPLER(sampler_MaskMap);

        // 비교 뷰어가 전역으로 켜는 "재질 판별 보기" (머티리얼 값이 아니라 장면 전체 스위치)
        half _ShaderTestDebugMasks;
        // 비교 뷰어의 "아웃라인 숨기기" 스위치 (0 = 보임이 기본값: 뷰어가 없는 장면에서도 아웃라인이 나온다)
        half _ShaderTestHideOutline;

        // 모든 패스가 같은 머티리얼 버퍼를 써야 SRP Batcher가 동작한다
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half _BumpScale;
            half _Smoothness;
            half _SpecularStrength;
            half _ReflectionStrength;
            half4 _SubsurfaceColor;
            half _ScatterWidth;
            half _DiffuseNormalBlur;
            half _TransmissionStrength;
            half _TransmissionPower;
            half _TransmissionDistortion;
            half4 _SheenColor;
            half _SheenIntensity;
            half _SheenPower;
            half _ClothSpecular;
            half _MetalDetect;
            half _MetalHue;
            half _MetalHueWidth;
            half _MetalSatMin;
            half _MetalValMin;
            half _MetalSmoothness;
            half _MetalBoost;
            half _MetalSaturation;
            half _SilverDetect;
            half _SilverSatMax;
            half _SilverValMin;
            half _GemDetect;
            half _GemHue;
            half _GemHueWidth;
            half _GemSatMin;
            half _GemSmoothness;
            half _GemGlow;
            half4 _BackFaceColor;
            half _Realism;
            half _ShadeSoftness;
            half _ShadeOffset;
            half4 _ShadowTint;
            half4 _FillColor;
            half _FillIntensity;
            float4 _FillDirection;
            half4 _RimColor;
            half _RimIntensity;
            half _RimWidth;
            half _RimSoftness;
            float4 _RimDirection;
            half _AmbientStrength;
            half4 _OutlineColor;
            half _OutlineWidth;
            half _OutlineOpacity;
            half _OutlineBaseBlend;
            half _OutlineNoiseScale;
            half _OutlineNoiseAmount;
        CBUFFER_END
        ENDHLSL

        // ------------------------------------------------------------------
        // 1. 본체: 재질별 표면 + 키·필·림 조명
        // ------------------------------------------------------------------
        Pass
        {
            Name "ForwardLit"
            // ForwardOnly: 포워드·디퍼드 렌더러 모두에서 그려진다
            Tags { "LightMode" = "UniversalForwardOnly" }

            Cull [_Cull]
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma shader_feature_local _MATERIALTYPE_STANDARD _MATERIALTYPE_SKIN _MATERIALTYPE_CLOTH
            #pragma shader_feature_local_fragment _MASKMAP

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BLENDING
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_BOX_PROJECTION
            #pragma multi_compile_fragment _ _REFLECTION_PROBE_ATLAS
            #pragma multi_compile_fragment _ _LIGHT_COOKIES
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
                float4 tangentWS  : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = nrmInputs.normalWS;
                output.tangentWS = float4(nrmInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(posInputs.positionCS.z);
                return output;
            }

            // ---------------- 재질 판별 ----------------
            struct SurfaceMasks
            {
                half metal;   // 금·황동·구리 (색 있는 금속)
                half silver;  // 은·강철·거울 (무채색 금속)
                half gem;     // 유리·보석
            };

            half HueMask(half hue, half center, half width)
            {
                half d = abs(hue - center);
                d = min(d, 1.0h - d);   // 색상환은 0과 1이 이어져 있다
                return 1.0h - smoothstep(width * 0.5h, width, d);
            }

            SurfaceMasks ComputeMasks(half3 baseLinear, float2 uv)
            {
                SurfaceMasks m;
            #if defined(_MASKMAP)
                half4 mask = SAMPLE_TEXTURE2D(_MaskMap, sampler_MaskMap, uv);
                m.metal = mask.r * _MetalDetect;
                m.silver = mask.b * _SilverDetect;
                m.gem = mask.g * _GemDetect;
            #else
                // 사람이 보는 색(sRGB) 기준 색상·채도·밝기로 판별해야 슬라이더 값이 직관적이다
                half3 hsv = RgbToHsv(LinearToSRGB(baseLinear));
                m.metal = HueMask(hsv.x, _MetalHue, _MetalHueWidth)
                        * smoothstep(_MetalSatMin, _MetalSatMin + 0.1h, hsv.y)
                        * smoothstep(_MetalValMin, _MetalValMin + 0.12h, hsv.z)
                        * _MetalDetect;
                m.silver = (1.0h - smoothstep(_SilverSatMax * 0.5h, _SilverSatMax, hsv.y))
                        * smoothstep(_SilverValMin, _SilverValMin + 0.1h, hsv.z)
                        * _SilverDetect;
                m.gem = HueMask(hsv.x, _GemHue, _GemHueWidth)
                        * smoothstep(_GemSatMin, _GemSatMin + 0.1h, hsv.y)
                        * smoothstep(0.2h, 0.3h, hsv.z)
                        * _GemDetect;
            #endif
                return m;
            }

            // ---------------- 조명 ----------------
            // 키 라이트 명암: 실사(람버트)와 부드러운 2단 명암을 섞는다
            half ShadeTerm(half NdotL)
            {
                half lambert = saturate(NdotL);
                half stylized = smoothstep(_ShadeOffset - _ShadeSoftness, _ShadeOffset + _ShadeSoftness, NdotL);
                return lerp(stylized, lambert, _Realism);
            }

            // 빛 하나의 확산(재질별) + 반사 하이라이트(GGX). shade3 = 명암값 (그림자 색 입히기에 사용)
            half3 EvaluateLight(BRDFData brdf, Light light, half3 N, half3 Nd, half3 V, half specScale, out half3 shade3)
            {
                half NdotL = dot(Nd, light.direction);
                half3 transmission = 0;
            #if defined(_MATERIALTYPE_SKIN)
                // 피부 SSS: 빨강이 더 깊이 퍼진다 → 명암 경계가 붉고 부드럽게 넘어간다
                half3 w = _ScatterWidth * half3(1.0h, 0.45h, 0.25h);
                half3 wrapped = saturate((NdotL + w) / (1.0h + w));
                half3 shade = lerp(ShadeTerm(NdotL).xxx, wrapped, max(_Realism, 0.6h));
                // 그림자 가장자리(반그림자)도 붉게 번진다
                half shadow = light.shadowAttenuation;
                half penumbra = shadow * (1.0h - shadow) * 4.0h;
                half3 shadow3 = saturate(shadow + penumbra * _ScatterWidth * 0.6h * _SubsurfaceColor.rgb);
                shade3 = shade * shadow3 * light.distanceAttenuation;
                // 투과광: 귀·손가락처럼 얇은 곳 뒤에서 빛이 오면 붉게 비친다
                half3 transDir = light.direction + N * _TransmissionDistortion;
                half trans = pow(saturate(dot(V, -transDir)), _TransmissionPower) * _TransmissionStrength;
                transmission = trans * _SubsurfaceColor.rgb * lerp(1.0h, shadow, 0.5h) * light.distanceAttenuation;
            #else
                shade3 = (ShadeTerm(NdotL) * light.shadowAttenuation * light.distanceAttenuation).xxx;
            #endif
                half3 diffuse = brdf.diffuse * (shade3 + transmission);
                half NdotLs = saturate(dot(N, light.direction));
                half3 spec = brdf.specular * DirectBRDFSpecular(brdf, N, light.direction, V)
                           * NdotLs * light.shadowAttenuation * light.distanceAttenuation * specScale;
                return (diffuse + spec) * light.color;
            }

            half3 CameraSpaceDirToWorld(float3 dirVS)
            {
                return SafeNormalize(mul((float3x3)UNITY_MATRIX_I_V, dirVS));
            }

            half4 Frag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half3 albedo = baseSample.rgb * _BaseColor.rgb;
                bool isFrontFace = IS_FRONT_VFACE(facing, true, false);
                // 안쪽 면(한 겹 옷의 안감): 지정한 색으로 바꿀 수 있다
                albedo = isFrontFace ? albedo : lerp(albedo, _BackFaceColor.rgb, _BackFaceColor.a);

                // 노멀맵
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float sgn = input.tangentWS.w;
                float3 bitangent = sgn * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS);
                half faceSign = isFrontFace ? 1.0h : -1.0h;   // 안쪽 면은 노멀을 뒤집어야 빛을 제대로 받는다
                half3 N = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld)) * faceSign;
                half3 Ngeo = NormalizeNormalPerPixel(input.normalWS) * faceSign;
                half3 V = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half NdotV = saturate(dot(N, V));

                // ---- 재질 판별 ----
                SurfaceMasks masks = ComputeMasks(baseSample.rgb, input.uv);
                half metallic = saturate(masks.metal + masks.silver);
                half gem = masks.gem * (1.0h - metallic);

                if (_ShaderTestDebugMasks > 0.5h)
                {
                    // 판별 결과 보기: 노랑 = 금속, 흰색 = 은·거울, 초록 = 유리·보석, 붉은 기 = 피부, 푸른 기 = 천
                    half3 dbg = albedo * 0.25h + half3(1.0h, 0.8h, 0.1h) * masks.metal
                              + half3(0.9h, 0.9h, 1.0h) * masks.silver + half3(0.1h, 1.0h, 0.5h) * gem;
                #if defined(_MATERIALTYPE_SKIN)
                    dbg = lerp(dbg, half3(1.0h, 0.45h, 0.4h), 0.5h);
                #elif defined(_MATERIALTYPE_CLOTH)
                    dbg += half3(0.05h, 0.1h, 0.35h);
                #endif
                    return half4(dbg, 1);
                }

                half smoothness = lerp(_Smoothness, _MetalSmoothness, metallic);
                smoothness = lerp(smoothness, _GemSmoothness, gem);

                half alpha = 1.0h;
                BRDFData brdf;
                InitializeBRDFData(albedo, metallic, half3(0, 0, 0), smoothness, alpha, brdf);
                // 보석·유리: 일반 표면(4%)보다 반사율이 높고, 확산(속 색)은 줄인다
                brdf.specular = lerp(brdf.specular, half3(0.08h, 0.08h, 0.08h) + albedo * 0.15h, gem);
                brdf.diffuse *= 1.0h - gem * 0.6h;
                // 금속 반사색의 채도를 올린다: 채도 낮은 금실(베이지)이 차가운 하늘을 비춰 은색처럼 보이는 것을 막는다
                half f0Luma = dot(brdf.specular, half3(0.2126h, 0.7152h, 0.0722h));
                half3 saturatedF0 = max(0.0h, f0Luma + (brdf.specular - f0Luma) * (1.0h + _MetalSaturation));
                brdf.specular = lerp(brdf.specular, saturatedF0, metallic);

                // 천은 하이라이트·반사를 줄인다 (금실 자수처럼 금속으로 판별된 곳은 그대로)
                half surfaceSpec = 1.0h;
            #if defined(_MATERIALTYPE_CLOTH)
                surfaceSpec = lerp(_ClothSpecular, 1.0h, saturate(metallic + gem));
            #endif
                // 금속 부분(금실 자수 포함)만 하이라이트·반사를 더 키울 수 있다
                half metalBoost = lerp(1.0h, _MetalBoost, metallic);
                half specScale = _SpecularStrength * surfaceSpec * metalBoost;

                // 피부는 확산광에 노멀맵을 흐리게 써서 모공이 거칠게 보이지 않게 한다
                half3 Nd = N;
            #if defined(_MATERIALTYPE_SKIN)
                Nd = SafeNormalize(lerp(N, Ngeo, _DiffuseNormalBlur));
            #endif

                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                half aoDirect = 1.0h;
                half aoIndirect = 1.0h;
            #if defined(_SCREEN_SPACE_OCCLUSION)
                AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
                aoDirect = ao.directAmbientOcclusion;
                aoIndirect = ao.indirectAmbientOcclusion;
            #endif

                // ---- 키 라이트 (장면 메인 라이트) ----
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1, 1, 1, 1));
                half3 keyShade;
                half3 color = EvaluateLight(brdf, mainLight, N, Nd, V, specScale, keyShade) * aoDirect;

                // ---- 환경광 + 환경 반사 (금속·보석이 금속답게 보이는 핵심) ----
                half3 ambient = SampleSH(Nd) * _AmbientStrength * aoIndirect;
                // 그늘진 쪽은 차가운 색으로 물들인다 (얼어붙은 세상)
                color += brdf.diffuse * ambient * lerp(_ShadowTint.rgb, half3(1, 1, 1), keyShade);
                half3 reflectDir = reflect(-V, N);
                half3 envRefl = GlossyEnvironmentReflection(reflectDir, input.positionWS, brdf.perceptualRoughness, aoIndirect, screenUV);
                half fresnel = Pow4(1.0h - NdotV);
                color += envRefl * EnvironmentBRDFSpecular(brdf, fresnel) * _ReflectionStrength * surfaceSpec * metalBoost;

                // ---- 추가 라이트 (포인트·스팟) ----
                // 버텍스 라이팅 모드여도 픽셀 단위로 계산한다
                #if defined(_ADDITIONAL_LIGHTS) || defined(_ADDITIONAL_LIGHTS_VERTEX)
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = screenUV;
                half3 unusedShade;

                #if USE_CLUSTER_LIGHT_LOOP
                [loop] for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dirLight = GetAdditionalLight(dirIndex, input.positionWS, half4(1, 1, 1, 1));
                    color += EvaluateLight(brdf, dirLight, N, Nd, V, specScale, unusedShade) * aoDirect;
                }
                #endif

                uint pixelLightCount = GetAdditionalLightsCount();
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light addLight = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    color += EvaluateLight(brdf, addLight, N, Nd, V, specScale, unusedShade) * aoDirect;
                LIGHT_LOOP_END
                #endif

                // ---- 필 라이트 (카메라 기준, 그림자 없음) ----
                half3 fillDir = CameraSpaceDirToWorld(_FillDirection.xyz);
                half fillWrap = saturate(dot(Nd, fillDir) * 0.5h + 0.5h);
                half3 fillLight = _FillColor.rgb * _FillIntensity;
                color += brdf.diffuse * fillLight * (fillWrap * fillWrap);
                // 금속·보석은 필 라이트에서도 두 번째 하이라이트가 생겨 입체감이 산다
                color += brdf.specular * DirectBRDFSpecular(brdf, N, fillDir, V) * saturate(dot(N, fillDir)) * fillLight * specScale;

                // ---- 림 라이트 (카메라 기준 역광) ----
                half3 rimDir = CameraSpaceDirToWorld(_RimDirection.xyz);
                half rimMask = smoothstep(1.0h - _RimWidth, 1.0h - _RimWidth + _RimSoftness, 1.0h - NdotV);
                half rimFacing = saturate(dot(N, rimDir) * 0.5h + 0.5h);
                half3 rimTint = lerp(albedo, half3(1, 1, 1), 0.5h);
            #if defined(_MATERIALTYPE_SKIN)
                rimTint = lerp(rimTint, _SubsurfaceColor.rgb, 0.3h);   // 피부 가장자리는 살짝 붉게 비친다
            #endif
                color += rimTint * _RimColor.rgb * rimMask * rimFacing * _RimIntensity;

                // ---- 천: 표면 솜털이 빛을 받아 가장자리가 부드럽게 빛난다 (sheen) ----
            #if defined(_MATERIALTYPE_CLOTH)
                half sheen = pow(1.0h - NdotV, _SheenPower) * _SheenIntensity * (1.0h - metallic);
                color += _SheenColor.rgb * sheen * (mainLight.color * keyShade + ambient);
            #endif

                // ---- 보석·유리: 정면에서 볼수록 속이 은은하게 빛난다 ----
                color += albedo * gem * _GemGlow * (NdotV * NdotV) * (mainLight.color * 0.5h + ambient);

                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // 2. 먹 아웃라인 (뒷면을 부풀려 그리기)
        // ------------------------------------------------------------------
        Pass
        {
            Name "InkOutline"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            Cull Front
            ZWrite On
            // 본체보다 아주 살짝 뒤로 밀어, 양면 옷의 안쪽 면 위에 먹이 덮이지 않게 한다
            Offset 1, 1
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float  brush      : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            float Hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            // 부드러운 3D 값 노이즈 (0~1): 굵기가 붓처럼 천천히 변하게 한다
            float ValueNoise3D(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float n000 = Hash13(i);
                float n100 = Hash13(i + float3(1, 0, 0));
                float n010 = Hash13(i + float3(0, 1, 0));
                float n110 = Hash13(i + float3(1, 1, 0));
                float n001 = Hash13(i + float3(0, 0, 1));
                float n101 = Hash13(i + float3(1, 0, 1));
                float n011 = Hash13(i + float3(0, 1, 1));
                float n111 = Hash13(i + float3(1, 1, 1));
                float nx00 = lerp(n000, n100, f.x);
                float nx10 = lerp(n010, n110, f.x);
                float nx01 = lerp(n001, n101, f.x);
                float nx11 = lerp(n011, n111, f.x);
                return lerp(lerp(nx00, nx10, f.y), lerp(nx01, nx11, f.y), f.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                float4 positionCS = TransformWorldToHClip(positionWS);

                // 노이즈는 오브젝트 원점 기준 월드 거리(미터): FBX 단위와 상관없이 같은 크기, 캐릭터가 움직여도 붓 자국이 따라다닌다
                float3 objectOriginWS = TransformObjectToWorld(float3(0, 0, 0));
                float brush = ValueNoise3D((positionWS - objectOriginWS) * _OutlineNoiseScale);
                float widthScale = lerp(1.0, brush * 1.6, _OutlineNoiseAmount);

                // 노멀 방향 한 점을 함께 투영해 화면에서의 실제 방향을 구한 뒤, 픽셀 단위로 일정한 굵기만큼 밀어낸다
                float4 tipCS = TransformWorldToHClip(positionWS + normalWS * 0.01);
                float2 ndcDelta = tipCS.xy / tipCS.w - positionCS.xy / positionCS.w;
                float2 dirPixels = SafeNormalize(float3(ndcDelta * _ScreenParams.xy, 0)).xy;
                float2 pixelToNdc = float2(2.0 / _ScreenParams.x, 2.0 / _ScreenParams.y);
                positionCS.xy += dirPixels * pixelToNdc * (_OutlineWidth * widthScale) * positionCS.w;

                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.brush = brush;
                output.fogFactor = ComputeFogFactor(positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                // 아웃라인 숨기기: 픽셀을 버려 색도 깊이도 남기지 않는다
                clip(0.5h - _ShaderTestHideOutline);

                half3 baseColor = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).rgb * _BaseColor.rgb;
                // 먹색에 바탕색을 조금 섞어 번진 먹처럼 보이게 한다
                half3 ink = lerp(_OutlineColor.rgb, _OutlineColor.rgb * 0.5h + baseColor * 0.35h, _OutlineBaseBlend);
                half alpha = _OutlineOpacity * lerp(1.0h, 0.55h + 0.45h * input.brush, _OutlineNoiseAmount);

                ink = MixFog(ink, input.fogFactor);
                return half4(ink, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // 3. 그림자 드리우기
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 ShadowVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                return ApplyShadowClamping(positionCS);
            }

            half4 ShadowFrag() : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // 4. 깊이 / 깊이+노멀 (SSAO 등 화면 효과용)
        // ------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            float4 DepthVert(Attributes input) : SV_POSITION
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return TransformObjectToHClip(input.positionOS.xyz);
            }

            half DepthFrag(float4 positionCS : SV_POSITION) : SV_Target
            {
                return positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_instancing

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float4 tangentWS  : TEXCOORD2;
            };

            Varyings DepthNormalsVert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                VertexNormalInputs nrmInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.normalWS = nrmInputs.normalWS;
                output.tangentWS = float4(nrmInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                return output;
            }

            // 본체 패스와 같은 노멀(노멀맵 + 안쪽 면 뒤집기)을 써야 SSAO 같은 화면 효과가 어긋나지 않는다
            half4 DepthNormalsFrag(Varyings input, FRONT_FACE_TYPE facing : FRONT_FACE_SEMANTIC) : SV_Target
            {
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uv), _BumpScale);
                float3 bitangent = input.tangentWS.w * cross(input.normalWS, input.tangentWS.xyz);
                half3x3 tangentToWorld = half3x3(input.tangentWS.xyz, bitangent, input.normalWS);
                half3 n = NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, tangentToWorld));
                return half4(IS_FRONT_VFACE(facing, n, -n), 0);
            }
            ENDHLSL
        }
    }

    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
