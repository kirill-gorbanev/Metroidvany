#ifndef FORTRESS_SEETHROUGH_FORWARD_INCLUDED
#define FORTRESS_SEETHROUGH_FORWARD_INCLUDED
// после прохода LitPassFragment (URP Lit и копии набора): вырезка окна вокруг героя в основном проходе.
// _ST_CAP (материал двусторонний ради разреза): изнанка — тёмная сетка (половина точек). Камера в толще стены видит
// сетку, а не плоскую заливку или пустоту за стеной.
#include "SeeThrough.hlsl"

void LitPassFragmentST(
    Varyings input
    , FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC
    , out half4 outColor : SV_Target0
#ifdef _WRITE_RENDERING_LAYERS
    , out uint outRenderingLayers : SV_Target1
#endif
)
{
    SeeThroughClip(input.positionCS);
    LitPassFragment(input, outColor
#ifdef _WRITE_RENDERING_LAYERS
        , outRenderingLayers
#endif
    );
#if defined(_ST_CAP) && !defined(_SURFACE_TYPE_TRANSPARENT)
    if (!IS_FRONT_VFACE(face, true, false))
    {
        // изнанка (толща стены у камеры, щели стыков) — не плоская заливка, а тёмная сетка
        uint2 q = (uint2)input.positionCS.xy & 1;
        clip((q.x ^ q.y) ? 1.0 : -1.0);
        outColor = half4(0.018, 0.016, 0.014, 1.0);
    }
#endif
}

#endif
