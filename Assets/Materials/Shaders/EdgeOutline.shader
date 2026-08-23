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

            // Core.hlsl FIRST: Blit.hlsl is written against the SRP core API and uses its
            // macros (TEXTURE2D_X and friends) without including them itself - without
            // this line the whole pass fails to compile and the outline never draws.
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            // Blit.hlsl supplies the fullscreen triangle Vert, _BlitTexture and the
            // Varyings with texcoord - the standard skeleton for a fullscreen pass.
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
            // GLOBALS, driven by Polish via Shader.SetGlobalFloat - not material properties.
            float _OutlineGlobalFade;
            float _OutlinePlayerDepth;  // the player's view depth - the fade is scoped HERE
            float _OutlineFadeWindow;   // +/- metres around it that the fade covers

            float EyeDepth(float2 uv)
            {
                return LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texel = _ScreenSize.zw * _Thickness;

                // Roberts cross: two diagonal difference pairs - cheap, clean at 1px.
                float2 uvA = uv + float2(-texel.x, -texel.y);
                float2 uvB = uv + float2( texel.x,  texel.y);
                float2 uvC = uv + float2( texel.x, -texel.y);
                float2 uvD = uv + float2(-texel.x,  texel.y);

                // DEPTH edges, relative to the centre depth so far geometry is not one
                // solid outline: a 0.5m step matters up close and not across the level.
                float centre = EyeDepth(uv);
                float dA = EyeDepth(uvA);
                float dB = EyeDepth(uvB);
                float dC = EyeDepth(uvC);
                float dD = EyeDepth(uvD);
                float dAB = dA - dB;
                float dCD = dC - dD;
                float depthAbs = sqrt(dAB * dAB + dCD * dCD);

                // FAR-SIDE silhouettes. Normalizing by the CENTRE depth made the rim pass
                // threshold only on the near side of a step - the line drew INSET, on the
                // object itself: a fat crescent eating small spheres, and a turret rim
                // that came and went with the angle. Normalized by the NEAREST tap and
                // masked to pixels standing BEHIND it, the line draws just outside the
                // object instead - it can never cover the model, and it exists from every
                // viewing angle.
                float nearTap = min(min(dA, dB), min(dC, dD));
                float farMask = smoothstep(0.0, 0.15, centre - nearTap);
                float depthEdge = depthAbs / max(nearTap, 0.1);
                depthEdge *= _DepthSensitivity * farMask;

                // NORMAL edges catch the creases depth cannot (two faces meeting at the
                // same distance).
                float3 nAB = SampleSceneNormals(uvA) - SampleSceneNormals(uvB);
                float3 nCD = SampleSceneNormals(uvC) - SampleSceneNormals(uvD);
                float normalEdge = sqrt(dot(nAB, nAB) + dot(nCD, nCD));
                normalEdge *= _NormalSensitivity;

                // Normal edges FADE with distance. Per-pixel normal change on a curved
                // surface grows as the object shrinks on screen - a distant sphere bends
                // its normals so fast that every pixel read as an edge and the whole ball
                // went black. Silhouettes are depth's job and survive at any range; the
                // normals only need to carry nearby creases.
                normalEdge *= saturate(_NormalFadeDistance / max(centre, 0.01));

                // The STEP edge: second-order depth - how far the centre deviates from
                // the average of each opposing tap pair. A sloped floor is LINEAR in
                // depth however steep, so this reads zero on ramps at any grazing angle;
                // a genuine step breaks planarity by the gap size, from EVERY viewing
                // angle. This is what the normal-gated combo term could not catch: the
                // turret's front disc face-on has the same normal as the wall behind it,
                // so a normal-break gate vetoed its own rim.
                float stepMag = max(abs(dA + dB - 2.0 * centre), abs(dC + dD - 2.0 * centre));
                float stepEdge = stepMag * _DepthAbsSensitivity * farMask;

                // GRAZING suppression. Eye depth across a plane is NOT linear in screen
                // space - it is a reciprocal curve whose second difference grows with
                // distance CUBED. Untreated, that painted the whole far floor black from
                // a camera-fixed distance outward (the "smudge at the end of the level"
                // that retreated as you advanced). Viewed face-on, a plane is depth-flat
                // and the turret disc's step survives untouched; the mask only kills the
                // term where the surface is seen edge-on, which is exactly where the
                // false positives live.
                float rawCentre = SampleSceneDepth(uv);
                float3 worldPos = ComputeWorldSpacePosition(uv, rawCentre, UNITY_MATRIX_I_VP);
                float3 viewDir = normalize(_WorldSpaceCameraPos - worldPos);
                float facing = saturate(dot(SampleSceneNormals(uv), viewDir));
                stepEdge *= smoothstep(0.15, 0.4, facing);

                float edge = smoothstep(_Threshold, _Threshold * 2.0,
                    max(max(depthEdge, normalEdge), stepEdge));

                // SKY is never inked. Sky pixels are "infinitely behind" everything, so
                // the far-side rule painted a smeared black band along any large surface's
                // horizon edge (the damage floor's far rim - the smudge at the end of the
                // level). An edge against the sky adds nothing: the silhouette against a
                // bright sky is already the strongest contrast in the frame.
                #if UNITY_REVERSED_Z
                float skyMask = step(0.000001, rawCentre);
                #else
                float skyMask = step(rawCentre, 0.999999);
                #endif
                edge *= skyMask;

                // The launch fade, scoped to the PLAYER: Polish publishes the player's
                // view depth, and only edges whose NEAR side sits within the window of it
                // dim - the ball's own rim fades while the camera trails, and every other
                // outline in the frame stays at full strength. Unset globals (0) read as
                // "no fade", so scenes without the driver draw normally.
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
