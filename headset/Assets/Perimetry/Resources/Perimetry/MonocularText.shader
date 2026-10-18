// Perimetry/MonocularText: TextMesh glyphs (font atlas alpha) at a fixed drive level, routed to one eye
// like Perimetry/MonocularUnlit. Glyphs are discarded in the untested eye, which stays black.
Shader "Perimetry/MonocularText"
{
    Properties
    {
        _MainTex ("Font texture", 2D) = "white" {}
        _LevelLeft ("Drive level, left eye", Vector) = (0, 0, 0, 1)
        _LevelRight ("Drive level, right eye", Vector) = (0, 0, 0, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Cull Off
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            #include "PerimetryCommon.cginc"

            sampler2D _MainTex;
            float4 _MainTex_ST;
            float4 _LevelLeft;
            float4 _LevelRight;

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation float eye : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.eye = PerimetryEyeIndex();
                return o;
            }

            float4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float eye = i.eye < 0 ? 1 : i.eye;
                if (_PerimetryEye >= 0 && abs(eye - _PerimetryEye) > 0.5) discard;
                float a = tex2D(_MainTex, i.uv).a;
                if (a <= 0.01) discard;
                float3 level = eye < 0.5 ? _LevelLeft.rgb : _LevelRight.rgb;
                return float4(PerimetrySrgbToLinear(level), a);
            }
            ENDCG
        }
    }
}
