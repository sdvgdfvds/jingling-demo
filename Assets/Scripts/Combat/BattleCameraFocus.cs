using System.Collections;
using UnityEngine;

namespace NexusAnima
{
    /// <summary>
    /// Temporarily zooms Main Camera toward a combat target, then restores pose.
    /// Distances are derived from the current home camera (tiny board safe).
    /// </summary>
    public class BattleCameraFocus : MonoBehaviour
    {
        Camera _cam;
        Vector3 _homePos;
        Quaternion _homeRot;
        bool _hasHome;
        Coroutine _routine;

        public bool IsFocusing { get; private set; }

        public void EnsureReady()
        {
            if (_cam == null)
                _cam = Camera.main;
            if (_cam != null && !_hasHome)
            {
                _homePos = _cam.transform.position;
                _homeRot = _cam.transform.rotation;
                _hasHome = true;
            }
        }

        public void CaptureHome()
        {
            _cam = Camera.main;
            if (_cam == null) return;
            _homePos = _cam.transform.position;
            _homeRot = _cam.transform.rotation;
            _hasHome = true;
        }

        public void AbortFocus()
        {
            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }
            IsFocusing = false;
        }

        /// <summary>
        /// Place Main Camera so the whole DynamicGrid / Board fills a formation-friendly frame,
        /// then store that pose as home.
        /// </summary>
        public void FrameToBoard(BoardGrid board, float fov = 42f)
        {
            EnsureReady();
            if (_cam == null)
                _cam = Camera.main;
            if (_cam == null || board == null)
                return;

            FrameMainCameraToBoard(board, _cam, fov);
            CaptureHome();
        }

        public static void FrameMainCameraToBoard(BoardGrid board, Camera cam = null, float fov = 42f)
        {
            if (board == null)
                return;
            if (cam == null)
                cam = Camera.main;
            if (cam == null)
                return;

            Bounds bounds = board.GetWorldBounds();
            Vector3 center = new Vector3(bounds.center.x, board.GridSurfaceY, bounds.center.z);
            float size = Mathf.Max(0.2f, Mathf.Max(bounds.size.x, bounds.size.z));

            // Match the old tiny-board framing ratios (≈37° pitch, south-looking).
            float distance = size * 1.15f;
            float height = size * 0.72f;
            Vector3 pos = center + new Vector3(0f, height, -distance);
            Vector3 lookAt = center + Vector3.up * (size * 0.04f);

            cam.transform.position = pos;
            cam.transform.rotation = Quaternion.LookRotation(lookAt - pos, Vector3.up);
            cam.fieldOfView = fov;
        }

        /// <summary>
        /// Zoom toward target. pullIn 0..1 = how far from home toward a close orbit.
        /// Legacy (duration, distance, height) overload kept for callers; distance/height ignored
        /// in favor of board-relative framing.
        /// </summary>
        public void FocusOn(Transform target, float duration = 0.3f, float distance = 0.5f, float height = 0.4f)
        {
            if (BattleFlyCamera.IsDriving) return;
            EnsureReady();
            if (_cam == null || target == null) return;
            if (!_hasHome)
                CaptureHome();
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = StartCoroutine(FocusRoutine(target, duration));
        }

        public void Release(float duration = 0.4f)
        {
            EnsureReady();
            if (_cam == null || !_hasHome) return;
            if (_routine != null)
                StopCoroutine(_routine);
            _routine = StartCoroutine(ReleaseRoutine(duration));
        }

        /// <summary>
        /// Concert BattleDemo only: pull in on the skill target, hold, then restore home.
        /// Ancient China must not call this — it keeps the during-cast FocusOn/Release.
        /// </summary>
        public IEnumerator CloseupThenHome(
            Transform target,
            Vector3 fallbackLook,
            float inDuration = 0.32f,
            float hold = 0.65f,
            float outDuration = 0.4f)
        {
            if (ArenaEnvironment.IsLegacyArchiveScene())
                yield break;
            if (BattleFlyCamera.IsDriving)
                yield break;

            EnsureReady();
            if (_cam == null)
                yield break;
            if (!_hasHome)
                CaptureHome();
            if (!_hasHome)
                yield break;

            if (_routine != null)
            {
                StopCoroutine(_routine);
                _routine = null;
            }

            // Always dolly from the live Play camera, never from a baked orbit.
            CaptureHome();
            IsFocusing = true;
            Vector3 startPos = _homePos;
            Quaternion startRot = _homeRot;
            _cam.transform.SetPositionAndRotation(startPos, startRot);
            inDuration = Mathf.Max(0.01f, inDuration);
            hold = Mathf.Max(0f, hold);
            outDuration = Mathf.Max(0.01f, outDuration);

            Vector3 lookPoint;
            if (target != null)
            {
                var unit = target.GetComponent<SpiritUnit>();
                lookPoint = unit != null ? unit.GetVfxWorldPosition() : target.position;
            }
            else
            {
                lookPoint = fallbackLook.sqrMagnitude > 1e-8f
                    ? fallbackLook
                    : startPos + startRot * Vector3.forward * 0.12f;
            }

            // Dolly strictly along the Play-view forward. The old camera→spirit chord
            // plus early look-at slerp pitched the lens up while still far, so the
            // first half of the push-in read as a zoom-out. Second-half framing (closer
            // on the skill target) stays; only the path into that pose changes.
            Vector3 viewFwd = startRot * Vector3.forward;
            if (viewFwd.sqrMagnitude < 1e-8f)
                viewFwd = Vector3.forward;
            viewFwd.Normalize();

            float along = Vector3.Dot(lookPoint - startPos, viewFwd);
            float minKeep = Mathf.Max(_cam.nearClipPlane * 2.5f, 0.028f);
            if (along < minKeep)
                along = Mathf.Max(minKeep + 0.04f, (lookPoint - startPos).magnitude * 0.5f);

            const float stayFraction = 0.55f;
            float travel = along * (1f - stayFraction);
            if (along - travel < minKeep)
                travel = Mathf.Max(0.012f, along - minKeep);
            travel = Mathf.Max(travel, 0.018f);

            Vector3 endPos = startPos + viewFwd * travel;
            if (Vector3.Dot(endPos - startPos, viewFwd) < 0f)
                endPos = startPos + viewFwd * 0.018f;
            if ((endPos - lookPoint).sqrMagnitude >= (startPos - lookPoint).sqrMagnitude)
                endPos = startPos + viewFwd * Mathf.Min(travel, along * 0.4f);

            Quaternion endRot = Quaternion.Slerp(
                startRot,
                Quaternion.LookRotation(lookPoint - endPos, Vector3.up),
                0.4f);

            float t = 0f;
            while (t < inDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / inDuration));
                _cam.transform.position = Vector3.Lerp(startPos, endPos, u);
                // Keep Play rotation for the first half (pure push-in); settle on the
                // spirit only in the second half — the part that was already correct.
                float rotU = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.48f, 1f, u));
                _cam.transform.rotation = Quaternion.Slerp(startRot, endRot, rotU);
                yield return null;
            }

            _cam.transform.position = endPos;
            _cam.transform.rotation = endRot;

            float held = 0f;
            while (held < hold)
            {
                held += Time.unscaledDeltaTime;
                _cam.transform.position = endPos;
                _cam.transform.rotation = endRot;
                yield return null;
            }

            Vector3 backStart = _cam.transform.position;
            Quaternion backRot = _cam.transform.rotation;
            t = 0f;
            while (t < outDuration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, t / outDuration);
                _cam.transform.position = Vector3.Lerp(backStart, _homePos, u);
                _cam.transform.rotation = Quaternion.Slerp(backRot, _homeRot, u);
                yield return null;
            }

            _cam.transform.position = _homePos;
            _cam.transform.rotation = _homeRot;
            IsFocusing = false;
        }

        IEnumerator FocusRoutine(Transform target, float duration)
        {
            IsFocusing = true;
            Vector3 startPos = _cam.transform.position;
            Quaternion startRot = _cam.transform.rotation;

            Vector3 lookPoint = target.position + Vector3.up * 0.08f;

            // Keep home viewing angle, but move much closer to the target.
            Vector3 fromTargetToHome = _homePos - lookPoint;
            float homeDist = fromTargetToHome.magnitude;
            if (homeDist < 0.05f)
                fromTargetToHome = -_cam.transform.forward;

            // Pull in hard so the hit VFX fills more of the frame on the tiny board.
            float focusDist = Mathf.Clamp(homeDist * 0.32f, 0.22f, 0.55f);
            Vector3 endPos = lookPoint + fromTargetToHome.normalized * focusDist;
            // Keep a gentle high angle so units + burst stay framed.
            endPos.y = Mathf.Lerp(_homePos.y, lookPoint.y + focusDist * 0.7f, 0.75f);

            Quaternion endRot = Quaternion.LookRotation(lookPoint - endPos, Vector3.up);

            float t = 0f;
            duration = Mathf.Max(0.01f, duration);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                _cam.transform.position = Vector3.Lerp(startPos, endPos, u);
                _cam.transform.rotation = Quaternion.Slerp(startRot, endRot, u);
                yield return null;
            }

            _cam.transform.position = endPos;
            _cam.transform.rotation = endRot;
            _routine = null;
        }

        IEnumerator ReleaseRoutine(float duration)
        {
            Vector3 startPos = _cam.transform.position;
            Quaternion startRot = _cam.transform.rotation;
            float t = 0f;
            duration = Mathf.Max(0.01f, duration);
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                float u = Mathf.SmoothStep(0f, 1f, t / duration);
                _cam.transform.position = Vector3.Lerp(startPos, _homePos, u);
                _cam.transform.rotation = Quaternion.Slerp(startRot, _homeRot, u);
                yield return null;
            }

            _cam.transform.position = _homePos;
            _cam.transform.rotation = _homeRot;
            IsFocusing = false;
            _routine = null;
        }
    }
}
