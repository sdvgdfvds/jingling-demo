using UnityEngine;
using UnityEngine.EventSystems;

namespace NexusAnima
{
    /// <summary>
    /// Drag the bond panel's bottom-right grip to resize it in Game view.
    /// Size is stored in PlayerPrefs so the next Play keeps it.
    /// </summary>
    public class BondPanelResizer : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        const string PrefW = "NexusAnima.BondPanel.W";
        const string PrefH = "NexusAnima.BondPanel.H";
        const float MinW = 168f;
        const float MinH = 180f;
        const float MaxW = 640f;
        const float MaxH = 820f;

        RectTransform _shell;

        public void Bind(RectTransform shell)
        {
            _shell = shell;
        }

        public void ApplySaved()
        {
            if (_shell == null)
                return;
            float w = PlayerPrefs.GetFloat(PrefW, _shell.sizeDelta.x);
            float h = PlayerPrefs.GetFloat(PrefH, _shell.sizeDelta.y);
            SetSize(w, h);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_shell == null || eventData == null)
                return;
            float scale = 1f;
            var canvas = _shell.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.scaleFactor > 0.01f)
                scale = canvas.scaleFactor;
            Vector2 size = _shell.sizeDelta;
            size.x += eventData.delta.x / scale;
            size.y -= eventData.delta.y / scale;
            SetSize(size.x, size.y);
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (_shell == null)
                return;
            PlayerPrefs.SetFloat(PrefW, _shell.sizeDelta.x);
            PlayerPrefs.SetFloat(PrefH, _shell.sizeDelta.y);
            PlayerPrefs.Save();
        }

        void SetSize(float w, float h)
        {
            if (_shell == null)
                return;
            w = Mathf.Clamp(w, MinW, MaxW);
            h = Mathf.Clamp(h, MinH, MaxH);
            _shell.sizeDelta = new Vector2(w, h);
        }
    }
}
