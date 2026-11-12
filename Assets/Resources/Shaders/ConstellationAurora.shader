// 오로라 장막(모험 키워드의 하늘). 먼 하늘에 세운 띠 모양 메시에 그린다(KeywordSky.cs).
// uv.x: 장막을 따라가는 방향(0~1), uv.y: 아래(0)에서 위(1). 가산 혼합이라 하늘 위에 빛만 더한다.
Shader "Constellation/Aurora"
{
    Properties
    {
        _LowColor ("Low Color", Color) = (0.2, 1.0, 0.55, 1)
        _HighColor ("High Color", Color) = (0.6, 0.3, 1.0, 1)
        _Intensity ("Intensity", Range(0, 3)) = 0.5
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Aurora"
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LowColor;
                half4 _HighColor;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float x = input.uv.x;
                float v = input.uv.y;
                float t = _Time.y;

                // 장막이 천천히 일렁이며 밝아졌다 어두워지는 큰 물결
                float band = 0.5 + 0.5 * sin(x * 9.0 + t * 0.25 + sin(x * 3.0 - t * 0.15) * 2.0);
                // 세로로 선 빛줄기
                float rays = 0.55 + 0.45 * sin(x * 90.0 + sin(x * 7.0 + t * 0.5) * 3.0 + t * 0.8);
                // 아래쪽 가장자리는 또렷하고 위로 갈수록 흐려진다.
                float lift = 1.0 - v;
                float shape = smoothstep(0.0, 0.12, v) * lift * sqrt(lift);
                // 장막의 양 끝은 사라진다.
                float ends = smoothstep(0.0, 0.12, x) * smoothstep(1.0, 0.88, x);

                half3 color = lerp(_LowColor.rgb, _HighColor.rgb, smoothstep(0.25, 0.9, v));
                float strength = shape * (0.35 + 0.65 * band) * rays * ends * _Intensity;
                return half4(color * strength, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
