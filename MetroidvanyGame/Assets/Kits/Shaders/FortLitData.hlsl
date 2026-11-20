// HDRP Lit + надстройки крепости. Подключается вместо Lit/LitData.hlsl в шейдерах Fortress/* (их собирает
// tools/gen_hdrp_shaders.py из HDRP/Lit этой версии Unity). Оборачивает GetSurfaceAndBuiltinData:
//  - FORT_WORLDUV: кладка по мировым координатам (на стенах u = x + z, v = y; на горизонтальных u = x, v = z),
//    масштаб — тайлинг Base Map (0.5 = плитка на 2 м);
//  - FORT_VERTEXCOLOR: альбедо × цвет вершин.

#define GetSurfaceAndBuiltinData GetSurfaceAndBuiltinData_HDLit
#include "Packages/com.unity.render-pipelines.high-definition/Runtime/Material/Lit/LitData.hlsl"
#undef GetSurfaceAndBuiltinData

void GetSurfaceAndBuiltinData(FragInputs input, float3 V, inout PositionInputs posInput, out SurfaceData surfaceData, out BuiltinData builtinData)
{
#ifdef FORT_WORLDUV
    {
        float3 pW = GetAbsolutePositionWS(input.positionRWS);
        float3 nW = input.tangentToWorld[2];
        float2 wuv; float3 wT; float3 wB;
        if (abs(nW.y) > 0.6) { wuv = pW.xz; wT = float3(1, 0, 0); wB = float3(0, 0, 1); }
        else { wuv = float2(pW.x + pW.z, pW.y); float3 g = float3(1, 0, 1); wT = normalize(g - nW * dot(nW, g) + 1e-5); wB = float3(0, 1, 0); }
        input.texCoord0 = float4(wuv, 0, 0);
        input.tangentToWorld[0] = wT;
        input.tangentToWorld[1] = wB;
    }
#endif

    GetSurfaceAndBuiltinData_HDLit(input, V, posInput, surfaceData, builtinData);

#if defined(FORT_VERTEXCOLOR) && (SHADERPASS == SHADERPASS_GBUFFER || SHADERPASS == SHADERPASS_FORWARD)
    surfaceData.baseColor *= input.color.rgb;
#endif
}
