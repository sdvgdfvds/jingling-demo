using UnityEngine;

namespace NexusAnima
{
    /// <summary>Short-lived claw slash used by melee spirits instead of projectiles.</summary>
    public class MeleeClawEffect : MonoBehaviour
    {
        public float Duration = 0.2f;
        public Vector3 BaseScale = Vector3.one;
        float _t;
        Renderer _renderer;

        void Awake()
        {
            _renderer = GetComponent<Renderer>();
        }

        void Update()
        {
            _t += Time.deltaTime;
            float u = Mathf.Clamp01(_t / Mathf.Max(0.01f, Duration));
            float grow = 1f + u * 0.55f;
            transform.localScale = new Vector3(BaseScale.x * grow, BaseScale.y, BaseScale.z * (1f + u * 0.25f));

            if (_renderer != null && _renderer.material != null)
            {
                Color c = _renderer.material.color;
                c.a = 1f - u;
                _renderer.material.color = c;
                if (_renderer.material.HasProperty("_EmissionColor"))
                    _renderer.material.SetColor("_EmissionColor", c * (1.8f * (1f - u)));
            }

            if (_t >= Duration)
                Destroy(gameObject);
        }
    }
}
