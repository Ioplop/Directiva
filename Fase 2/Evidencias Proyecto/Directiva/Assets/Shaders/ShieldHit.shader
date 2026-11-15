Shader "Directiva/ShieldHit"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.3, 0.8, 1, 1)
        _Coverage ("Cobertura (0-1)", Range(0.05, 1)) = 0.3
        _Softness ("Suavidad del borde", Range(0.01, 1)) = 0.3
        _RimPower ("Brillo de borde", Range(0.5, 6)) = 2
        _Fade ("Intensidad", Range(0, 1)) = 0
        _HitDir ("Direccion del golpe (local)", Vector) = (0, 0, 1, 0)
        _Center ("Centro (local)", Vector) = (0, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Blend SrcAlpha One
            ZWrite Off
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _HitDir;
                float4 _Center;
                half _Coverage;
                half _Softness;
                half _RimPower;
                half _Fade;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float3 dirOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dirOS = IN.positionOS.xyz - _Center.xyz;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.viewDirWS = GetWorldSpaceViewDir(TransformObjectToWorld(IN.positionOS.xyz));
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // Que tan de frente al golpe esta este punto del escudo (1 = justo enfrente)
                float facing = dot(normalize(IN.dirOS), normalize(_HitDir.xyz));
                float edge = 1.0 - 2.0 * _Coverage;
                float mask = saturate((facing - edge) / max(_Softness, 0.001));

                // Las caras vistas de lado brillan mas
                float ndv = saturate(dot(normalize(IN.normalWS), normalize(IN.viewDirWS)));
                float rim = pow(1.0 - ndv, _RimPower);

                float alpha = mask * _Fade * (0.35 + 0.65 * rim);
                return half4(_Color.rgb, alpha);
            }
            ENDHLSL
        }
    }
}