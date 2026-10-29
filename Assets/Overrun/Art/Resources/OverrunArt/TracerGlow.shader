Shader "Overrun/TracerGlow"
{
    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha One
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                // Soft cross-section makes thin light streaks read clearly without
                // a bloom volume, source texture, or a moving bullet billboard.
                half center = saturate(1.0 - abs(input.uv.y * 2.0 - 1.0));
                half endFade = saturate(input.uv.x * 8.0) * saturate((1.0 - input.uv.x) * 12.0);
                return half4(input.color.rgb, input.color.a * center * center * endFade);
            }
            ENDHLSL
        }
    }
}
