Shader "MyPortal/PortalSurface"
{
    // 门面 Shader：把镜像相机渲染出来的 RenderTexture 按"屏幕空间"采样显示，
    // 依次叠加：屏幕空间扭曲 -> 模糊 -> 开关门溶解 -> 边缘辉光。
    //
    // 为什么可以直接用屏幕像素采样：
    // 镜像相机的投影参数与玩家相机完全一致，且它的位姿是玩家位姿经 PortalMapping 映射后的结果。
    // 设玩家屏幕上的像素 p 对应世界方向 d，那么该方向映射后是 R·d；
    // 因为镜像相机的朝向 = 玩家相机朝向 × R，所以 R·d 在镜像相机局部空间里的分量与 d 在玩家相机里的完全相同，
    // 投影到屏幕后仍然是同一个像素 p。于是门面覆盖的每个像素直接取 RT 的同像素即为正确结果。
    // 反过来说，一旦镜像相机的 FOV / 宽高比与玩家相机不一致，这个等式就不成立，画面会错位。
    //
    // 噪声用的是程序化 value noise（不依赖任何贴图资源），
    Properties
    {
        _PortalTexture("Portal Texture", 2D) = "black" {}

        [Header(Distortion)]
        _NoiseTiling("Noise Tiling", Float) = 6
        _NoiseSpeed("Noise Speed", Float) = 0.4
        _DistortionStrength("Distortion Strength", Range(0, 0.1)) = 0.012
        _DistortionFadeDistance("Distortion Fade Distance", Float) = 12

        [Header(Blur)]
        _BlurStrength("Blur Strength", Range(0, 0.02)) = 0.002

        [Header(Edge Glow)]
        [HDR] _GlowColor("Glow Color", Color) = (0.4, 0.8, 1, 1)
        _GlowIntensity("Glow Intensity", Float) = 1.5
        _GlowPower("Glow Power", Float) = 6
        _GlowNoiseStrength("Glow Noise Strength", Range(0, 1)) = 0.6
        _GlowNoiseSpeed("Glow Noise Speed", Float) = 1.2

        [Header(Dissolve)]
        _DissolveNoise("Dissolve Noise", 2D) = "gray" {}
        _DissolveTiling("Dissolve Tiling", Float) = 1
        _OpenAmount("Open Amount", Range(0, 1)) = 1
        _DissolveEdgeWidth("Dissolve Edge Width", Range(0.001, 0.5)) = 0.08
        [HDR] _DissolveEdgeColor("Dissolve Edge Color", Color) = (0.6, 1, 1, 1)
        _DissolveEdgeIntensity("Dissolve Edge Intensity", Float) = 2
        [HDR] _ClosedColor("Closed Color", Color) = (0.03, 0.06, 0.09, 1)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "PortalSurface"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_PortalTexture);
            SAMPLER(sampler_PortalTexture);
            TEXTURE2D(_DissolveNoise);
            SAMPLER(sampler_DissolveNoise);

            CBUFFER_START(UnityPerMaterial)
                float4 _PortalTexture_ST;
                float4 _DissolveNoise_ST;
                float _NoiseTiling;
                float _NoiseSpeed;
                float _DistortionStrength;
                float _DistortionFadeDistance;
                float _BlurStrength;
                float4 _GlowColor;
                float _GlowIntensity;
                float _GlowPower;
                float _GlowNoiseStrength;
                float _GlowNoiseSpeed;
                float _DissolveTiling;
                float _OpenAmount;
                float _DissolveEdgeWidth;
                float4 _DissolveEdgeColor;
                float _DissolveEdgeIntensity;
                float4 _ClosedColor;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos : TEXCOORD0;
                float2 localPosition : TEXCOORD1; // 门面自身的二维坐标，单位面片为 [-0.5, 0.5]
                float viewDepth : TEXCOORD2;      // 与镜头的距离（米）
            };

            // ---------- 程序化噪声 ----------
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f); // smoothstep 插值，避免格子感

                float a = Hash21(i);
                float b = Hash21(i + float2(1.0, 0.0));
                float c = Hash21(i + float2(0.0, 1.0));
                float d = Hash21(i + float2(1.0, 1.0));

                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            // ---------- 模糊：中心 + 8 邻居（近似高斯权重）----------
            half3 SamplePortalBlurred(float2 uv, float radius)
            {
                half3 center = SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv).rgb;

                if (radius < 0.0005)
                    return center;

                float2 d = radius;

                half3 color = center * 0.25;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + float2(d.x, 0.0)).rgb * 0.125;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + float2(-d.x, 0.0)).rgb * 0.125;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + float2(0.0, d.y)).rgb * 0.125;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + float2(0.0, -d.y)).rgb * 0.125;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + d).rgb * 0.0625;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv - d).rgb * 0.0625;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + float2(d.x, -d.y)).rgb * 0.0625;
                color += SAMPLE_TEXTURE2D(_PortalTexture, sampler_PortalTexture, uv + float2(-d.x, d.y)).rgb * 0.0625;

                return color;
            }

            Varyings vert(Attributes input)
            {
                Varyings output;

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);

                // ComputeScreenPos 内部带了 _ProjectionParams.x 的修正，
                // 这正是"相机渲染到 RenderTexture 时投影被翻转"的补偿，因此不需要自己翻 V。
                output.screenPos = ComputeScreenPos(output.positionCS);

                output.localPosition = input.positionOS.xy;
                output.viewDepth = -TransformWorldToView(positionWS).z;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float2 screenUV = input.screenPos.xy / input.screenPos.w;

                // ---------- 1. 屏幕空间扭曲 ----------
                // 噪声坐标用门面局部坐标，噪声"贴"在门上而不是贴在屏幕上
                float2 noiseUV = input.localPosition * _NoiseTiling;
                float flow = _Time.y * _NoiseSpeed;

                float2 distortion = float2(
                    ValueNoise(noiseUV + float2(flow, flow * 0.7)),
                    ValueNoise(noiseUV * 1.37 + float2(flow * 0.5, -flow))) * 2.0 - 1.0;

                // 离镜头越远扰动越小，避免远处门面抖得厉害
                float distanceFade = saturate(input.viewDepth / max(_DistortionFadeDistance, 0.001));
                float2 uv = screenUV + distortion * (_DistortionStrength * (1.0 - distanceFade));

                // ---------- 2. 模糊 ----------
                half3 viewColor = SamplePortalBlurred(uv, _BlurStrength);

                // ---------- 3. 开关门溶解 ----------
                // 阈值从"高于噪声最大值"扫到"低于噪声最小值"：
                //   _OpenAmount = 0 -> 整块都是关闭面板；= 1 -> 全部露出门内画面。
                // 过渡带宽度由 _DissolveEdgeWidth 决定，噪声贴图的灰度决定溶解的形状；
                float dissolveNoise = SAMPLE_TEXTURE2D(_DissolveNoise, sampler_DissolveNoise, input.localPosition * _DissolveTiling + 0.5).r;
                float width = max(_DissolveEdgeWidth, 0.0001);
                float threshold = lerp(1.0 + width, -width, _OpenAmount);
                float openMask = saturate((dissolveNoise - threshold) / width);

                // 关闭状态
                half3 closedColor = _ClosedColor.rgb * (0.7 + 0.6 * dissolveNoise);
                half3 color = lerp(closedColor, viewColor, openMask);

                // 溶解前沿的烧灼边缘：openMask 在 0.5 附近最亮，两端收敛为 0，
                float burnEdge = 1.0 - abs(openMask * 2.0 - 1.0);
                color += _DissolveEdgeColor.rgb * (_DissolveEdgeIntensity * burnEdge);

                // ---------- 4. 边缘辉光 ----------
                // 用门面自身坐标算"到边缘的距离"：中心 0，边缘 1
                float edge = saturate(max(abs(input.localPosition.x), abs(input.localPosition.y)) * 2.0);

                float flicker = ValueNoise(input.localPosition * _NoiseTiling * 0.5 + float2(0.0, _Time.y * _GlowNoiseSpeed));
                float glow = pow(edge, _GlowPower) * _GlowIntensity * lerp(1.0, flicker, _GlowNoiseStrength);

                color += _GlowColor.rgb * glow;

                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
