// 빛나는 점(별, 손끝, 아이콘). 항상 카메라를 보는 빌보드이고 가산 혼합으로 그린다.
// _UseTexture가 1이면 텍스처 알파를 모양으로 쓴다(키워드 아이콘).
Shader "Constellation/Glow"
{
    Properties
    {
        _Color ("Color", Color) = (1, 1, 1, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1
        _Core ("Core Size", Range(0.01, 1)) = 0.22
        _MainTex ("Shape Texture", 2D) = "white" {}
        _UseTexture ("Use Texture", Range(0, 1)) = 0
        _Billboard ("Billboard", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Glow"
            Blend One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _Core;
                half _UseTexture;
                half _Billboard;
                float4 _MainTex_ST;
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

                if (_Billboard > 0.5)
                {
                    // 오브젝트 중심을 뷰 공간으로 옮긴 뒤, 화면과 나란하게 사각형을 편다.
                    float3 centerWS = TransformObjectToWorld(float3(0.0, 0.0, 0.0));
                    float3 scale = float3(
                        length(float3(UNITY_MATRIX_M._m00, UNITY_MATRIX_M._m10, UNITY_MATRIX_M._m20)),
                        length(float3(UNITY_MATRIX_M._m01, UNITY_MATRIX_M._m11, UNITY_MATRIX_M._m21)),
                        1.0);
                    float3 positionVS = TransformWorldToView(centerWS);
                    positionVS.xy += input.positionOS.xy * scale.xy;
                    output.positionCS = TransformWViewToHClip(positionVS);
                }
                else
                {
                    output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                }
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 centered = input.uv * 2.0 - 1.0;
                float r = length(centered);
                // 밝은 중심 + 부드럽게 퍼지는 빛
                float core = saturate(1.0 - r / max(_Core, 0.001));
                float halo = saturate(1.0 - r);
                float radial = core * core + halo * halo * 0.55;
                float shape = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv).a;
                float alpha = lerp(radial, shape, _UseTexture);
                half3 color = _Color.rgb * input.color.rgb * (alpha * _Intensity * _Color.a * input.color.a);
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
