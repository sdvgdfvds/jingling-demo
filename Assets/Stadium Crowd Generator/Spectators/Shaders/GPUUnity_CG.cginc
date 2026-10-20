#ifndef GPU_UNITY_CG_INCLUDED
#define GPU_UNITY_CG_INCLUDED

#include "UnityCG.cginc"
sampler2D _VertexPositionTexture;
sampler2D _VertexNormalTexture;
sampler2D _VertexTangentTexture;
float4 _VertexPositionTexture_TexelSize;
float _Speed;
float _Length;
float _DeltaTime;

inline void VertexMotion1(uint vertexID,out half4 vertex)
{
	float t = (_Time.y - _DeltaTime) / _Length * _Speed;
	#if ANIM_LOOP 
		t = fmod(t, 1.0);
	#else
		t = saturate(t);
	#endif 
	float x = (vertexID + 0.5) * _VertexPositionTexture_TexelSize.x;
	float y = t; 
	vertex = tex2Dlod(_VertexPositionTexture, float4(x, y, 0, 0));
}

inline void VertexMotion2(uint vertexID,out half4 vertex,out half3 normal)
{
	float t = (_Time.y - _DeltaTime) / _Length * _Speed;
	#if ANIM_LOOP 
		t = fmod(t, 1.0);
	#else
		t = saturate(t);
	#endif 
	float x = (vertexID + 0.5) * _VertexPositionTexture_TexelSize.x;
	float y = t; 
	vertex = tex2Dlod(_VertexPositionTexture, float4(x, y, 0, 0));
	normal = tex2Dlod(_VertexNormalTexture, float4(x, y, 0, 0));
}

inline void VertexMotion3(uint vertexID,out half4 vertex,out half3 normal,out half4 tangent)
{
	float t = (_Time.y - _DeltaTime) / _Length * _Speed;
	#if ANIM_LOOP 
		t = fmod(t, 1.0);
	#else
		t = saturate(t);
	#endif 
	float x = (vertexID + 0.5) * _VertexPositionTexture_TexelSize.x;
	float y = t; 
	vertex = tex2Dlod(_VertexPositionTexture, float4(x, y, 0, 0));
	normal = tex2Dlod(_VertexNormalTexture, float4(x, y, 0, 0));
	tangent = tex2Dlod(_VertexTangentTexture, float4(x, y, 0, 0));
}


inline void VertexMotion1(uint vertexID,uint instanceID,out half4 vertex)
{
	float t = (_Time.y - _DeltaTime) / _Length * _Speed;
	#if ANIM_LOOP 
		t = fmod(t, 1.0);
	#else
		t = saturate(t);
	#endif 
	float x = (vertexID + 0.5) * _VertexPositionTexture_TexelSize.x;
	float y = t;
	vertex = tex2Dlod(_VertexPositionTexture, float4(x, y, 0, 0));
}

inline void VertexMotion2(uint vertexID,uint instanceID,out half4 vertex,out half3 normal)
{
	float t = (_Time.y - _DeltaTime) / _Length * _Speed;
	#if ANIM_LOOP 
		t = fmod(t, 1.0);
	#else
		t = saturate(t);
	#endif 
	float x = (vertexID + 0.5) * _VertexPositionTexture_TexelSize.x;
	float y = t;
	vertex = tex2Dlod(_VertexPositionTexture, float4(x, y, 0, 0));
	normal = tex2Dlod(_VertexNormalTexture, float4(x, y, 0, 0));
}

inline void VertexMotion3(uint vertexID,uint instanceID,out half4 vertex,out half3 normal,out half4 tangent)
{
	float t = (_Time.y - _DeltaTime) / _Length * _Speed;
	#if ANIM_LOOP 
		t = fmod(t, 1.0);
	#else
		t = saturate(t);
	#endif 
	float x = (vertexID + 0.5) * _VertexPositionTexture_TexelSize.x;
	float y = t;
	vertex = tex2Dlod(_VertexPositionTexture, float4(x, y, 0, 0));
	normal = tex2Dlod(_VertexNormalTexture, float4(x, y, 0, 0));
	tangent = tex2Dlod(_VertexTangentTexture, float4(x, y, 0, 0));
}


#endif
