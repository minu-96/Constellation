// 밤하늘: 남보라에서 진한 남색으로 이어지는 그러데이션, 잔별, 은하수 띠, 언덕 실루엣.
// 카메라를 따라다니는 큰 구의 안쪽 면에 그린다(SkyBuilder).
Shader "Constellation/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.012, 0.02, 0.075, 1)
        _HorizonColor ("Horizon Color", Color) = (0.16, 0.11, 0.36, 1)
        _HillColor ("Hill Color", Color) = (0.006, 0.008, 0.025, 1)
        _StarDensity ("Star Density", Range(0, 1)) = 0.5
        _StarBrightness ("Star Brightness", Range(0, 3)) = 1.1
        _HillHeight ("Hill Height", Range(-0.3, 0.3)) = -0.06
    }
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Sky"
            Cull Front
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _HillColor;
                half _StarDensity;
                half _StarBrightness;
                half _HillHeight;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.dirOS = input.positionOS.xyz;
                return output;
            }

            float hash13(float3 p)
            {
                p = frac(p * 0.1031);
                p += dot(p, p.zyx + 31.32);
                return frac((p.x + p.y) * p.z);
            }

            float3 hash33(float3 p)
            {
                p = frac(p * float3(0.1031, 0.1030, 0.0973));
                p += dot(p, p.yxz + 33.33);
                return frac((p.xxy + p.yxx) * p.zyx);
            }

            // 방향 벡터를 격자로 나눠 칸마다 별 하나를 찍는다.
            float starLayer(float3 dir, float scale, float density, float timeScale)
            {
                float3 p = dir * scale;
                float3 cell = floor(p);
                float3 local = frac(p) - 0.5;
                float3 rnd = hash33(cell);
                float present = step(1.0 - density, rnd.x);
                float3 offset = (hash33(cell + 17.0) - 0.5) * 0.6;
                float d = length(local - offset);
                float size = lerp(0.04, 0.11, rnd.y);
                float core = saturate(1.0 - d / size);
                float twinkle = 0.65 + 0.35 * sin(_Time.y * timeScale * (0.6 + rnd.z * 1.8) + rnd.y * 6.2831);
                return present * core * core * twinkle * lerp(0.35, 1.0, rnd.z);
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.dirOS);

                // 하늘 그러데이션
                float up = saturate(dir.y);
                float t = pow(up, 0.55);
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, t);

                // 은하수 띠: 기울어진 축에 수직인 큰 원 주변을 살짝 밝힌다.
                float3 axis = normalize(float3(0.35, 0.5, 0.79));
                float bandDistance = dot(dir, axis) * 3.2;
                float band = exp(-bandDistance * bandDistance);
                color += band * half3(0.035, 0.03, 0.07);

                // 잔별 두 겹
                float stars = starLayer(dir, 55.0, _StarDensity * 0.55, 1.3);
                stars += starLayer(dir, 110.0, _StarDensity * (0.35 + band * 0.4), 2.1) * 0.6;
                color += stars * _StarBrightness * half3(0.85, 0.9, 1.0) * smoothstep(-0.02, 0.2, dir.y + 0.05);

                // 언덕 실루엣: 방위각에 따라 높이가 달라지는 능선
                float az = atan2(dir.x, dir.z);
                float ridge = _HillHeight
                    + 0.045 * sin(az * 2.0 + 0.7)
                    + 0.028 * sin(az * 5.0 + 2.1)
                    + 0.012 * sin(az * 11.0 + 4.3);
                float elevation = dir.y;
                float hill = 1.0 - smoothstep(ridge - 0.004, ridge + 0.004, elevation);
                // 능선 바로 위에 옅은 빛을 둔다.
                float glow = exp(-max(elevation - ridge, 0.0) * 9.0) * 0.35;
                color += _HorizonColor.rgb * glow;
                color = lerp(color, _HillColor.rgb, hill);

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
