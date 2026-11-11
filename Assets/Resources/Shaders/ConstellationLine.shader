// 별을 잇는 선(LineRenderer). 가운데가 밝고 가장자리로 부드럽게 흐려진다.
Shader "Constellation/Line"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.9, 0.62, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.4
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Line"
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                // uv.y는 선의 폭 방향(0~1)
                float across = 1.0 - abs(input.uv.y * 2.0 - 1.0);
                float core = smoothstep(0.55, 1.0, across);
                float halo = across * across * 0.45;
                float alpha = (core + halo) * _Intensity * _Color.a * input.color.a;
                return half4(_Color.rgb * input.color.rgb * alpha, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
