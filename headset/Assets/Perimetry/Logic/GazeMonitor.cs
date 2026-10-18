using System;
using Perimetry.Core.Geometry;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Latest gaze sample from the eye tracker (Phase 5), as an absolute field direction of the tested eye
    /// (degrees from straight ahead, patient's view), and whether gaze counts as available: a sample younger
    /// than <c>staleMs</c>. On a headset without an eye tracker (Quest 3) no sample ever arrives and
    /// everything behaves as before. The per-presentation statistics live in <see cref="PresentationTimeline"/>.
    /// </summary>
    public sealed class GazeMonitor
    {
        private readonly double _staleMs;
        private double? _lastMs;
        private FieldPoint _last;

        public GazeMonitor(double staleMs)
        {
            if (staleMs <= 0) throw new ArgumentOutOfRangeException(nameof(staleMs));
            _staleMs = staleMs;
        }

        /// <summary>True once any sample has arrived (the device has an eye tracker that produced data).</summary>
        public bool EverTracked { get; private set; }

        public void Sample(double tMs, FieldPoint gaze)
        {
            _lastMs = tMs; _last = gaze; EverTracked = true;
        }

        public bool Available(double nowMs) => _lastMs.HasValue && nowMs - _lastMs.Value <= _staleMs;

        public FieldPoint? Last(double nowMs) => Available(nowMs) ? _last : (FieldPoint?)null;

        /// <summary>Angle between the current gaze and <paramref name="target"/> (degrees); null when gaze is unavailable.</summary>
        public double? ErrorDeg(double nowMs, FieldPoint target) =>
            Available(nowMs) ? AngularGeometry.AngleBetweenDeg(target, _last) : (double?)null;
    }
}
