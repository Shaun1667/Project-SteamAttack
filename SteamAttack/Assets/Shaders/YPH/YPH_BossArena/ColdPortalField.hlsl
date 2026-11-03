#ifndef YPH_COLD_PORTAL_FIELD
#define YPH_COLD_PORTAL_FIELD
TEXTURE3D(_NoiseVolume); SAMPLER(sampler_NoiseVolume);
float PortalNoise(float3 p) {
 return SAMPLE_TEXTURE3D_LOD(_NoiseVolume,sampler_NoiseVolume,p,0).r;
}
float PortalField(float3 p,float time) {
 float3 q=p*float3(3.2,2.8,2.5);
 q.y-=time*0.10; q.z-=time*0.07;
 float3 warp=float3(PortalNoise(q*0.8),PortalNoise(q*0.8+0.31),PortalNoise(q*0.8+0.67))-0.5;
 q+=warp*0.60;
 float3 fineWarp=float3(PortalNoise(q*1.7+0.17),PortalNoise(q*1.7+0.53),PortalNoise(q*1.7+0.83))-0.5;
 q+=fineWarp*0.10;
 return PortalNoise(q)*0.42+PortalNoise(q*2.03+0.13)*0.30+PortalNoise(q*4.07+0.41)*0.20+PortalNoise(q*8.13)*0.08;
}
#endif
