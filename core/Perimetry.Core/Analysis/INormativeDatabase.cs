namespace Perimetry.Core.Analysis
{
    /// <summary>
    /// Normative data plug-in (age x location -> mean / sd / percentile limits), on the
    /// HFA-equivalent dB scale, for one grid. The HFA database is proprietary and does not exist
    /// for this hardware, so implementations are either the provisional synthetic one or a
    /// database built from the clinic's own healthy cohort (phase 6), both through
    /// <see cref="NormativeBuilder"/>.
    /// </summary>
    public interface INormativeDatabase
    {
        string Id { get; }
        string GridId { get; }
        bool IsProvisional { get; }
        int LocationCount { get; }
        /// <summary>Age-expected sensitivity (HFA-equivalent dB) at a location.</summary>
        double ExpectedHfa(int locationIndex, double ageYears);
        double TdSd(int locationIndex);
        double PdSd(int locationIndex);
        /// <summary>Smallest configured probability level p with value below the p-quantile of normals (e.g. 0.005), or 1 when not significant.</summary>
        double TdProbability(int locationIndex, double td);
        double PdProbability(int locationIndex, double pd);
        double MdProbability(double md);
        /// <summary>PSD is significant when abnormally HIGH.</summary>
        double PsdProbability(double psd);
        double GhQuantile(double p);
        /// <summary>Quantile of |upper - lower| GHT sector-score difference in normals for a sector.</summary>
        double GhtSectorDiffQuantile(int sector, double p);
        /// <summary>Age-expected foveal sensitivity (HFA-equivalent dB), phase 5; NaN when the database has no foveal data.</summary>
        double ExpectedFovealHfa(double ageYears);
        /// <summary>Probability level of a foveal total deviation (as <see cref="TdProbability"/>); 1 when not significant or no foveal data.</summary>
        double FovealTdProbability(double td);
    }
}
