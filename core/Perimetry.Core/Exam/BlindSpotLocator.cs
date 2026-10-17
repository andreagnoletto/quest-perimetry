using System.Collections.Generic;
using System.Linq;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;

namespace Perimetry.Core.Exam
{
    /// <summary>
    /// Locates the physiological blind spot before the test (needed for Heijl-Krakau monitoring).
    /// Candidates are the nominal position (15°, -1.5° in OD convention) followed by the configured
    /// offsets; each is shown at the configured bright level. The first candidate not seen on every one
    /// of its presentations is accepted. If none qualifies, Heijl-Krakau is disabled for the exam and
    /// the report states that the blind spot was not located.
    /// </summary>
    public sealed class BlindSpotLocator
    {
        private readonly List<FieldPoint> _candidates;
        private readonly int _perCandidate;
        private int _candidate;
        private int _shownAtCandidate;

        public double StimulusDb { get; }
        public FieldPoint? Found { get; private set; }
        public bool IsFinished { get; private set; }

        public BlindSpotLocator(BlindSpotConfig cfg)
        {
            var nominal = new FieldPoint(cfg.NominalX, cfg.NominalY);
            _candidates = cfg.SearchOffsetsDeg.Select(o => nominal.Offset(o[0], o[1])).Distinct().ToList();
            _perCandidate = cfg.PresentationsPerCandidate;
            StimulusDb = cfg.StimulusDb;
        }

        /// <summary>Candidate to show next (OD convention).</summary>
        public FieldPoint Current => _candidates[_candidate];

        public void Update(bool seen)
        {
            if (IsFinished) return;
            if (seen)
            {
                _candidate++;
                _shownAtCandidate = 0;
            }
            else if (++_shownAtCandidate >= _perCandidate)
            {
                Found = _candidates[_candidate];
                IsFinished = true;
                return;
            }
            if (_candidate >= _candidates.Count) IsFinished = true;
        }
    }
}
