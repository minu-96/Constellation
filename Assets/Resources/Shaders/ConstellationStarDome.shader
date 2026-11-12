// 배경 별 수천 개를 메시 하나로 그린다(StarDome.cs). 별마다 사각형 하나이고 항상 카메라를 본다.
// 정점 데이터: POSITION = 별의 방향(단위 벡터), TEXCOORD0 = 사각형 모서리(-1~1),
//             TEXCOORD1 = (각크기, 반짝임 속도, 밝기, 위상), COLOR = 별 색
// _Reflect가 1이면 위아래를 뒤집어 호수에 비친 별을 그린다.
Shader "Constellation/StarDome"
{
    Properties
    {
        _Brightness ("Brightness", Range(0, 4)) = 1
        _Reflect ("Reflect", Range(0, 1)) = 0
        _ShoreY ("Shore Depth (y)", Range(0.05, 1)) = 0.42
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Background+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Stars"
            Blend One One
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half _Brightness;
                half _Reflect;
                half _ShoreY;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 corner : TEXCOORD0;
                float4 data : TEXCOORD1;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 corner : TEXCOORD0;
                half3 color : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 dir = input.positionOS.xyz;
                float height = dir.y;
                float2 stretch = float2(1.0, 1.0);
                // 지평선 가까운 별은 대기에 가려 어둡고 더 많이 반짝인다.
                float brightness = input.data.z * (smoothstep(0.0, 0.3, height) * 0.85 + 0.15);
                float twinkleAmount = lerp(0.35, 0.1, saturate(height * 3.0));
                float twinkle = 1.0 + twinkleAmount * sin(_Time.y * input.data.y + input.data.w * 6.2831853);

                if (_Reflect > 0.5)
                {
                    // 물에 비친 별: 위아래를 뒤집고, 세로로 길게 늘이고, 어둡게 한다. 물가의 땅 쪽에서는 사라진다.
                    dir.y = -dir.y;
                    stretch = float2(1.0, 3.0);
                    brightness *= 0.35 * (1.0 - smoothstep(_ShoreY - 0.1, _ShoreY - 0.02, height));
                    twinkle = 0.75 + 0.25 * sin(_Time.y * 2.3 + input.data.w * 40.0);
                }

                // 별의 중심을 뷰 공간으로 옮긴 뒤 화면과 나란하게 사각형을 편다. 크기는 각도로 정해진다.
                float3 centerVS = TransformWorldToView(TransformObjectToWorld(dir));
                float size = input.data.x * length(centerVS);
                centerVS.xy += input.corner * size * stretch;
                output.positionCS = TransformWViewToHClip(centerVS);
                output.corner = input.corner;
                output.color = input.color.rgb * (brightness * twinkle * _Brightness);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // 밝은 중심과 부드럽게 번지는 가장자리
                float falloff = saturate(1.0 - length(input.corner));
                float glow = falloff * falloff * falloff + 0.25 * falloff * falloff;
                return half4(input.color * glow, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
