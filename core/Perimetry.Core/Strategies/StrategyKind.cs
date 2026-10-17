namespace Perimetry.Core.Strategies
{
    public enum StrategyKind
    {
        /// <summary>Bayesian ZEST with bimodal prior and neighbour seeding (standard stopping rule, config "zest").</summary>
        Zest,
        /// <summary>ZEST with a looser stopping rule and narrower normal prior (config "zestFast").</summary>
        ZestFast,
        /// <summary>HFA-style Full Threshold 4-2 (last seen; re-test if &gt; 4 dB from expected).</summary>
        FullThreshold,
        /// <summary>Plain OPI fourTwo staircase (mean of last two reversals). Research only.</summary>
        FourTwo,
        /// <summary>Threshold-related supraliminal screening.</summary>
        Screening,
    }
}
