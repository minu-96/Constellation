// 별을 잇는 선(LineRenderer). 가운데가 밝고 가장자리로 부드럽게 흐려진다.
// _FlowStrength를 올리면 선을 따라 빛이 흐르고, _PulseStrength를 올리면 _PulsePosition(0~1) 자리에 밝은 빛 덩어리가 생긴다.
// 둘 다 기본은 0이라 손·미리보기 선 등 다른 선은 영향을 받지 않는다.
Shader "Constellation/Line"
{
    Properties
    {
        _Color ("Color", Color) = (1, 0.9, 0.62, 1)
        _Intensity ("Intensity", Range(0, 8)) = 1.4
        _FlowStrength ("Flow Strength", Range(0, 3)) = 0
        _FlowDensity ("Flow Bands", Range(0, 64)) = 4
        _FlowSpeed ("Flow Speed", Range(-4, 4)) = 0.6
        _PulsePosition ("Pulse Position", Range(-1, 2)) = 0
        _PulseStrength ("Pulse Strength", Range(0, 8)) = 0
        _PulseWidth ("Pulse Width", Range(0.005, 1)) = 0.08
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
                half _FlowStrength;
                half _FlowDensity;
                half _FlowSpeed;
                half _PulsePosition;
                half _PulseStrength;
                half _PulseWidth;
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
                // uv.x는 선의 길이 방향(0~1). 흐르는 빛 띠와 지나가는 빛 덩어리를 더한다.
                float wave = 0.5 + 0.5 * sin((input.uv.x * _FlowDensity - _Time.y * _FlowSpeed) * 6.2831853);
                float flow = 1.0 + _FlowStrength * wave * wave * wave * wave;
                float pulseDistance = (input.uv.x - _PulsePosition) / _PulseWidth;
                float pulse = _PulseStrength * exp(-pulseDistance * pulseDistance);
                float alpha = (core + halo) * (_Intensity * flow + pulse * (core + 0.4)) * _Color.a * input.color.a;
                return half4(_Color.rgb * input.color.rgb * alpha, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
