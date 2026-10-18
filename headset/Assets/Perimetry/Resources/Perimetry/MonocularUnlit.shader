// Perimetry/MonocularUnlit: draws a uniform patch (background, stimulus, fixation target) to ONE eye.
// - Eye routing: _PerimetryEye = 0 (left, OS), 1 (right, OD) or -1 (both). The other eye gets
//   _PerimetryUntestedLevel (0 = black). Requires single-pass instanced / multiview stereo (Quest default);
//   in a non-stereo preview every pixel is drawn as the right eye.
// - Levels are sRGB-ENCODED drive levels per channel (what the luminance calibration is indexed by), one
//   set per eye so a per-eye calibration is honoured even when both eyes are drawn.
// - Ordered dithering to _PerimetryMaxCode (255 for 8-bit), Bayer 4x4, optionally shifted every frame:
//   code = floor(level * maxCode + threshold). Output is sRGBToLinear(code / maxCode) into an sRGB target,
//   so the panel receives exactly `code`. Same formula as Perimetry.Headset.Logic.Dither (tested there).
// Plain UnityCG HLSL: renders in URP (SRPDefaultUnlit pass) and in the built-in pipeline.
// Project must use Linear colour space, HDR off, no post-processing (see Perimetry > Configure Project).
Shader "Perimetry/MonocularUnlit"
{
    Properties
    {
        _LevelLeft ("Drive level, left eye (sRGB-encoded RGB)", Vector) = (0, 0, 0, 1)
        _LevelRight ("Drive level, right eye (sRGB-encoded RGB)", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" }
        Pass
        {
            Cull Off
            ZWrite On
            ZTest LEqual
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "PerimetryCommon.cginc"

            float4 _LevelLeft;
            float4 _LevelRight;

            struct appdata { float4 vertex : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                nointerpolation float eye : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.eye = PerimetryEyeIndex();
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float eye = i.eye < 0 ? 1 : i.eye;
                float3 level = eye < 0.5 ? _LevelLeft.rgb : _LevelRight.rgb;
                if (_PerimetryEye >= 0 && abs(eye - _PerimetryEye) > 0.5) level = _PerimetryUntestedLevel.xxx;
                return float4(PerimetryDitherToLinear(level, i.pos.xy), 1);
            }
            ENDCG
        }
    }
}
