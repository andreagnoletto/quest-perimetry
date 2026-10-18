using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Perimetry.Headset
{
    /// <summary>
    /// Patient response button: either index trigger of a Touch controller, or a Bluetooth clicker / keyboard
    /// (Space, Enter, PageDown/PageUp, right arrow, gamepad button 0). Polled once per frame, so timestamps
    /// have frame resolution (11 ms at 90 Hz). Operator controls: thumbstick up/down or arrow keys adjust the
    /// setup screens; holding B/Y (or Escape) for 3 s stops the session.
    /// Requires Player Settings > Active Input Handling = Both (or Input Manager) for the legacy key polling.
    /// </summary>
    public sealed class ResponseInput
    {
        public const float AbortHoldSeconds = 3f;
        private static readonly KeyCode[] ResponseKeys =
            { KeyCode.Space, KeyCode.Return, KeyCode.KeypadEnter, KeyCode.PageDown, KeyCode.PageUp, KeyCode.RightArrow, KeyCode.JoystickButton0 };

        private readonly List<InputDevice> _controllers = new List<InputDevice>();
        private bool _wasDown;
        private int _lastAxisStep;
        private float _abortHeldSince = -1;

        public bool IsDown { get; private set; }
        public bool PressedThisFrame { get; private set; }
        public bool ReleasedThisFrame { get; private set; }
        public int AdjustThisFrame { get; private set; }
        public bool AbortRequested { get; private set; }

        public void Poll(float now)
        {
            bool down = false, operatorHold = false;
            float axisY = 0;
            InputDevices.GetDevicesWithCharacteristics(InputDeviceCharacteristics.Controller, _controllers);
            foreach (var c in _controllers)
            {
                if (c.TryGetFeatureValue(CommonUsages.triggerButton, out bool t) && t) down = true;
                if (c.TryGetFeatureValue(CommonUsages.secondaryButton, out bool b) && b) operatorHold = true;
                if (c.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 a) && Mathf.Abs(a.y) > Mathf.Abs(axisY)) axisY = a.y;
            }
            try
            {
                foreach (var k in ResponseKeys) if (Input.GetKey(k)) down = true;
                if (Input.GetKey(KeyCode.Escape)) operatorHold = true;
                if (Input.GetKeyDown(KeyCode.UpArrow)) AdjustThisFrame = +1;
                if (Input.GetKeyDown(KeyCode.DownArrow)) AdjustThisFrame = -1;
            }
            catch (System.InvalidOperationException)
            {
                // Active Input Handling = Input System only: legacy keys unavailable, controllers still work.
            }

            PressedThisFrame = down && !_wasDown;
            ReleasedThisFrame = !down && _wasDown;
            IsDown = down;
            _wasDown = down;

            int step = axisY > 0.7f ? 1 : axisY < -0.7f ? -1 : 0;
            AdjustThisFrame = step != 0 && step != _lastAxisStep ? step : AdjustThisFrame;
            _lastAxisStep = step;

            if (operatorHold) { if (_abortHeldSince < 0) _abortHeldSince = now; }
            else _abortHeldSince = -1;
            AbortRequested = _abortHeldSince >= 0 && now - _abortHeldSince >= AbortHoldSeconds;
        }

        public void EndFrame() { AdjustThisFrame = 0; }
    }
}
