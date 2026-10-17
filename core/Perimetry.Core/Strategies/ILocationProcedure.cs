namespace Perimetry.Core.Strategies
{
    /// <summary>
    /// A threshold (or screening) procedure for ONE test location, written as a step-wise state
    /// machine in the style of OPI's *.start / *.step / *.stop / *.final functions, so that the
    /// algorithm can run in a different process from stimulus presentation (headset, simulator,
    /// live operator panel). All values are device dB.
    /// </summary>
    public interface ILocationProcedure
    {
        string Name { get; }
        bool IsFinished { get; }
        /// <summary>Stimulus to present next (device dB). Only valid while !IsFinished.</summary>
        double NextStimulusDb();
        /// <summary>Records the response to a presentation at <paramref name="stimulusDb"/>. Only call for presentations with good fixation.</summary>
        void Update(double stimulusDb, bool seen);
        /// <summary>Threshold estimate in device dB (NaN for screening).</summary>
        double FinalDb { get; }
        int NumPresentations { get; }
        /// <summary>"Rev", "Min", "Max", "Stop", "MaxPresentations", "Seen", "NotSeen", ...</summary>
        string StopReason { get; }
        /// <summary>True when the location was not seen at the brightest stimulus (reported as "&lt;0 dB").</summary>
        bool IsCensoredLow { get; }
        /// <summary>True when the location was seen at the dimmest stimulus.</summary>
        bool IsCensoredHigh { get; }
    }
}
