using System.Collections.Generic;
using Perimetry.Core.Geometry;
using UnityEngine;
using UnityEngine.XR;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace Perimetry.Headset
{
    /// <summary>
    /// Eye tracker access (Phase 5). On a Quest Pro with the OpenXR <c>XR_EXT_eye_gaze_interaction</c> extension
    /// (Unity OpenXR feature "Eye Gaze Interaction Profile", enabled by Perimetry > Configure Project) and the
    /// <c>com.oculus.permission.EYE_TRACKING</c> permission granted, the combined gaze pose arrives through the
    /// Input System as an <c>EyeGaze</c> device; some runtimes also expose it through the legacy XR input as a
    /// device with the EyeTracking characteristic (<c>eyesData</c> fixation point). Both are read; the first that
    /// has data wins. On a Quest 3 (no eye tracker) <see cref="TryGetGazeDirection"/> is always false and the flow
    /// behaves as before.
    /// The gaze pose is in the XR tracking space, the same space as the head pose the camera follows, so the
    /// direction is brought into camera space (where the head-locked rig lives) with the inverse head rotation.
    /// Field coordinates then follow <see cref="AngularGeometry.FromDirection"/> (x right, y up, z forward).
    /// </summary>
    public sealed class GazeTracking
    {
        public const string AndroidPermission = "com.oculus.permission.EYE_TRACKING";

        private readonly Camera _camera;
        private readonly List<InputDevice> _devices = new List<InputDevice>();

        public GazeTracking(Camera camera) { _camera = camera; }

        /// <summary>"openxrEyeGaze", "xrEyesData" or "none" (last call).</summary>
        public string Source { get; private set; } = "none";

        /// <summary>An eye-tracking device is present (data may still need the permission or a calibration).</summary>
        public bool DevicePresent
        {
            get
            {
#if ENABLE_INPUT_SYSTEM
                foreach (var d in InputSystem.devices)
                    if (d != null && d.layout != null && d.layout.Contains("EyeGaze")) return true;
#endif
                InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, _devices);
                return _devices.Count > 0;
            }
        }

        /// <summary>Gaze direction in camera (head) space this frame; false when no valid sample exists.</summary>
        public bool TryGetGazeDirection(out Vector3 dirInCameraSpace)
        {
            var inv = Quaternion.Inverse(_camera.transform.localRotation);
#if ENABLE_INPUT_SYSTEM
            foreach (var d in InputSystem.devices)
            {
                if (d == null || d.layout == null || !d.layout.Contains("EyeGaze")) continue;
                var tracked = d.TryGetChildControl<ButtonControl>("pose/isTracked");
                var rot = d.TryGetChildControl<QuaternionControl>("pose/rotation");
                if (rot == null || (tracked != null && !tracked.isPressed)) continue;
                dirInCameraSpace = (inv * (rot.ReadValue() * Vector3.forward)).normalized;
                Source = "openxrEyeGaze";
                return true;
            }
#endif
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.EyeTracking, _devices);
            foreach (var d in _devices)
            {
                if (!d.isValid) continue;
                if (d.TryGetFeatureValue(CommonUsages.eyesData, out Eyes eyes) && eyes.TryGetFixationPoint(out Vector3 fixation))
                {
                    var origin = _camera.transform.localPosition;
                    var v = fixation - origin;
                    if (v.sqrMagnitude > 1e-6f)
                    {
                        dirInCameraSpace = (inv * v).normalized;
                        Source = "xrEyesData";
                        return true;
                    }
                }
                if (d.TryGetFeatureValue(CommonUsages.isTracked, out bool isTracked) && isTracked
                    && d.TryGetFeatureValue(CommonUsages.deviceRotation, out Quaternion q))
                {
                    dirInCameraSpace = (inv * (q * Vector3.forward)).normalized;
                    Source = "xrEyesData";
                    return true;
                }
            }
            Source = "none";
            dirInCameraSpace = Vector3.forward;
            return false;
        }

        /// <summary>Camera-space direction to absolute field coordinates (degrees; azimuth x, elevation y).</summary>
        public static FieldPoint ToField(Vector3 dirInCameraSpace) =>
            AngularGeometry.FromDirection((dirInCameraSpace.x, dirInCameraSpace.y, dirInCameraSpace.z));

        /// <summary>Asks for the eye-tracking permission on the device (no-op elsewhere). Returns a note for the session log.</summary>
        public static string RequestPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (UnityEngine.Android.Permission.HasUserAuthorizedPermission(AndroidPermission)) return "permission granted";
            UnityEngine.Android.Permission.RequestUserPermission(AndroidPermission);
            return "permission requested";
#else
            return "permission not applicable on this platform";
#endif
        }
    }
}
