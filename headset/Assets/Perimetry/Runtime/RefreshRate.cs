using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR;

namespace Perimetry.Headset
{
    /// <summary>
    /// Display refresh rate. Quest apps start at 72 Hz; 90 Hz is requested through the OpenXR Meta display
    /// utilities when the package provides them (PERIMETRY_OPENXR_DISPLAY_UTILITIES, set by a version define).
    /// Whatever rate is granted, the flow is told the real one so 200 ms is quantised to the right frame count.
    /// </summary>
    public static class RefreshRate
    {
        private static readonly List<XRDisplaySubsystem> Displays = new List<XRDisplaySubsystem>();

        private static XRDisplaySubsystem Display()
        {
            SubsystemManager.GetSubsystems(Displays);
            foreach (var d in Displays) if (d.running) return d;
            return null;
        }

        public static bool TryGetReported(out float hz)
        {
            hz = 0;
            var d = Display();
            return d != null && d.TryGetDisplayRefreshRate(out hz) && hz > 1;
        }

        public static void TryRequest(double hz, Action<string> log)
        {
#if PERIMETRY_OPENXR_DISPLAY_UTILITIES
            var d = Display();
            if (d == null) { log("no XR display"); return; }
            bool ok = UnityEngine.XR.OpenXR.Features.Meta.DisplayUtilities.TryRequestDisplayRefreshRate(d, (float)hz);
            log((ok ? "requested " : "request refused ") + hz + " Hz");
#else
            log("not available in this build (OpenXR display utilities missing); using the runtime default");
#endif
        }
    }
}
