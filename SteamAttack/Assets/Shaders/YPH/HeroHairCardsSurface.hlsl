#ifndef YPH_HERO_HAIR_CARDS_SURFACE_INCLUDED
#define YPH_HERO_HAIR_CARDS_SURFACE_INCLUDED

// 반사·색 공간·법선 계산을 본체와 공유하고 카드의 표면 데이터만 바꾼다.
#define HairReadSurface HairOpaqueReadSurface
#include "Assets/Shaders/YPH/HeroHairSurface.hlsl"
#undef HairReadSurface

void HairReadSurface(float2 uv, out float4 groom, out float4 flow)
{
    groom = SAMPLE_TEXTURE2D(_HairBakeMap, sampler_HairBakeMap, uv);
    // 카드의 V 방향이 뿌리에서 끝으로 향하므로 두 배 각도는 (-1, 0)이다.
    flow = float4(0.0, 0.5, 1.0, 0.0);
}

#endif
