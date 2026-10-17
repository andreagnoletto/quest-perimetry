using System;

namespace Perimetry.Core.Strategies
{
    /// <summary>
    /// HFA-style Full Threshold for one location. Port of OPI R/full_threshold.r FT.start / FT.step
    /// (Apache-2.0): a 4-2 staircase whose result is the LAST SEEN stimulus; if the first staircase
    /// ends by reversals and differs from the starting estimate by more than 4 dB, a second
    /// staircase is run starting from the first result and its last-seen value is reported.
    /// </summary>
    public sealed class FullThresholdProcedure : ILocationProcedure
    {
        public string Name => "FT";
        public double StartingEstimate { get; }
        public double MinStimulus { get; }
        public double MaxStimulus { get; }
        public double RetestIfDiffersByDb { get; }
        public bool UseSecondStaircase { get; }

        private double _currentLevel;
        private double? _lastSeen;
        private bool? _lastResponse;
        public int NumberOfReversals { get; private set; }
        public int CurrSeenLimit { get; private set; }
        public int CurrNotSeenLimit { get; private set; }
        public int NumPresentations { get; private set; }
        public double FirstStairResult { get; private set; } = double.NaN;
        public double SecondStairResult { get; private set; } = double.NaN;
        private bool _finished;
        private string _stopReason = "Not";

        public FullThresholdProcedure(double est, double minStimulus, double maxStimulus, double retestIfDiffersByDb = 4, bool useSecondStaircase = true)
        {
            if (est < minStimulus || est > maxStimulus) throw new ArgumentException("FT.start: est must be in the range of instRange");
            StartingEstimate = est; _currentLevel = est; MinStimulus = minStimulus; MaxStimulus = maxStimulus;
            RetestIfDiffersByDb = retestIfDiffersByDb; UseSecondStaircase = useSecondStaircase;
        }

        public bool IsFinished => _finished;
        public double NextStimulusDb() => _currentLevel;

        private (double final, string reason) CurrentStairFinal()
        {
            // FT.final.details
            if (CurrSeenLimit == 2) return (MaxStimulus, "Max");
            if (CurrNotSeenLimit == 2) return (MinStimulus, "Min");
            return (_lastSeen ?? double.NaN, "Reversals");
        }

        public void Update(double stimulusDb, bool seen)
        {
            if (_finished) throw new InvalidOperationException("FT.step: stepping FT staircase when it has already terminated");
            NumPresentations++;
            if (seen) _lastSeen = stimulusDb;
            if (stimulusDb == MinStimulus && !seen) CurrNotSeenLimit++;
            if (stimulusDb == MaxStimulus && seen) CurrSeenLimit++;
            if (NumPresentations > 1 && _lastResponse.HasValue && seen != _lastResponse.Value) NumberOfReversals++;
            _lastResponse = seen;

            bool thisStairStops = NumberOfReversals >= 2 || CurrNotSeenLimit >= 2 || CurrSeenLimit >= 2;
            if (!thisStairStops)
            {
                double delta = (NumberOfReversals == 0 ? 4 : 2) * (seen ? 1 : -1);
                _currentLevel = Math.Min(MaxStimulus, Math.Max(MinStimulus, _currentLevel + delta));
                return;
            }

            if (double.IsNaN(FirstStairResult))
            {
                var (final, reason) = CurrentStairFinal();
                FirstStairResult = final;
                _stopReason = reason;
                if (UseSecondStaircase && reason == "Reversals" && Math.Abs(FirstStairResult - StartingEstimate) > RetestIfDiffersByDb)
                {
                    // OPI resets the counters but keeps lastResponse, so the first response of the
                    // second staircase can already count as a reversal. Kept identical for cross-checking.
                    _currentLevel = final;
                    CurrSeenLimit = 0;
                    CurrNotSeenLimit = 0;
                    NumberOfReversals = 0;
                    _lastSeen = null;
                }
                else
                {
                    _finished = true;
                }
            }
            else
            {
                // OPI stores lastSeen as secondStairResult but FT.final() re-derives the result from the
                // current staircase state (Max / Min / lastSeen); we report the latter, as OPI does.
                SecondStairResult = _lastSeen ?? double.NaN;
                var (final, reason) = CurrentStairFinal();
                _final = final;
                _stopReason = "Second" + reason;
                _finished = true;
                return;
            }
            if (_finished) _final = FirstStairResult;
        }

        private double _final = double.NaN;

        public double FinalDb => _finished ? _final : double.NaN;

        public bool RanSecondStaircase => _stopReason.StartsWith("Second", StringComparison.Ordinal);
        public string StopReason => _finished ? _stopReason : "Not";
        public bool IsCensoredLow => _finished && (_stopReason == "Min" || _stopReason == "SecondMin");
        public bool IsCensoredHigh => _finished && (_stopReason == "Max" || _stopReason == "SecondMax");
    }
}
