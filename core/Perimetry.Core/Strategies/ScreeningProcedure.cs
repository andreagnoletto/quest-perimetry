using System;

namespace Perimetry.Core.Strategies
{
    public enum ScreeningOutcome { Pending, Seen, SeenOnRetest, NotSeen }

    /// <summary>
    /// Threshold-related supraliminal screening: one stimulus brighter than the age-expected
    /// threshold by a fixed amount (default 6 dB); "not seen" is retested once (configurable).
    /// No threshold is estimated (FinalDb = NaN).
    /// </summary>
    public sealed class ScreeningProcedure : ILocationProcedure
    {
        public string Name => "Screening";
        public double StimulusDb { get; }
        public bool RetestNotSeen { get; }
        public ScreeningOutcome Outcome { get; private set; } = ScreeningOutcome.Pending;
        public int NumPresentations { get; private set; }

        public ScreeningProcedure(double stimulusDb, bool retestNotSeen)
        {
            StimulusDb = stimulusDb; RetestNotSeen = retestNotSeen;
        }

        public bool IsFinished => Outcome != ScreeningOutcome.Pending;
        public double NextStimulusDb() => StimulusDb;

        public void Update(double stimulusDb, bool seen)
        {
            if (IsFinished) throw new InvalidOperationException("screening already finished");
            NumPresentations++;
            if (seen) Outcome = NumPresentations == 1 ? ScreeningOutcome.Seen : ScreeningOutcome.SeenOnRetest;
            else if (!RetestNotSeen || NumPresentations >= 2) Outcome = ScreeningOutcome.NotSeen;
        }

        public double FinalDb => double.NaN;
        public string StopReason => Outcome.ToString();
        public bool IsCensoredLow => false;
        public bool IsCensoredHigh => false;
    }
}
