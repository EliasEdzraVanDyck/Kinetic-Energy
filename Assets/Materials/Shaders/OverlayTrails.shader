Shader "Custom/URP/OverlayTrails"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)

        _Intensity ("Intensity", Range(0, 1)) = 1

        _LineCount ("Line Count", Range(8, 100)) = 45
        _Density ("Density", Range(0, 1)) = 0.55
        _LineWidth ("Line Width", Range(0.01, 0.5)) = 0.10

        _InnerRadius ("Minimum Start", Range(0, 1)) = 0.18
        _OuterRadius ("Maximum Start", Range(0, 1)) = 0.68
        _TailSoftness ("Tail Softness", Range(0.001, 0.2)) = 0.04

        _AnimSpeed ("Animation Speed", Range(0, 10)) = 1.5
        _LengthAnimation ("Length Animation", Range(0, 0.25)) = 0.04
        _AngularJitter ("Angular Jitter", Range(0, 0.2)) = 0.025

        _Center ("Vanishing Point", Vector) = (0.5, 0.5, 0, 0)
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Cull Off
        ZWrite Off
        ZTest Always

        Blend SrcAlpha OneMinusSrcAlpha

        Pass
        {
            Name "SpeedLines"

            HLSLPROGRAM

            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)

                half4 _Color;

                float _Intensity;

                float _LineCount;
                float _Density;
                float _LineWidth;

                float _InnerRadius;
                float _OuterRadius;
                float _TailSoftness;

                float _AnimSpeed;
                float _LengthAnimation;
                float _AngularJitter;

                float4 _Center;

            CBUFFER_END


            // ------------------------------------------------------------
            // Cheap deterministic random value.
            // Same input -> same output, which keeps each ray stable.
            // ------------------------------------------------------------

            float Hash(float n)
            {
                return frac(sin(n * 127.1) * 43758.5453123);
            }


            Varyings vert(Attributes IN)
            {
                Varyings OUT;

                OUT.positionHCS =
                    TransformObjectToHClip(IN.positionOS.xyz);

                OUT.uv = IN.uv;

                return OUT;
            }


            half4 frag(Varyings IN) : SV_Target
            {
                const float kTwoPi = 6.28318530718;

                // --------------------------------------------------------
                // Position relative to vanishing point
                // --------------------------------------------------------

                float2 p = IN.uv - _Center.xy;

                // Correct for aspect ratio so angular spacing looks
                // consistent on widescreen resolutions.
                float aspect = _ScreenParams.x / _ScreenParams.y;

                float2 pAspect = p;
                pAspect.x *= aspect;


                // --------------------------------------------------------
                // Angle around the center
                // --------------------------------------------------------

                float angle = atan2(pAspect.y, pAspect.x);

                // Convert -PI...PI to 0...1
                float normalizedAngle =
                    angle / kTwoPi + 0.5;


                // --------------------------------------------------------
                // Split the screen into radial sectors.
                //
                // Each sector can contain one streak.
                // --------------------------------------------------------

                float sectorCoord =
                    normalizedAngle * _LineCount;

                float sectorID =
                    floor(sectorCoord);

                float local =
                    frac(sectorCoord);


                // --------------------------------------------------------
                // Random values unique to this streak
                // --------------------------------------------------------

                float rndExists =
                    Hash(sectorID + 1.37);

                float rndPosition =
                    Hash(sectorID + 17.13);

                float rndWidth =
                    Hash(sectorID + 31.71);

                float rndLength =
                    Hash(sectorID + 67.91);

                float rndSpeed =
                    Hash(sectorID + 91.43);

                float rndPhase =
                    Hash(sectorID + 121.7);


                // Some sectors contain no line.
                float exists =
                    step(1.0 - _Density, rndExists);


                // --------------------------------------------------------
                // Tiny animated angular movement
                // --------------------------------------------------------

                float time = _Time.y * _AnimSpeed;

                float wobble =
                    sin(
                        time * lerp(0.5, 1.1, rndSpeed)
                        + rndPhase * kTwoPi
                    );

                wobble *= _AngularJitter;

                local = frac(local + wobble);


                // --------------------------------------------------------
                // Random position of line inside its sector
                // --------------------------------------------------------

                float lineCenter =
                    lerp(0.20, 0.80, rndPosition);

                float distanceFromLine =
                    abs(local - lineCenter);


                // Random thickness
                float width =
                    _LineWidth
                    * lerp(0.55, 1.55, rndWidth);


                // Pixel-size antialiasing
                float aa =
                    max(fwidth(distanceFromLine), 0.0005);


                float lineMask =
                    1.0 - smoothstep(
                        width,
                        width + aa * 1.5,
                        distanceFromLine
                    );


                // --------------------------------------------------------
                // Calculate normalized distance:
                //
                //   center = 0
                //   edge of screen = 1
                //
                // This accounts for the fact that the viewport is
                // rectangular rather than circular.
                // --------------------------------------------------------

                float dist =
                    length(pAspect);

                float2 direction =
                    pAspect / max(dist, 0.0001);


                float distanceToVerticalEdge;

                if (direction.x >= 0)
                    distanceToVerticalEdge =
                        (1.0 - _Center.x) * aspect;
                else
                    distanceToVerticalEdge =
                        _Center.x * aspect;


                float distanceToHorizontalEdge;

                if (direction.y >= 0)
                    distanceToHorizontalEdge =
                        1.0 - _Center.y;
                else
                    distanceToHorizontalEdge =
                        _Center.y;


                float xEdge =
                    distanceToVerticalEdge
                    / max(abs(direction.x), 0.0001);

                float yEdge =
                    distanceToHorizontalEdge
                    / max(abs(direction.y), 0.0001);


                float distanceToEdge =
                    min(xEdge, yEdge);


                float radius =
                    saturate(dist / max(distanceToEdge, 0.0001));


                // --------------------------------------------------------
                // Every line starts at a different distance from center.
                //
                // Because width is angular, the ray naturally becomes
                // wider toward the outside of the screen, creating the
                // triangular/comic-book appearance.
                // --------------------------------------------------------

                float startRadius =
                    lerp(
                        _InnerRadius,
                        _OuterRadius,
                        rndLength
                    );


                // --------------------------------------------------------
                // Animate the line length slightly.
                // --------------------------------------------------------

                float lengthPulse =
                    sin(
                        time
                        * lerp(0.65, 1.35, rndSpeed)
                        + rndPhase * kTwoPi
                    );

                startRadius +=
                    lengthPulse * _LengthAnimation;


                // Fade in at the inner end of the streak.
                float radialMask =
                    smoothstep(
                        startRadius,
                        startRadius + _TailSoftness,
                        radius
                    );


                // --------------------------------------------------------
                // Final result
                // --------------------------------------------------------

                float alpha =
                    lineMask
                    * radialMask
                    * exists
                    * _Intensity
                    * _Color.a;


                return half4(
                    _Color.rgb,
                    alpha
                );
            }

            ENDHLSL
        }
    }
}