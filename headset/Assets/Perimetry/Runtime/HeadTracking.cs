using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Perimetry.Headset
{
    /// <summary>
    /// Keeps the camera on the head pose (what TrackedPoseDriver does, without the Input System dependency),
    /// reports whether the headset is worn, and gives the eye positions in camera space, which the stimulus
    /// geometry is centred on. Eye positions come from the XR head device (left/rightEyePosition relative to
    /// centerEyePosition), else from the camera's stereo view matrices; when neither is available (desktop
    /// preview) a configured fallback IPD is used. The source is logged in the session log.
    /// </summary>
    public sealed class HeadTracking
    {
        private readonly Camera _camera;
        private readonly float _fallbackIpdM;
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        public HeadTracking(Camera camera, float fallbackIpdM)
        {
            _camera = camera;
            _fallbackIpdM = fallbackIpdM;
            Application.onBeforeRender += UpdatePose;
        }

        public bool XrActive => XRSettings.isDeviceActive;
        public bool EyesFromXr => EyeSource != "fallback";
        /// <summary>"xrDevice", "stereoMatrices" or "fallback".</summary>
        public string EyeSource { get; private set; } = "fallback";
        public Vector3 LeftEyeLocal { get; private set; }
        public Vector3 RightEyeLocal { get; private set; }
        public float IpdM => Vector3.Distance(LeftEyeLocal, RightEyeLocal);

        private bool TryHmd(out InputDevice hmd)
        {
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.HeadMounted, _devices);
            hmd = _devices.Count > 0 ? _devices[0] : default;
            return _devices.Count > 0 && hmd.isValid;
        }

        public void UpdatePose()
        {
            if (!TryHmd(out var hmd)) return;
            if (hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 p)) _camera.transform.localPosition = p;
            if (hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion r)) _camera.transform.localRotation = r;
        }

        /// <summary>True unless the proximity sensor says the headset is off the face.</summary>
        public bool UserPresent()
        {
            if (!TryHmd(out var hmd)) return true;
            return !hmd.TryGetFeatureValue(CommonUsages.userPresence, out bool present) || present;
        }

        public void UpdateEyes()
        {
            if (TryHmd(out var hmd)
                && hmd.TryGetFeatureValue(CommonUsages.centerEyePosition, out Vector3 c)
                && hmd.TryGetFeatureValue(CommonUsages.centerEyeRotation, out Quaternion cr)
                && hmd.TryGetFeatureValue(CommonUsages.leftEyePosition, out Vector3 l)
                && hmd.TryGetFeatureValue(CommonUsages.rightEyePosition, out Vector3 r))
            {
                var inv = Quaternion.Inverse(cr);
                if (Accept(inv * (l - c), inv * (r - c), "xrDevice")) return;
            }
            if (_camera.stereoEnabled)
            {
                var lw = _camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse.MultiplyPoint(Vector3.zero);
                var rw = _camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse.MultiplyPoint(Vector3.zero);
                if (Accept(_camera.transform.InverseTransformPoint(lw), _camera.transform.InverseTransformPoint(rw), "stereoMatrices")) return;
            }
            EyeSource = "fallback";
            LeftEyeLocal = new Vector3(-_fallbackIpdM / 2, 0, 0);
            RightEyeLocal = new Vector3(_fallbackIpdM / 2, 0, 0);
        }

        /// <summary>Accepts eye positions only when they look like a human IPD (45-85 mm) with the right eye on the right.</summary>
        private bool Accept(Vector3 left, Vector3 right, string source)
        {
            float ipd = Vector3.Distance(left, right);
            if (ipd < 0.045f || ipd > 0.085f || right.x <= left.x) return false;
            LeftEyeLocal = left; RightEyeLocal = right; EyeSource = source;
            return true;
        }

        public void Dispose() => Application.onBeforeRender -= UpdatePose;
    }
}
