Shader "MyPortal/PortalParticle"
{
    // 粒子用的加法混合 Shader。
    // 发光形状（软圆点）是程序化算出来的径向衰减，不需要任何 sprite 贴图，
    // 颜色取自粒子自身的顶点色（Start Color / Color over Lifetime），
    // 所以一套材质可以同时给吸入、爆发、常驻流动三层粒子用。
    //
    // 注意：Properties 块里不能写注释 —— ShaderLab 会把 // 之后的内容当成非法 token，
    // 报 "Parse error: syntax error, unexpected $undefined, expecting TVAL_ID or TVAL_VARREF"。
    Properties
    {
        _Intensity("Intensity", Float) = 2.5
        _Softness("Softness", Range(0.5, 8)) = 2.5
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "PortalParticle"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float _Intensity;
                float _Softness;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 程序化圆点：UV 中心为 1、边缘为 0 的平滑衰减
                float2 offset = input.uv - 0.5;
                float radius = saturate(length(offset) * 2.0);
                float falloff = pow(1.0 - radius, _Softness);

                // 顶点色乘 _Intensity 后可以超过 1，配合 Bloom 就是发光粒子
                half3 color = input.color.rgb * _Intensity;
                half alpha = input.color.a * falloff;

                return half4(color, alpha);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
