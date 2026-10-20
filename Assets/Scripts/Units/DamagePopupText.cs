using UnityEngine;

namespace NexusAnima
{
    public class DamagePopupText : MonoBehaviour
    {
        public float Duration = 0.8f;
        public Vector3 WorldOrigin;
        float _t;
        RectTransform _rt;

        void Awake()
        {
            _rt = GetComponent<RectTransform>();
        }

        void Update()
        {
            _t += Time.deltaTime;
            Vector3 world = WorldOrigin + Vector3.up * (1.2f * _t);
            if (_rt != null)
            {
                Vector2 canvasPos = GameScreenCanvas.WorldToCanvasLocal(world, out bool visible);
                _rt.gameObject.SetActive(visible);
                if (visible)
                    _rt.anchoredPosition = canvasPos;
            }

            if (_t >= Duration) Destroy(gameObject);
        }
    }
}
