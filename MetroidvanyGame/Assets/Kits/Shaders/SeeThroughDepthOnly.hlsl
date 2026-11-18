#ifndef FORTRESS_SEETHROUGH_DEPTHONLY_INCLUDED
#define FORTRESS_SEETHROUGH_DEPTHONLY_INCLUDED
// после DepthOnlyPass.hlsl: та же вырезка окна в проходе глубины (иначе SSAO и глубина видят вырезанную стену)
#include "SeeThrough.hlsl"

half DepthOnlyFragmentST(Varyings input) : SV_TARGET
{
    SeeThroughClip(input.positionCS);
    return DepthOnlyFragment(input);
}

#endif
