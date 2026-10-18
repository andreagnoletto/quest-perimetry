using System;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;

namespace Perimetry.Core.Tests
{
    public class Phase2CoreTests
    {
        [Test]
        public void LoadFromFiles_MatchesDirectoryLoad_AndHash()
        {
            var files = ClinicalConfig.ReadFiles(TestEnv.ConfigDir);
            var fromFiles = ClinicalConfig.LoadFromFiles(files);
            Assert.That(fromFiles.Hash, Is.EqualTo(TestEnv.Config.Hash));
            Assert.That(fromFiles.Grids.Keys, Is.EquivalentTo(TestEnv.Config.Grids.Keys));
            Assert.That(fromFiles.Display.BackgroundCdm2, Is.EqualTo(TestEnv.Config.Display.BackgroundCdm2));
            Assert.That(files.Keys, Has.Member("grids/24-2.json"));

            // CRLF checkouts hash identically
            var crlf = files.ToDictionary(k => k.Key, k => k.Value.Replace("\r\n", "\n").Replace("\n", "\r\n"));
            Assert.That(ClinicalConfig.ComputeHash(crlf), Is.EqualTo(fromFiles.Hash));

            var missing = files.Where(f => f.Key != "timing.json").ToDictionary(k => k.Key, k => k.Value);
            Assert.Throws<System.IO.FileNotFoundException>(() => ClinicalConfig.LoadFromFiles(missing));
        }

        [Test]
        public void DiscardedPresentation_IsLoggedAndReissued_WithoutAffectingStrategy()
        {
            var s = new ExamSession(new ExamSettings { Seed = 3, FixationMode = FixationMode.CentralEyeTracking,
                FalsePositiveCatchTrials = false, FalseNegativeCatchTrials = false }, TestEnv.Config, TestEnv.Norm(), TestEnv.Scale);
            var r1 = s.Next();
            s.Submit(new PresentationResult { Sequence = r1.Sequence, Responded = true, LatencyMs = 400, Discarded = true, OnsetMs = 1000 });
            var rec = s.BuildRecord();
            Assert.That(rec.Presentations.Single().Disposition, Is.EqualTo("aborted"));
            Assert.That(rec.Reliability.AbortedPresentations, Is.EqualTo(1));
            Assert.That(rec.Reliability.TotalPresentations, Is.EqualTo(0));
            Assert.That(rec.Locations.All(l => l.Presentations == 0));

            Assert.Throws<InvalidOperationException>(() => { var p = s.Next(); s.RecordPause(0, 1, "patient"); });
            var pending = s.BuildRecord().Presentations.Count;
            Assert.That(pending, Is.EqualTo(1));
        }

        [Test]
        public void DiscardedCatchTrial_IsNotCounted_AndPausesAreRecorded()
        {
            var s = new ExamSession(new ExamSettings { Seed = 5, FixationMode = FixationMode.CentralUnmonitored,
                FalsePositiveCatchTrials = true, FalseNegativeCatchTrials = false }, TestEnv.Config, TestEnv.Norm(), TestEnv.Scale);
            double t = 0;
            bool discardedFp = false;
            for (int i = 0; i < 200 && !discardedFp; i++)
            {
                var r = s.Next();
                bool isFp = r.Kind == PresentationKind.FalsePositiveCatch;
                s.Submit(new PresentationResult { Sequence = r.Sequence, Responded = !isFp && r.Db < 20, LatencyMs = 450, OnsetMs = t, Discarded = isFp });
                t += 1500;
                if (isFp)
                {
                    discardedFp = true;
                    // the catch trial is owed again on the next request
                    Assert.That(s.Next().Kind, Is.EqualTo(PresentationKind.FalsePositiveCatch));
                }
            }
            Assert.That(discardedFp);
            Assert.That(s.BuildRecord().Reliability.FalsePositives.Trials, Is.EqualTo(0));

            var s2 = new ExamSession(new ExamSettings { Seed = 5 }, TestEnv.Config, TestEnv.Norm(), TestEnv.Scale);
            s2.RecordPause(1000, 4000, "patient");
            s2.RecordPause(9000, 9500, "headsetRemoved");
            Assert.That(s2.BuildRecord().Reliability.PausedMs, Is.EqualTo(3500));
            Assert.Throws<ArgumentException>(() => s2.RecordPause(10, 5, "operator"));
        }
    }
}
