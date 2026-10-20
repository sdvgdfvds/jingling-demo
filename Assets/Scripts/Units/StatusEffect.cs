using UnityEngine;

namespace NexusAnima
{
    public class StatusEffect
    {
        public string Id;
        public float Remaining;
        public float AtkMul = 1f;
        public bool Stun;
        public bool Burn;
        public float BurnDps;

        public bool Tick(float dt)
        {
            Remaining -= dt;
            return Remaining > 0f;
        }
    }
}
