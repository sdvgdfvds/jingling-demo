
#ifndef GPU_UNITY_STANDARD_CORE_FORWARD_INCLUDED
#define GPU_UNITY_STANDARD_CORE_FORWARD_INCLUDED

#if defined(UNITY_NO_FULL_STANDARD_SHADER)
#   define UNITY_STANDARD_SIMPLE 1
#endif


#include "UnityStandardConfig.cginc"

#if UNITY_STANDARD_SIMPLE
    #include "GPUUnity_StandardCoreForwardSimple.cginc"
    VertexOutputBaseSimple vertBase (VertexInput v, uint vid : SV_VertexID) { return vertForwardBaseSimple(v,vid); }
    VertexOutputForwardAddSimple vertAdd (VertexInput v, uint vid : SV_VertexID) { return vertForwardAddSimple(v,vid); }
    half4 fragBase (VertexOutputBaseSimple i) : SV_Target { return fragForwardBaseSimpleInternal(i); }
    half4 fragAdd (VertexOutputForwardAddSimple i) : SV_Target { return fragForwardAddSimpleInternal(i); }
#else
    #include "GPUUnity_StandardCore.cginc"
    VertexOutputForwardBase vertBase (VertexInput v, uint vid : SV_VertexID) { return vertForwardBase(v,vid); }
    VertexOutputForwardAdd vertAdd (VertexInput v, uint vid : SV_VertexID) { return vertForwardAdd(v,vid); }
    half4 fragBase (VertexOutputForwardBase i) : SV_Target { return fragForwardBaseInternal(i); }
    half4 fragAdd (VertexOutputForwardAdd i) : SV_Target { return fragForwardAddInternal(i); }
#endif

#endif // GPU_UNITY_STANDARD_CORE_FORWARD_INCLUDED
