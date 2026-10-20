using System;
using UnityEngine;

namespace NexusAnima
{
    [Serializable]
    public struct IdleAnimProfile
    {
        public float BodyFrequency;
        public float BobAmplitude;
        public float SwayAmplitude;
        public float TailFrequency;
        public float TailAmplitude;
        public float PhaseOffset;

        public static IdleAnimProfile Default => new IdleAnimProfile
        {
            BodyFrequency = 0.43f,
            BobAmplitude = 0.0042f,
            SwayAmplitude = 0.18f,
            TailFrequency = 0.28f,
            TailAmplitude = 1.0f,
            PhaseOffset = 0f
        };
    }
}
