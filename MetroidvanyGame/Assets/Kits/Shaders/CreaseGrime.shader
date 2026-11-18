// Грязь в стыках: полноэкранный проход по глубине и нормалям.
// В каждом вогнутом стыке (стена-стена, стена-пол, балка в стене, предмет на стене) поверхность темнеет и пачкается
// по мировому шуму, с тонкими трещинками. Не зависит от материала: работает на всех ассетах сразу.
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
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always
        Pass
        {
            Name "CreaseGrime"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            TEXTURE2D(_NoiseTex); SAMPLER(sampler_NoiseTex);
            float _Radius, _Strength, _NoiseScale, _CrackStrength, _FadeDistance, _EdgeWear;
            float4 _GrimeColor, _EdgeColor;

            float RawDepth(float2 uv)
            {
            #if UNITY_REVERSED_Z
                return SampleSceneDepth(uv);
            #else
                return lerp(UNITY_NEAR_CLIP_VALUE, 1, SampleSceneDepth(uv));
            #endif
            }

            float3 WorldPos(float2 uv, float d) { return ComputeWorldSpacePosition(uv, d, UNITY_MATRIX_I_VP); }

            float3 GeoNormal(float2 uv, float3 p)
            {
                float2 px = 1.0 / _ScreenParams.xy;
                float3 pr = WorldPos(uv + float2(px.x, 0), RawDepth(uv + float2(px.x, 0)));
                float3 pu = WorldPos(uv + float2(0, px.y), RawDepth(uv + float2(0, px.y)));
                float3 pl = WorldPos(uv - float2(px.x, 0), RawDepth(uv - float2(px.x, 0)));
                float3 pd = WorldPos(uv - float2(0, px.y), RawDepth(uv - float2(0, px.y)));
                // берём меньшие разности (не перескакивать через край предмета)
                float3 dx = length(pr - p) < length(p - pl) ? pr - p : p - pl;
                float3 dy = length(pu - p) < length(p - pd) ? pu - p : p - pd;
                float3 n = normalize(cross(dy, dx));
                float3 toCam = _WorldSpaceCameraPos - p;
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

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                float d = RawDepth(uv);
            #if UNITY_REVERSED_Z
                if (d <= 1e-6) return col;
            #else
                if (d >= 1 - 1e-6) return col;
            #endif
                float3 p = WorldPos(uv, d);
                float3 n = GeoNormal(uv, p);   // нормаль из глубины: только настоящая геометрия, без рельефа текстур
                float eye = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                if (eye > _FadeDistance) return col;
                // радиус в метрах -> в экранных координатах
                float2 r = _Radius / eye * float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11) * 0.5;
                float crease = 0, ridge = 0;
                // поворот узора выборки на каждом пикселе (шум) — без полос-призраков
                float ign = frac(52.9829189 * frac(dot(uv * _ScreenParams.xy, float2(0.06711056, 0.00583715))));
                [unroll] for (int i = 0; i < 8; i++)
                {
                    float a = (i + ign) * 0.7853982;
                    float2 dir = float2(cos(a), sin(a));
                    [unroll] for (int k = 1; k <= 2; k++)
                    {
                        float2 q = uv + dir * r * (k - 0.5 + 0.5 * ign);
                        float dq = RawDepth(q);
                        float3 pq = WorldPos(q, dq);
                        float3 v = pq - p;
                        float dist = length(v);
                        if (dist > _Radius * 3.5 * k || dist < 1e-4) continue;   // другой предмет далеко позади — не стык
                        float3 nq = GeoNormal(q, pq);
                        float side = dot(n, v / dist);
                        float concave = saturate(side * 2.0);                      // соседняя поверхность встаёт перед нами
                        float convex = saturate(-side * 2.0);                      // соседняя поверхность уходит за ребро
                        float bend = saturate(1.0 - dot(n, nq));                   // нормали расходятся — излом
                        crease += concave * bend * saturate(1.0 - dist / (_Radius * 2.0 * k)) / k;   // плавный спад
                        if (k == 1) ridge += convex * bend * saturate(1.0 - dist / (_Radius * 0.45));   // ребро — узкая полоса у самой кромки
                    }
                }
                crease = saturate(crease * 0.33);
                ridge = saturate(ridge * 0.45);
                float noise = Tri(p, n, _NoiseScale);
                float fine = Tri(p, n, _NoiseScale * 6.0);
                float grime = saturate(crease * (0.45 + noise)) * _Strength;
                // тонкие трещинки вдоль стыка: узкая полоса шума высокой частоты
                float crack = (1.0 - smoothstep(0.0, 0.035, abs(fine - 0.5))) * saturate(crease * 2.0 - 0.3) * _CrackStrength;
                float fade = 1.0 - saturate((eye - _FadeDistance * 0.7) / (_FadeDistance * 0.3));
                float3 g = lerp(col.rgb, col.rgb * _GrimeColor.rgb, grime * fade);
                g *= 1.0 - crack * fade * 0.55;
                // выпуклое ребро: сбитая светлая кромка с рваным краем — ребро читается скруглённым, не идеальным
                float chip = saturate((noise - 0.25) * 1.6) * (0.6 + 0.4 * fine);
                float wear = ridge * chip * _EdgeWear * fade;
                g = lerp(g, g * _EdgeColor.rgb, wear);
                g *= 1.0 - ridge * (1.0 - chip) * 0.18 * fade;                 // в сколах — тень, как у реальной кромки
                return half4(g, col.a);
            }
            ENDHLSL
        }
    }
}
