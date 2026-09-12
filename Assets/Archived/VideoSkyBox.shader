Shader "Unlit/VideoSkyboxShader"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
    }
    SubShader
    {
        // Tell the renderer this is a background element
        Tags { "Queue"="Background" "RenderType"="Background" }

        // We don't want to see the back of the skybox, only the inside
        Cull Off ZWrite Off ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            sampler2D _MainTex;
            
            v2f vert (appdata v) {
                v2f o;
                // Use built-in Unity methods to position the skybox correctly
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target {
                // Just sample our video texture
                return tex2D(_MainTex, i.uv);
            }
            ENDCG
        }
    }
}