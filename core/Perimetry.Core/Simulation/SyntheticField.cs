using System;
using System.Linq;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Simulation
{
    public enum DefectType { Normal, SuperiorArcuate, InferiorArcuate, Hemianopia, CentralScotoma }

    /// <summary>
    /// True (simulated) thresholds in HFA-equivalent dB at each grid location, OD convention.
    /// Normal values come from the same model as the provisional normative plus patient-specific
    /// global shift and local noise; defects subtract <c>depthDb</c> in a region:
    ///  - superior/inferior arcuate: nasal Bjerrum region, 8°-24° eccentricity, x &lt;= 9°, incl. the nasal step;
    ///  - hemianopia: the whole temporal (x &gt; 0) half-field (vertical-meridian respecting);
    ///  - central scotoma: eccentricity &lt;= 5°.
    /// </summary>
    public sealed class SyntheticField
    {
        public TestGrid Grid { get; }
        public double[] TrueHfaDb { get; }
        public bool[] InDefect { get; }
        public DefectType Defect { get; }

        private SyntheticField(TestGrid g, double[] t, bool[] d, DefectType type) { Grid = g; TrueHfaDb = t; InDefect = d; Defect = type; }

        public static bool IsInDefect(DefectType type, FieldPoint p)
        {
            double e = p.Eccentricity;
            switch (type)
            {
                case DefectType.SuperiorArcuate: return p.Y > 0 && e >= 8 && e <= 24.5 && p.X <= 9;
                case DefectType.InferiorArcuate: return p.Y < 0 && e >= 8 && e <= 24.5 && p.X <= 9;
                case DefectType.Hemianopia: return p.X > 0;
                case DefectType.CentralScotoma: return e <= 5;
                default: return false;
            }
        }

        public static SyntheticField Create(SyntheticNormativeConfig model, TestGrid grid, double age, DefectType defect,
            double depthDb, Rng rng, double localNoiseScale = 1.0, double globalShiftDb = double.NaN)
        {
            double shift = double.IsNaN(globalShiftDb) ? rng.Gaussian(0, model.GlobalShiftSdDb) : globalShiftDb;
            var inDef = grid.Locations.Select(l => IsInDefect(defect, l.Point)).ToArray();
            var t = grid.Locations.Select(l =>
            {
                double v = Analysis.ProvisionalNormative.ModelMean(model, l.Point, age) + shift
                           + localNoiseScale * rng.Gaussian(0, Analysis.ProvisionalNormative.ModelLocalSd(model, l.Point));
                if (inDef[l.Index]) v -= depthDb;
                return Math.Max(-10, v);
            }).ToArray();
            return new SyntheticField(grid, t, inDef, defect);
        }
    }
}
