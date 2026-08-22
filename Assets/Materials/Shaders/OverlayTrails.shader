Shader "Custom/URP/OverlayTrails"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (1, 1, 1, 1)

        _Intensity ("Intensity", Range(0, 1)) = 1

        _LineCount ("Line Count", Range(8, 100)) = 45
        _Density ("Density", Range(0, 1)) = 0.55
        _LineWidth ("Line Width", Range(0.01, 0.5)) = 0.10

        _InnerRadius ("Inner Clear Radius", Range(0, 2)) = 0.5
        _OuterRadius ("Outer Travel Extent", Range(0.5, 3)) = 2.0
        _TailSoftness ("Tail Softness", Range(0.001, 0.2)) = 0.04

        _AnimSpeed ("Animation Speed", Range(0, 10)) = 1.5
        _LengthAnimation ("Length Animation", Range(0, 0.25)) = 0.04
        _AngularJitter ("Angular Jitter", Range(0, 0.2)) = 0.025

        _Center ("Vanishing Point", Vector) = (0.5, 0.5, 0, 0)

        _OutwardTaper ("Outward Taper", Range(0, 1)) = 0.15

        _StreamSpeed ("Stream Speed", Range(0, 5)) = 1.2
        _StreamLength ("Stream Length", Range(0.05, 1)) = 0.35
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

                float _OutwardTaper;

                float _StreamSpeed;
                float _StreamLength;

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


                // Random thickness. The mask itself is built AFTER the radius is known -
                // the outward taper needs it.
                float width =
                    _LineWidth
                    * lerp(0.55, 1.55, rndWidth);


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
                // Outward taper: the streak's angular width SHRINKS with
                // radius, so each wedge is thick toward the centre and
                // runs to a point at the screen edge - an arrowhead
                // pointing AWAY from the centre. (Constant angular width
                // did the opposite: wedges widened outward, so every
                // streak read as an arrow aimed AT the centre.)
                // --------------------------------------------------------

                width *= lerp(1.25, 1.0 - _OutwardTaper, radius);


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
                // STREAMING SLIVERS: each line is a finite band that
                // slides OUTWARD along its ray and wraps.
                //
                // Banding runs on a CIRCULAR radius (0.5 screen heights
                // = 1), NOT the edge-normalised one above - that one is
                // corrected for the rectangular viewport, so any fixed
                // threshold on it traced a scaled RECTANGLE around the
                // centre. The edge-normalised radius keeps serving the
                // outward taper, where tracking the actual screen edge
                // is the point.
                // --------------------------------------------------------

                float rBand =
                    dist * 2.0;


                // Each line's clear centre is its own: jittered per line
                // so the inner ends land at slightly different distances
                // instead of a single shared ring.
                float rndInner =
                    Hash(sectorID + 7.7);

                float innerR =
                    _InnerRadius * lerp(0.8, 1.35, rndInner);


                float len =
                    _StreamLength * lerp(0.7, 1.4, rndLength);

                // _OuterRadius is the outward travel extent in the SAME
                // circular units - past the corners, so slivers leave
                // the screen before wrapping.
                float span =
                    _OuterRadius + len - innerR;

                float cycle =
                    frac(
                        rndPosition
                        + time * _StreamSpeed
                            * lerp(0.75, 1.35, rndSpeed)
                    );

                float bandStart =
                    innerR + cycle * span - len;


                // Long soft head and tail (scaled by the sliver's own
                // length) - hard ends on a fast band strobed instead of
                // reading as motion.
                float fade =
                    max(_TailSoftness, len * 0.4);

                float radialMask =
                    smoothstep(
                        bandStart,
                        bandStart + fade,
                        rBand
                    )
                    * (1.0 - smoothstep(
                        bandStart + len - fade,
                        bandStart + len,
                        rBand
                    ));

                radialMask *=
                    smoothstep(
                        innerR * 0.85,
                        innerR,
                        rBand
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