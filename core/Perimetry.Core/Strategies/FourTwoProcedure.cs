using System;

namespace Perimetry.Core.Strategies
{
    /// <summary>
    /// Single 4-2 dB staircase. Port of OPI R/fourTwo.r (Apache-2.0): steps of 4 dB until the first
    /// reversal, then 2 dB; stops after 2 reversals (result = mean of the last two stimuli), or
    /// after 2 "not seen" at the brightest / 2 "seen" at the dimmest stimulus.
    /// </summary>
    public sealed class FourTwoProcedure : ILocationProcedure
    {
        public string Name => "fourTwo";
        public double StartingEstimate { get; }
        public double MinStimulus { get; }
        public double MaxStimulus { get; }
        private double _currentLevel;
        private bool? _lastResponse;
        private double _prevStim = double.NaN, _lastStim = double.NaN;
        private double _stairResult = double.NaN;
        private string _finished = "Not";
        public int NumberOfReversals { get; private set; }
        public int CurrSeenLimit { get; private set; }
        public int CurrNotSeenLimit { get; private set; }
        public int NumPresentations { get; private set; }

        public FourTwoProcedure(double est, double minStimulus, double maxStimulus)
        {
            if (est < minStimulus || est > maxStimulus) throw new ArgumentException("fourTwo.start: est must be in the range of instRange");
            StartingEstimate = est; _currentLevel = est; MinStimulus = minStimulus; MaxStimulus = maxStimulus;
        }

        public bool IsFinished => _finished != "Not";
        public double NextStimulusDb() => _currentLevel;

        public void Update(double stimulusDb, bool seen)
        {
            if (IsFinished) throw new InvalidOperationException("fourTwo.step: stepping fourTwo staircase when it has already terminated");
            NumPresentations++;
            _prevStim = _lastStim; _lastStim = stimulusDb;
            if (stimulusDb == MinStimulus && !seen) CurrNotSeenLimit++;
            if (stimulusDb == MaxStimulus && seen) CurrSeenLimit++;
            if (NumPresentations > 1 && seen != _lastResponse) NumberOfReversals++;
            _lastResponse = seen;

            if (NumberOfReversals >= 2) { _finished = "Rev"; _stairResult = (_prevStim + _lastStim) / 2.0; }
            else if (CurrNotSeenLimit >= 2) { _finished = "Min"; _stairResult = MinStimulus; }
            else if (CurrSeenLimit >= 2) { _finished = "Max"; _stairResult = MaxStimulus; }
            else
            {
                double delta = (NumberOfReversals == 0 ? 4 : 2) * (seen ? 1 : -1);
                _currentLevel = Math.Min(MaxStimulus, Math.Max(MinStimulus, _currentLevel + delta));
            }
        }

        public double FinalDb => _stairResult;
        public string StopReason => _finished;
        public bool IsCensoredLow => _finished == "Min";
        public bool IsCensoredHigh => _finished == "Max";
    }
}
