#ifndef FORTRESS_SEETHROUGH_DEPTHNORMALS_INCLUDED
#define FORTRESS_SEETHROUGH_DEPTHNORMALS_INCLUDED
// после LitDepthNormalsPass.hlsl: та же вырезка окна в проходе глубины и нормалей (SSAO)
#include "SeeThrough.hlsl"

void DepthNormalsFragmentST(
    Varyings input
    , out half4 outNormalWS : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    SeeThroughClip(input.positionCS);
    DepthNormalsFragment(input, outNormalWS
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
}

#endif
