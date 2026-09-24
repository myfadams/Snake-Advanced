Shader "Universal Render Pipeline/Custom/SmoothDissolve"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        [MainTexture] _MainTex("Main Texture", 2D) = "white" {}
        _Dissovle_Texture("Dissolve Noise Texture", 2D) = "white" {}
        _Cutoff("Dissolve Cutoff (0 = Solid, 1 = Gone)", Range(0.0, 1.0)) = 0.0
        [HDR] _Edge_Color("Edge Glow Color", Color) = (2.0, 1.2, 0.2, 1.0)
        _Edge_Width("Edge Glow Width", Range(0.001, 0.4)) = 0.08
        _Glow_Thickness("Glow Multiplier", Range(0.1, 10.0)) = 2.5
        _NoiseScale("Noise Tiling", Float) = 1.0

        // Additional properties for script tinting and compatibility
        [HideInInspector] _Color("Legacy Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _EmissionColor("Emission Color", Color) = (0, 0, 0, 1)
        [HideInInspector] _Color_Glow("Glow Color", Color) = (2, 1.2, 0.2, 1)
        [HideInInspector] _ColorEdge("Edge Color", Color) = (2, 1.2, 0.2, 1)
        [HideInInspector] _Coloredges("Coloredges", Color) = (2, 1.2, 0.2, 1)
        [HideInInspector] _Main_Color("Main Color", Color) = (1, 1, 1, 1)
        [HideInInspector] _cutoff("Lowercase Cutoff", Range(0.0, 1.0)) = 0.0
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "RenderPipeline" = "UniversalPipeline"
        }
        LOD 100
        Cull Off

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_Dissovle_Texture);
            SAMPLER(sampler_Dissovle_Texture);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Color;
                float4 _Edge_Color;
                float4 _Color_Glow;
                float4 _ColorEdge;
                float4 _Coloredges;
                float4 _Main_Color;
                float4 _MainTex_ST;
                float4 _Dissovle_Texture_ST;
                float _Cutoff;
                float _cutoff;
                float _Edge_Width;
                float _Glow_Thickness;
                float _NoiseScale;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float activeCutoff = max(_Cutoff, _cutoff);

                float2 noiseUV = input.uv * max(_NoiseScale, 0.01) * _Dissovle_Texture_ST.xy + _Dissovle_Texture_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_Dissovle_Texture, sampler_Dissovle_Texture, noiseUV).r;

                // When activeCutoff = 0, clipVal is noise in [0, 1] -> no pixels clipped (fully solid)
                // When activeCutoff = 1, clipVal is noise - 1 <= 0 -> all pixels clipped (fully vanished)
                half clipVal = noise - activeCutoff;
                clip(clipVal);

                // Base albedo
                half4 mainTex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                half4 tint = _BaseColor;
                if (dot(_Main_Color.rgb, _Main_Color.rgb) > 0.01)
                {
                    tint *= _Main_Color;
                }
                half3 col = mainTex.rgb * tint.rgb;

                // Burning edge glow
                half edgeWidth = max(_Edge_Width, 0.001);
                half edgeFactor = 1.0 - saturate(clipVal / edgeWidth);

                // Determine glowing edge color
                half3 glowCol = _Edge_Color.rgb;
                if (dot(_Color_Glow.rgb, _Color_Glow.rgb) > 0.01)
                {
                    glowCol = _Color_Glow.rgb;
                }
                else if (dot(_ColorEdge.rgb, _ColorEdge.rgb) > 0.01)
                {
                    glowCol = _ColorEdge.rgb;
                }
                else if (dot(_Coloredges.rgb, _Coloredges.rgb) > 0.01)
                {
                    glowCol = _Coloredges.rgb;
                }

                half3 emission = glowCol * (pow(edgeFactor, 1.5) * _Glow_Thickness);

                return half4(col + emission, 1.0);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex ShadowPassVertex
            #pragma fragment ShadowPassFragment
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_Dissovle_Texture);
            SAMPLER(sampler_Dissovle_Texture);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Color;
                float4 _Edge_Color;
                float4 _Color_Glow;
                float4 _ColorEdge;
                float4 _Coloredges;
                float4 _Main_Color;
                float4 _MainTex_ST;
                float4 _Dissovle_Texture_ST;
                float _Cutoff;
                float _cutoff;
                float _Edge_Width;
                float _Glow_Thickness;
                float _NoiseScale;
            CBUFFER_END

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowPassVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);

            #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
            #else
                float3 lightDirectionWS = _LightDirection;
            #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                positionCS = ApplyShadowClamping(positionCS);
                output.positionCS = positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 ShadowPassFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float activeCutoff = max(_Cutoff, _cutoff);
                float2 noiseUV = input.uv * max(_NoiseScale, 0.01) * _Dissovle_Texture_ST.xy + _Dissovle_Texture_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_Dissovle_Texture, sampler_Dissovle_Texture, noiseUV).r;

                clip(noise - activeCutoff);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma target 2.0
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            TEXTURE2D(_Dissovle_Texture);
            SAMPLER(sampler_Dissovle_Texture);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _Color;
                float4 _Edge_Color;
                float4 _Color_Glow;
                float4 _ColorEdge;
                float4 _Coloredges;
                float4 _Main_Color;
                float4 _MainTex_ST;
                float4 _Dissovle_Texture_ST;
                float _Cutoff;
                float _cutoff;
                float _Edge_Width;
                float _Glow_Thickness;
                float _NoiseScale;
            CBUFFER_END

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                return output;
            }

            half4 DepthOnlyFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float activeCutoff = max(_Cutoff, _cutoff);
                float2 noiseUV = input.uv * max(_NoiseScale, 0.01) * _Dissovle_Texture_ST.xy + _Dissovle_Texture_ST.zw;
                half noise = SAMPLE_TEXTURE2D(_Dissovle_Texture, sampler_Dissovle_Texture, noiseUV).r;

                clip(noise - activeCutoff);
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
