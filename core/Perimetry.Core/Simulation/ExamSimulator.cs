using System;
using Perimetry.Core.Exam;

namespace Perimetry.Core.Simulation
{
    /// <summary>Drives an <see cref="ExamSession"/> with a <see cref="SimulatedObserver"/> on a simulated clock.</summary>
    public static class ExamSimulator
    {
        public static ExamRecord Run(ExamSession session, SimulatedObserver observer, int maxPresentations = 5000)
        {
            double clock = 0;
            int count = 0;
            while (!session.IsFinished)
            {
                var req = session.Next();
                if (req == null) break;
                if (++count > maxPresentations) throw new InvalidOperationException("simulation did not terminate");
                clock += req.PreStimulusDelayMs;
                var res = observer.Respond(req, clock);
                clock += res.Responded && res.LatencyMs.HasValue ? res.LatencyMs.Value : req.ResponseWindowMs;
                session.Submit(res);
            }
            return session.BuildRecord();
        }
    }
}
