using System;
using System.Collections.Generic;
using System.IO;
using Perimetry.Core.Config;
using Perimetry.Core.Luminance;
using Perimetry.Headset.Logic;
using UnityEngine;
using UnityEngine.XR;

namespace Perimetry.Headset
{
    /// <summary>
    /// Headset entry point. Loads config (Resources) and the optional calibration and exam order from
    /// Application.persistentDataPath, builds the head-locked rig and drives <see cref="PatientFlow"/> once
    /// per frame: input edges, then Tick, then render the view. Files on the headset
    /// (/sdcard/Android/data/&lt;package&gt;/files/):
    ///   calibration.json  photometric calibration (LuminanceCalibration); absent or expired = "NÃO CALIBRADO"
    ///   exam-order.json   optional {patientRef, ageYears, refractiveCorrection, presetId, seed}; renamed after use
    ///   exams/            exam_*.json + session_*.json, each with a .hmac integrity sidecar
    ///   device.key        per-device HMAC key (created on first run)
    /// </summary>
    public sealed class PerimetryApp : MonoBehaviour
    {
        public const string CalibrationFile = "calibration.json";
        public const string OrderFile = "exam-order.json";

        private ClinicalConfig _cfg;
        private HeadsetConfig _h;
        private LuminanceMapper _lum;
        private PatientFlow _flow;
        private HeadTracking _head;
        private HeadsetRig _rig;
        private ResponseInput _input;
        private VoicePlayer _voice;
        private ExamStorage _storage;
        private readonly List<double> _frameTimes = new List<double>();
        private bool _refreshMeasured, _eyeSourceLogged, _wasPresent = true;
        private string _fatal;

        public PatientFlow Flow => _flow;
        public HeadsetRig Rig => _rig;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureInstance()
        {
#if UNITY_2023_1_OR_NEWER
            if (FindAnyObjectByType<PerimetryApp>() == null)
#else
            if (FindObjectOfType<PerimetryApp>() == null)
#endif
                new GameObject("PerimetryApp").AddComponent<PerimetryApp>();
        }

        private void Awake()
        {
            try { Init(); }
            catch (Exception e)
            {
                _fatal = e.GetType().Name + ": " + e.Message;
                Debug.LogError("[Perimetry] startup failed: " + e);
            }
        }

        private void Init()
        {
            Application.targetFrameRate = 120;
            QualitySettings.vSyncCount = 0;
            Screen.sleepTimeout = SleepTimeout.NeverSleep;

            _cfg = ConfigLoader.Load();
            _h = HeadsetConfig.From(_cfg);
            string data = Application.persistentDataPath;

            _lum = new LuminanceMapper(_cfg.Display, LoadCalibration(data, out string calNote));
            _storage = new ExamStorage(Path.Combine(data, "exams"), ExamStorage.LoadOrCreateKey(Path.Combine(data, ExamStorage.KeyFileName)));
            var order = LoadOrder(data);

            var cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("PerimetryCamera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            cam.transform.SetParent(null, false);
            cam.transform.localScale = Vector3.one;
            _head = new HeadTracking(cam, (float)(_h.Presentation.FallbackIpdMm / 1000.0));
            _rig = new HeadsetRig(cam, _head, _lum, _h, _cfg.Display.VirtualStimulusDistanceM);
            _input = new ResponseInput();
            _voice = new VoicePlayer(gameObject, _h.Language);

            _flow = new PatientFlow(_cfg, _h, _lum, _storage, order, new FlowEnvironment
            {
                DeviceModel = SystemInfo.deviceModel,
                DeviceSerial = SystemInfo.deviceUniqueIdentifier,
            });
            _flow.Note("app", $"unity {Application.unityVersion}, xr {(XRSettings.isDeviceActive ? XRSettings.loadedDeviceName : "none")}, colorSpace {QualitySettings.activeColorSpace}");
            _flow.Note("calibration", calNote);
            if (order != null) _flow.Note("examOrder", "loaded from " + OrderFile);
            RefreshRate.TryRequest(_cfg.Display.RefreshRateHz, n => _flow.Note("refreshRateRequest", n));
            Debug.Log("[Perimetry] ready, config " + _cfg.Hash.Substring(0, 12) + ", storage " + _storage.DirectoryPath);
        }

        private LuminanceCalibration LoadCalibration(string dir, out string note)
        {
            string p = Path.Combine(dir, CalibrationFile);
            if (!File.Exists(p)) { note = "none (nominal sRGB, NÃO CALIBRADO)"; return null; }
            try
            {
                var cal = LuminanceCalibration.FromJson(File.ReadAllText(p));
                if (cal.IsExpired(DateTime.UtcNow)) { note = "expired " + cal.ValidUntilUtc?.ToString("o") + " (ignored, NÃO CALIBRADO)"; return null; }
                note = "measured " + cal.DeviceSerial + " " + cal.MeasuredAtUtc?.ToString("o");
                return cal;
            }
            catch (Exception e) { note = "invalid (" + e.Message + "), NÃO CALIBRADO"; return null; }
        }

        private static ExamOrder LoadOrder(string dir)
        {
            string p = Path.Combine(dir, OrderFile);
            if (!File.Exists(p)) return null;
            var order = ExamOrder.Parse(File.ReadAllText(p));
            // consume it: the next patient must not inherit this order
            File.Move(p, Path.Combine(dir, $"exam-order.used-{DateTime.UtcNow:yyyyMMdd'T'HHmmss}.json"));
            return order;
        }

        private void Update()
        {
            if (_flow == null) return;
            double now = Time.realtimeSinceStartupAsDouble * 1000.0;
            MeasureRefresh(now);

            _input.Poll((float)(now / 1000.0));
            if (_input.PressedThisFrame) _flow.ButtonDown(now);
            if (_input.ReleasedThisFrame) _flow.ButtonUp(now);
            if (_input.AdjustThisFrame != 0) _flow.OperatorAdjust(_input.AdjustThisFrame);
            if (_input.AbortRequested && !_flow.IsFinished) _flow.OperatorAbort(now);
            _input.EndFrame();

            bool present = _head.UserPresent();
            if (present != _wasPresent) { _flow.SetUserPresent(present, now); _wasPresent = present; }

            _flow.Tick(now);
            _rig.Render(_flow.View);
            if (_flow.View.AudioCue != null) _voice.Play(_flow.View.AudioCue);

            if (!_eyeSourceLogged && _head.XrActive && Time.frameCount > 30)
            {
                _eyeSourceLogged = true;
                _flow.Note("eyePositions", $"{_head.EyeSource} ipd={_head.IpdM * 1000:F1}mm");
            }
        }

        /// <summary>Median frame interval over the first 90 frames after start-up, handed to the flow before the exam.</summary>
        private void MeasureRefresh(double now)
        {
            if (_refreshMeasured || Time.frameCount < 30) return;
            _frameTimes.Add(now);
            if (_frameTimes.Count < 91) return;
            var dts = new List<double>();
            for (int i = 1; i < _frameTimes.Count; i++) dts.Add(_frameTimes[i] - _frameTimes[i - 1]);
            dts.Sort();
            double hz = 1000.0 / dts[dts.Count / 2];
            _refreshMeasured = true;
            if (RefreshRate.TryGetReported(out float reported)) hz = reported;
            _flow.SetMeasuredRefreshRate(hz);
        }

        private void OnGUI()
        {
            // Visible only on the desktop / simulator mirror window, never inside the headset.
            if (_fatal != null) { GUI.Label(new Rect(10, 10, 1200, 40), "Perimetry: " + _fatal); return; }
            if (_flow == null) return;
            GUI.Label(new Rect(10, 10, 1200, 22), $"{_flow.State}  eye {_flow.CurrentEye}  preset {_flow.Preset.Id}  " +
                $"{(_lum.IsCalibrated ? "calibrated" : "NÃO CALIBRADO")}  {_flow.RefreshRateHz:F0} Hz  eyes {_head.EyeSource}");
            var s = _flow.CurrentSession;
            if (s != null)
            {
                var r = s.BuildRecord().Reliability;
                GUI.Label(new Rect(10, 32, 1200, 22), $"presentations {r.TotalPresentations}  FL {r.FixationLosses.Errors}/{r.FixationLosses.Trials}  " +
                    $"FP {r.FalsePositives.Errors}/{r.FalsePositives.Trials}  FN {r.FalseNegatives.Errors}/{r.FalseNegatives.Trials}");
            }
            GUI.Label(new Rect(10, 54, 1200, 22), Perimetry.Core.Exam.Disclaimers.Research);
        }

        private void OnDestroy() => _head?.Dispose();
    }
}
