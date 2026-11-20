// Грязь в стыках (HDRP, полноэкранный Custom Pass перед постобработкой; ставит HdrpSetup → объект «Crease Grime» в сцене).
// В каждом вогнутом стыке (стена-стена, стена-пол, балка в стене, предмет на стене) поверхность темнеет и пачкается
// по мировому шуму, с тонкими трещинками; выпуклые рёбра — сбитая светлая кромка. Нормали — из глубины (только
// настоящая геометрия). Не зависит от материала: работает на всех ассетах сразу.
Shader "Fortress/CreaseGrime"
{
    Properties
    {
        _NoiseTex ("Noise (world)", 2D) = "gray" {}
        _Radius ("Radius (m)", Float) = 0.14
        _Strength ("Strength", Range(0, 1)) = 0.6
        _GrimeColor ("Grime Color", Color) = (0.32, 0.27, 0.21, 1)
        _NoiseScale ("Noise Scale (1/m)", Float) = 0.9
        _CrackStrength ("Cracks", Range(0, 1)) = 0.35
        _FadeDistance ("Fade Distance (m)", Float) = 45
        _EdgeWear ("Convex Edge Wear", Range(0, 1)) = 0.55
        _EdgeColor ("Edge Wear Tint", Color) = (1.32, 1.26, 1.15, 1)
    }

    HLSLINCLUDE
    #pragma vertex Vert
    #pragma target 4.5
    #pragma only_renderers d3d11 playstation xboxone xboxseries vulkan metal switch switch2

    #include "Packages/com.unity.render-pipelines.high-definition/Runtime/RenderPipeline/RenderPass/CustomPass/CustomPassCommon.hlsl"

    TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
    float _Radius, _Strength, _NoiseScale, _CrackStrength, _FadeDistance, _EdgeWear;
    float4 _GrimeColor, _EdgeColor;

    float RawDepth(float2 uv)
    {
        uint2 px = (uint2)clamp(uv * _ScreenSize.xy, 0, _ScreenSize.xy - 1);
        return LoadCameraDepth(px);
    }

    // позиция относительно камеры (HDRP camera-relative)
    float3 RelPos(float2 uv, float d) { return ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP); }

    float3 GeoNormal(float2 uv, float3 p)
    {
        float2 px = _ScreenSize.zw;
        float3 pr = RelPos(uv + float2(px.x, 0), RawDepth(uv + float2(px.x, 0)));
        float3 pu = RelPos(uv + float2(0, px.y), RawDepth(uv + float2(0, px.y)));
        float3 pl = RelPos(uv - float2(px.x, 0), RawDepth(uv - float2(px.x, 0)));
        float3 pd = RelPos(uv - float2(0, px.y), RawDepth(uv - float2(0, px.y)));
        // берём меньшие разности (не перескакивать через край предмета)
        float3 dx = length(pr - p) < length(p - pl) ? pr - p : p - pl;
        float3 dy = length(pu - p) < length(p - pd) ? pu - p : p - pd;
        float3 n = normalize(cross(dy, dx));
        float3 toCam = GetCurrentViewPosition() - p;
        return dot(n, toCam) < 0 ? -n : n;
    }

    float Tri(float3 p, float3 n, float s)
    {
        float3 w = abs(n); w /= (w.x + w.y + w.z + 1e-4);
        float a = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.zy * s).r;
        float b = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xz * s).r;
        float c = SAMPLE_TEXTURE2D(_NoiseTex, sampler_NoiseTex, p.xy * s).r;
        return a * w.x + b * w.y + c * w.z;
    }

    float4 FullScreenPass(Varyings varyings) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(varyings);
        float2 uv = varyings.positionCS.xy * _ScreenSize.zw;
        float3 col = CustomPassLoadCameraColor(varyings.positionCS.xy, 0);
        float d = LoadCameraDepth(varyings.positionCS.xy);
    #if UNITY_REVERSED_Z
        if (d <= 1e-6) return float4(col, 1);
    #else
        if (d >= 1 - 1e-6) return float4(col, 1);
    #endif
        float3 p = RelPos(uv, d);
        float3 n = GeoNormal(uv, p);
        float eye = LinearEyeDepth(d, _ZBufferParams);
        if (eye > _FadeDistance) return float4(col, 1);
        // радиус в метрах -> в экранных координатах
        float2 r = _Radius / eye * float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11) * 0.5;
        float crease = 0, ridge = 0;
        // поворот узора выборки на каждом пикселе (шум) — без полос-призраков
        float ign = frac(52.9829189 * frac(dot(varyings.positionCS.xy, float2(0.06711056, 0.00583715))));
        [unroll] for (int i = 0; i < 8; i++)
        {
            float a = (i + ign) * 0.7853982;
            float2 dir = float2(cos(a), sin(a));
            [unroll] for (int k = 1; k <= 2; k++)
            {
                float2 q = uv + dir * r * (k - 0.5 + 0.5 * ign);
                float dq = RawDepth(q);
                float3 pq = RelPos(q, dq);
                float3 v = pq - p;
                float dist = length(v);
                if (dist > _Radius * 3.5 * k || dist < 1e-4) continue;   // другой предмет далеко позади — не стык
                float3 nq = GeoNormal(q, pq);
                float side = dot(n, v / dist);
                float concave = saturate(side * 2.0);                      // соседняя поверхность встаёт перед нами
                float convex = saturate(-side * 2.0);                      // соседняя поверхность уходит за ребро
                float bend = saturate(1.0 - dot(n, nq));                   // нормали расходятся — излом
                crease += concave * bend * saturate(1.0 - dist / (_Radius * 2.0 * k)) / k;
                if (k == 1) ridge += convex * bend * saturate(1.0 - dist / (_Radius * 0.45));
            }
        }
        crease = saturate(crease * 0.33);
        ridge = saturate(ridge * 0.45);
        float3 pw = GetAbsolutePositionWS(p);                              // шум — по миру, не по камере
        float noise = Tri(pw, n, _NoiseScale);
        float fine = Tri(pw, n, _NoiseScale * 6.0);
        float grime = saturate(crease * (0.45 + noise)) * _Strength;
        float crack = (1.0 - smoothstep(0.0, 0.035, abs(fine - 0.5))) * saturate(crease * 2.0 - 0.3) * _CrackStrength;
        float fade = 1.0 - saturate((eye - _FadeDistance * 0.7) / (_FadeDistance * 0.3));
        float3 g = lerp(col, col * _GrimeColor.rgb, grime * fade);
        g *= 1.0 - crack * fade * 0.55;
        float chip = saturate((noise - 0.25) * 1.6) * (0.6 + 0.4 * fine);
        float wear = ridge * chip * _EdgeWear * fade;
        g = lerp(g, g * _EdgeColor.rgb, wear);
        g *= 1.0 - ridge * (1.0 - chip) * 0.18 * fade;
        return float4(g, 1);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderPipeline" = "HDRenderPipeline" }
        Pass
        {
            Name "CreaseGrime"
            ZWrite Off
            ZTest Always
            Blend Off
            Cull Off
            HLSLPROGRAM
            #pragma fragment FullScreenPass
            ENDHLSL
        }
    }
    Fallback Off
}
