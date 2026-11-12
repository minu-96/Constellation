// 밤하늘 배경: 그러데이션, 지평선 대기광, 은하수(텍스처), 능선 세 겹, 호수 반사.
// 카메라를 따라다니는 큰 구의 안쪽 면에 그린다(SkyBuilder).
// 배경 별은 여기서 그리지 않는다. 별 돔 메시(ConstellationStarDome.shader)가 그린다.
// 화면 전체를 덮는 셰이더라 가볍게 유지한다: 텍스처 샘플 2회, 반복문과 노이즈 계산 없음.
Shader "Constellation/Sky"
{
    Properties
    {
        _ZenithColor ("Zenith Color", Color) = (0.012, 0.02, 0.075, 1)
        _HorizonColor ("Horizon Color", Color) = (0.11, 0.09, 0.27, 1)
        _HazeColor ("Haze Color", Color) = (0.34, 0.27, 0.50, 1)
        _HillColor ("Hill Color", Color) = (0.006, 0.008, 0.025, 1)
        _GroundColor ("Ground Color", Color) = (0.004, 0.005, 0.014, 1)
        _WaterTint ("Water Tint", Color) = (0.42, 0.48, 0.70, 1)
        _MilkyWayTex ("Milky Way", 2D) = "black" {}
        _RidgeTex ("Ridge Heights", 2D) = "black" {}
        _MilkyWayStrength ("Milky Way Strength", Range(0, 3)) = 1
        _RidgeMax ("Ridge Max (y)", Range(0, 0.5)) = 0.139
        _RidgeLayers ("Ridge Layers", Range(0, 1)) = 1
        _Lake ("Lake", Range(0, 1)) = 1
        _ShoreY ("Shore Depth (y)", Range(0.05, 1)) = 0.42
        _Vignette ("Vignette", Range(0, 1)) = 0.35
        _Exposure ("Exposure", Range(0, 3)) = 1
        _Ripple0 ("Ripple 0 (dir, age)", Vector) = (0, -1, 0, -1)
        _Ripple1 ("Ripple 1 (dir, age)", Vector) = (0, -1, 0, -1)
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

            TEXTURE2D(_MilkyWayTex);
            SAMPLER(sampler_MilkyWayTex);
            TEXTURE2D(_RidgeTex);
            SAMPLER(sampler_RidgeTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _ZenithColor;
                half4 _HorizonColor;
                half4 _HazeColor;
                half4 _HillColor;
                half4 _GroundColor;
                half4 _WaterTint;
                float4 _MilkyWayTex_ST;
                float4 _RidgeTex_ST;
                half _MilkyWayStrength;
                half _RidgeMax;
                half _RidgeLayers;
                half _Lake;
                half _ShoreY;
                half _Vignette;
                half _Exposure;
                float4 _Ripple0;
                float4 _Ripple1;
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
                float3 cameraForward : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                // 돔은 회전하지 않으므로 오브젝트 공간 방향이 곧 월드 방향이다.
                output.dirOS = input.positionOS.xyz;
                // 뷰 행렬의 셋째 행은 카메라가 보는 반대 방향이다.
                output.cameraForward = -float3(UNITY_MATRIX_V[2].x, UNITY_MATRIX_V[2].y, UNITY_MATRIX_V[2].z);
                return output;
            }

            float hash12(float2 p)
            {
                float3 p3 = frac(float3(p.x, p.y, p.x) * 0.1031);
                p3 += dot(p3, p3.yzx + 33.33);
                return frac((p3.x + p3.y) * p3.z);
            }

            // 손이 가리킨 자리에서 퍼지는 물결 하나. ripple.xyz는 물결의 중심 방향, w는 시작한 뒤 흐른 시간(초, 음수면 없음).
            float rippleAt(float3 dir, float4 ripple)
            {
                float age = ripple.w;
                float distanceFromCenter = sqrt(max(2.0 - 2.0 * dot(dir, ripple.xyz), 0.0));
                float x = (distanceFromCenter - age * 0.12) * 55.0;
                float envelope = exp(-x * x * 0.08) * saturate(1.0 - age / 3.0) * step(0.0, age);
                return sin(x) * envelope;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 dir = normalize(input.dirOS);
                float azimuth = atan2(dir.x, dir.z);
                float u = azimuth * 0.15915494 + 0.5;

                // 지평선 아래는 호수다. 위쪽 하늘을 거울처럼 뒤집어 다시 계산하고 잔물결로 살짝 흔든다.
                float depth = -dir.y;
                float water = step(0.0, depth) * step(0.5, _Lake);
                float y = dir.y;
                float handRipple = 0.0;
                if (water > 0.5)
                {
                    handRipple = rippleAt(dir, _Ripple0) + rippleAt(dir, _Ripple1);
                    float ripple = sin(azimuth * 61.0 + _Time.y * 0.7 + depth * 40.0) * 0.0025
                                 + sin(azimuth * 23.0 - _Time.y * 0.45) * 0.003;
                    y = max(depth + (ripple + handRipple * 0.012) * saturate(depth * 8.0), 0.0);
                    // 흔든 뒤 길이를 다시 1로 맞춘다.
                    float horizontal = sqrt(saturate(1.0 - dir.y * dir.y));
                    y = y / sqrt(horizontal * horizontal + y * y);
                }

                // 하늘 그러데이션과 지평선에 깔리는 대기광
                float up = saturate(y);
                half3 color = lerp(_HorizonColor.rgb, _ZenithColor.rgb, sqrt(up));
                color += _HazeColor.rgb * (exp(-up * 8.0) * 0.55);

                // 은하수. 지평선 가까이에서는 대기에 묻혀 흐려진다.
                half3 milkyWay = SAMPLE_TEXTURE2D(_MilkyWayTex, sampler_MilkyWayTex, float2(u, y * 0.5 + 0.5)).rgb;
                color += milkyWay * (_MilkyWayStrength * smoothstep(0.0, 0.25, y));

                // 능선 세 겹. 멀수록 안개 색에 가깝고, 가까울수록 어둡다.
                half3 ridge = SAMPLE_TEXTURE2D(_RidgeTex, sampler_RidgeTex, float2(u, 0.5)).rgb * _RidgeMax;
                const float edge = 0.0025;
                float farMask = (1.0 - smoothstep(ridge.r - edge, ridge.r + edge, y)) * _RidgeLayers;
                float midMask = (1.0 - smoothstep(ridge.g - edge, ridge.g + edge, y)) * _RidgeLayers;
                float nearMask = 1.0 - smoothstep(ridge.b - edge, ridge.b + edge, y);
                color = lerp(color, lerp(_HillColor.rgb, _HazeColor.rgb, 0.45), farMask);
                color = lerp(color, lerp(_HillColor.rgb, _HazeColor.rgb, 0.21), midMask);
                color = lerp(color, _HillColor.rgb, nearMask);

                if (water > 0.5)
                {
                    // 물은 하늘보다 어둡고, 발밑으로 올수록 더 어두워진다. 가장 가까운 곳은 물가의 땅이다.
                    color *= _WaterTint.rgb;
                    // 손이 일으킨 물결의 마루에 옅은 빛이 맺힌다.
                    color += _HazeColor.rgb * (max(handRipple, 0.0) * 0.08);
                    color = lerp(color, _GroundColor.rgb, smoothstep(0.2, 0.8, depth));
                    float shore = _ShoreY + 0.03 * sin(azimuth * 3.0 + 1.0) + 0.012 * sin(azimuth * 7.0 + 4.0);
                    color = lerp(color, _GroundColor.rgb, smoothstep(shore - 0.01, shore + 0.01, depth));
                }

                // 시선 가장자리를 살짝 어둡게(비네트). 두 눈이 같은 값을 쓰도록 머리 방향 기준으로 계산한다.
                float facing = dot(dir, normalize(input.cameraForward));
                color *= 1.0 - _Vignette * (1.0 - smoothstep(0.45, 0.92, facing));
                color *= _Exposure;

                // 어두운 그러데이션에 띠가 생기지 않게 아주 약한 잡음을 섞는다.
                half3 encoded = sqrt(max(color, 0.0));
                encoded += (hash12(input.positionCS.xy) - 0.5) * (1.5 / 255.0);
                color = encoded * encoded;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
