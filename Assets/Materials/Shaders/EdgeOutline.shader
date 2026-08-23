Shader "Custom/URP/EdgeOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _Thickness ("Thickness (pixels)", Range(0.5, 4)) = 1
        _DepthSensitivity ("Depth Sensitivity", Range(0, 10)) = 1.5
        _NormalSensitivity ("Normal Sensitivity", Range(0, 5)) = 1.0
        _Threshold ("Edge Threshold", Range(0.01, 1)) = 0.2
        _NormalFadeDistance ("Normal Fade Distance", Range(1, 60)) = 8
        _DepthAbsSensitivity ("Combo Depth Sensitivity", Range(0, 4)) = 0.6
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZWrite Off
        Cull Off
        ZTest Always

        Pass
        {
            Name "EdgeOutline"

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            float4 _OutlineColor;
            float _Thickness;
            float _DepthSensitivity;
            float _NormalSensitivity;
            float _Threshold;
            float _NormalFadeDistance;
            float _DepthAbsSensitivity;

            float _OutlineGlobalFade;
            float _OutlinePlayerDepth;
            float _OutlineFadeWindow;

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texel = _ScreenSize.zw * _Thickness;

                float2 uvA = uv + float2(-texel.x, -texel.y);
                float2 uvB = uv + float2( texel.x,  texel.y);
                float2 uvC = uv + float2( texel.x, -texel.y);
                float2 uvD = uv + float2(-texel.x,  texel.y);

                float centre = EyeDepth(uv);
                float dA = EyeDepth(uvA);
                float dB = EyeDepth(uvB);
                float dC = EyeDepth(uvC);
                float dD = EyeDepth(uvD);
                float dAB = dA - dB;
                float dCD = dC - dD;
                float depthAbs = sqrt(dAB * dAB + dCD * dCD);

                float nearTap = min(min(dA, dB), min(dC, dD));
                float farMask = smoothstep(0.0, 0.15, centre - nearTap);
                float depthEdge = depthAbs / max(nearTap, 0.1);
                depthEdge *= _DepthSensitivity * farMask;

                float3 nAB = SampleSceneNormals(uvA) - SampleSceneNormals(uvB);
                float3 nCD = SampleSceneNormals(uvC) - SampleSceneNormals(uvD);
                float normalEdge = sqrt(dot(nAB, nAB) + dot(nCD, nCD));
                normalEdge *= _NormalSensitivity;

                normalEdge *= saturate(_NormalFadeDistance / max(centre, 0.01));

                float stepMag = max(abs(dA + dB - 2.0 * centre), abs(dC + dD - 2.0 * centre));
                float stepEdge = stepMag * _DepthAbsSensitivity * farMask;

                float rawCentre = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawCentre, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float facing = saturate(dot(SampleSceneNormals(uv), viewDir));
                stepEdge *= smoothstep(0.15, 0.4, facing);

                float edge = smoothstep(_Threshold, _Threshold * 2.0,
                    max(max(depthEdge, normalEdge), stepEdge));

                #if UNITY_REVERSED_Z
                float skyMask = step(0.000001, rawCentre);
                #else
                float skyMask = step(rawCentre, 0.999999);
                #endif
                edge *= skyMask;

                float globalFade = _OutlineGlobalFade <= 0.001 ? 1.0 : saturate(_OutlineGlobalFade);
                float window = max(_OutlineFadeWindow, 0.001);
                float playerProximity = 1.0 - saturate(abs(nearTap - _OutlinePlayerDepth) / window);
                edge *= lerp(1.0, globalFade, playerProximity);

                half4 scene = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return lerp(scene, _OutlineColor, edge * _OutlineColor.a);
            }

            ENDHLSL
        }
    }
}

