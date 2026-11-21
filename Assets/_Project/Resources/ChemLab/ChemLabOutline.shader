// Inverted-hull outline for inspected objects (ChemLabInspectable.SetMark): the mesh drawn again, pushed out along
// its normals by _Width metres, front faces culled, so only a coloured rim shows around the silhouette.
// Lives in Resources so builds include it (it is only referenced from code). Single-pass instanced stereo safe.
Shader "ChemLab/Outline"
{
    Properties
    {
        _Color ("Color", Color) = (0.08, 0.28, 0.54, 1)
        _Width ("Width (m)", Float) = 0.008
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry+10" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _Width;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings vert(Attributes i)
            {
                Varyings o;
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                // world-space push: the rim is the same few millimetres on a cup and on a cabinet
                float3 n = TransformObjectToWorldDir(i.normalOS, false);
                float len = length(n);
                float3 ws = TransformObjectToWorld(i.positionOS.xyz) + (len > 1e-5 ? n / len : 0) * _Width;
                o.positionCS = TransformWorldToHClip(ws);
                return o;
            }

            half4 frag(Varyings i) : SV_Target { return _Color; }
            ENDHLSL
        }
    }
}
