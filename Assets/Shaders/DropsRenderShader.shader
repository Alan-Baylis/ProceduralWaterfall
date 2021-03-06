﻿Shader "Waterfall/DropsRender"
{
	Properties
	{
		_MainTex("Texture", 2D) = "white" {}
	}

	CGINCLUDE
	#include "UnityCG.cginc"

	struct Drop
	{
		uint streamId;
		float2 age;
		float dropSize;
		float3 position;
		float3 prevPosition;
		float4 velocity;
		float4 params;
	};

	struct v2g
	{
		float4 position : TEXCOORD0;
		float3 prevPosition : TEXCOORD1;
		float4 velocity : TEXCOORD2;
		float4 color : COLOR;
	};

	struct g2f
	{
		float4 position : POSITION;
		float2 texcoord : TEXCOORD0;
		float4 color : COLOR;
	};

	StructuredBuffer<Drop> _DropsBuffer;
	sampler2D _DropTexture;
	float4 _DropTexture_ST;
	float4x4 _InvViewMatrix;

	static const float3 g_positions_from[2] =
	{
		float3(-1, 1, 0),
		float3(1, 1, 0)
	};

	static const float3 g_positions_to[2] =
	{
		float3(-1,-1, 0),
		float3(1,-1, 0)
	};

	static const float2 g_texcoords[4] =
	{
		float2(0, 0),
		float2(1, 0),
		float2(0, 1),
		float2(1, 1),
	};

	v2g vert(uint id : SV_VertexID)
	{
		v2g o;
		o.position.xyz = _DropsBuffer[id].position;
		o.position.w = _DropsBuffer[id].dropSize;
		o.prevPosition = _DropsBuffer[id].prevPosition;
		o.velocity = _DropsBuffer[id].velocity;
		o.color = float4(0.18, 0.19, 0.19, 0.07);
		//o.color = float4(0.08, 0.09, 0.09, 0.07);
		return o;
	}

	[maxvertexcount(4)]
	void geom(point v2g In[1], inout TriangleStream<g2f> SpriteStream)
	{
		g2f o;
		[unroll]
		for (int i = 0; i < 2; i++)
		{
			float3 position = g_positions_from[i] * In[0].position.w;
			position = mul(_InvViewMatrix, position) + In[0].prevPosition;
			o.position = mul(UNITY_MATRIX_MVP, float4(position, 1.0));

			o.color = In[0].color;
			o.texcoord = g_texcoords[i];

			SpriteStream.Append(o);
		}

		for (int j = 0; j < 2; j++)
		{
			float3 position = g_positions_to[j] * In[0].position.w;
			position = mul(_InvViewMatrix, position) + lerp(In[0].prevPosition.xyz, In[0].position.xyz, 0.1 * length(In[0].velocity.xyz));
			o.position = mul(UNITY_MATRIX_MVP, float4(position, 1.0));

			o.color = In[0].color;
			o.texcoord = g_texcoords[j+2];

			SpriteStream.Append(o);
		}

		SpriteStream.RestartStrip();
	}

	fixed4 frag(g2f i) : SV_Target
	{
		return tex2D(_DropTexture, i.texcoord.xy) * i.color;
	}
		ENDCG

	SubShader
	{
		Tags{ "RenderType" = "Transparent" "IgnoreProjector" = "True" "RenderType" = "Transparent" }
			Zwrite Off
			Blend OneMinusDstColor One
			//Blend One One
			Cull Off

		Pass
		{
			CGPROGRAM
			#pragma target 5.0
			#pragma vertex vert
			#pragma geometry geom
			#pragma fragment frag
			ENDCG
		}
	}
}
