#ifndef YPH_HERO_HAIR_CARDS_FORWARD_INCLUDED
#define YPH_HERO_HAIR_CARDS_FORWARD_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
#include "Assets/Shaders/YPH/HeroHairCardsSurface.hlsl"
#define HairPassFragment HairCardInternalFragment
#include "Assets/Shaders/YPH/HeroHairDetailForward.hlsl"
#undef HairPassFragment

// 알파로 빈 공간을 잘라 투명 정렬 문제를 피한다. 뒷면에서도 같은 머리결을
// 보이도록 기하 법선과 TBN의 방향을 함께 뒤집은 뒤 공통 조명을 계산한다.
void HairCardPassFragment(Varyings input, FRONT_FACE_TYPE frontFace : FRONT_FACE_SEMANTIC,
    out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    Alpha(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a, _BaseColor, _Cutoff);
    half faceSign = IS_FRONT_VFACE(frontFace, 1.0, -1.0);
    input.normalWS *= faceSign;
#if defined(REQUIRES_WORLD_SPACE_TANGENT_INTERPOLATOR)
    input.tangentWS.w *= faceSign;
#endif
    HairCardInternalFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}

#endif
