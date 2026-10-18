using System;
using System.Collections.Generic;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Where the fixation target is drawn (Phase 5). A target is one or more points in absolute field
    /// coordinates of the tested eye (degrees from straight ahead, patient's view):
    /// Central = one point at the target position; Diamond4 (foveal test) = four dots at ±d on each axis
    /// around an empty centre, the centre being where the foveal stimulus appears. In oculokinetic mode the
    /// target position is not (0, 0) and the dots are composed around it like stimuli
    /// (<see cref="AngularGeometry.Compose"/>).
    /// </summary>
    public static class FixationLayout
    {
        public static IReadOnlyList<FieldPoint> Points(FixationTargetKind kind, FieldPoint target, double diamondHalfSizeDeg)
        {
            if (kind == FixationTargetKind.Central) return new[] { target };
            if (diamondHalfSizeDeg <= 0) throw new ArgumentOutOfRangeException(nameof(diamondHalfSizeDeg));
            double d = diamondHalfSizeDeg;
            return new[]
            {
                AngularGeometry.Compose(target, new FieldPoint(d, 0)),
                AngularGeometry.Compose(target, new FieldPoint(0, d)),
                AngularGeometry.Compose(target, new FieldPoint(-d, 0)),
                AngularGeometry.Compose(target, new FieldPoint(0, -d)),
            };
        }

        /// <summary>Target scale during the lock animation: start scale at progress 0, 1 at progress 1.</summary>
        public static double LockScale(double progress, LockAnimationConfig a) =>
            a.StartScale + (1 - a.StartScale) * Clamp01(progress);

        /// <summary>Target luminance factor during the lock animation: start factor at progress 0, 1 at progress 1.</summary>
        public static double LockLuminanceFactor(double progress, LockAnimationConfig a) =>
            a.StartLuminanceFactor + (1 - a.StartLuminanceFactor) * Clamp01(progress);

        private static double Clamp01(double v) => v < 0 ? 0 : v > 1 ? 1 : v;
    }
}
