using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Luminance;
using Perimetry.Headset.Logic;

namespace Perimetry.Headset.Logic.Tests
{
    public class TimelineTests
    {
        private static PresentationRequest Req(bool shown = true, int isi = 800) => new PresentationRequest
        {
            Sequence = 7, StimulusShown = shown, DurationMs = 200, ResponseWindowMs = 1000, PreStimulusDelayMs = isi,
        };

        [TestCase(72, 14)]
        [TestCase(90, 18)]
        [TestCase(120, 24)]
        public void StimulusLastsWholeFrames(double hz, int frames)
        {
            var tl = new PresentationTimeline(Req(), 0, hz);
            Assert.That(tl.FramesPlanned, Is.EqualTo(frames));
            double dt = 1000 / hz, t = 0;
            int visible = 0;
            while (!tl.IsDone) { t += dt; tl.Tick(t); if (tl.StimulusVisible) visible++; }
            Assert.That(visible, Is.EqualTo(frames));
            Assert.That(tl.OnsetMs.Value, Is.GreaterThanOrEqualTo(800).And.LessThan(800 + dt));
            var r = tl.Result(100);
            Assert.That(r.Responded, Is.False);
            Assert.That(r.OnsetMs, Is.EqualTo(tl.OnsetMs.Value - 100));
            Assert.That(t - tl.OnsetMs.Value, Is.GreaterThanOrEqualTo(1000).And.LessThan(1000 + dt), "ends when the window elapses");
        }

        [Test]
        public void PressBeforeOnset_IsEarly_PressInWindow_GivesLatency_AndEndsAfterStimulus()
        {
            var tl = new PresentationTimeline(Req(), 0, 90);
            double t = 0;
            Assert.Throws<InvalidOperationException>(() => tl.Result(0));
            tl.Press(300);
            Assert.That(tl.EarlyPresses, Is.EqualTo(1));
            while (tl.OnsetMs == null) { t += 1000 / 90.0; tl.Tick(t); }
            double onset = tl.OnsetMs.Value;
            tl.Press(onset + 150);
            tl.Press(onset + 400); // second press ignored
            t += 1000 / 90.0; tl.Tick(t);
            Assert.That(tl.IsDone, Is.False, "the stimulus still runs its 200 ms");
            while (!tl.IsDone) { t += 1000 / 90.0; tl.Tick(t); }
            Assert.That(t - onset, Is.LessThan(230));
            var r = tl.Result(0);
            Assert.That(r.Responded);
            Assert.That(r.LatencyMs, Is.EqualTo(150).Within(1e-9));
            tl.Press(t + 5); // after done: ignored
            Assert.That(tl.EarlyPresses, Is.EqualTo(1));
        }

        [Test]
        public void BlankCatchTrial_NeverDraws_AndLatePressIsIgnored()
        {
            var tl = new PresentationTimeline(Req(shown: false), 0, 90);
            double t = 0;
            while (!tl.IsDone) { t += 1000 / 90.0; tl.Tick(t); Assert.That(tl.StimulusVisible, Is.False); }
            Assert.That(tl.FramesShown, Is.EqualTo(0));
            var ab = new PresentationTimeline(Req(), 0, 90).Aborted(0, 50);
            Assert.That(ab.Discarded);
            Assert.That(ab.OnsetMs, Is.EqualTo(50));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PresentationTimeline(Req(), 0, 0));
            Assert.Throws<ArgumentNullException>(() => new PresentationTimeline(null, 0, 90));
        }
    }

    public class GeometryTests
    {
        private static readonly (double x, double y, double z) RightEye = (0.0315, 0, 0);

        /// <summary>SPEC §2: angular size verified automatically, error &lt; 5 %. Goldmann I-V from config, seen from the tested eye.</summary>
        [Test]
        public void AngularSize_AllGoldmannSizes_FromTestedEye_Within5Percent()
        {
            var cfg = Env.Config;
            foreach (GoldmannSize size in Enum.GetValues(typeof(GoldmannSize)))
            {
                double want = Stimulus.DiameterDeg(cfg.Stimulus, size);
                foreach (var (x, y) in new[] { (0.0, 0.0), (27.0, 3.0), (-21.0, -21.0), (9.0, 27.0) })
                {
                    var p = StimulusPlacement.Place(x, y, want, RightEye, cfg.Display.VirtualStimulusDistanceM);
                    // measure: angle between opposite rim points, from the eye
                    var (u, v) = Basis(p.DirX, p.DirY, p.DirZ);
                    var a = (p.X + u.x * p.RadiusM - RightEye.x, p.Y + u.y * p.RadiusM - RightEye.y, p.Z + u.z * p.RadiusM - RightEye.z);
                    var b = (p.X - u.x * p.RadiusM - RightEye.x, p.Y - u.y * p.RadiusM - RightEye.y, p.Z - u.z * p.RadiusM - RightEye.z);
                    double measured = Stimulus.AngleBetweenDeg(Norm(a), Norm(b));
                    Assert.That(Math.Abs(measured - want) / want, Is.LessThan(0.05), $"{size} at ({x},{y})");
                    Assert.That(StimulusPlacement.SubtendedDeg(RightEye, (p.X, p.Y, p.Z), p.RadiusM), Is.EqualTo(want).Within(1e-9));
                    // direction from the eye matches the field position
                    var dir = Norm((p.X - RightEye.x, p.Y - RightEye.y, p.Z - RightEye.z));
                    Assert.That(Stimulus.AngleBetweenDeg(dir, Stimulus.Direction(new FieldPoint(x, y))), Is.LessThan(1e-6));
                }
            }
            Assert.That(Stimulus.DiameterDeg(cfg.Stimulus, GoldmannSize.III), Is.EqualTo(0.431).Within(0.001));
        }

        [Test]
        public void PlacingFromHeadCentre_WouldMisplaceByAbout1_8Degrees()
        {
            var p = StimulusPlacement.Place(0, 0, 0.43, (0, 0, 0), 1.0);
            var fromEye = Norm((p.X - RightEye.x, p.Y, p.Z));
            double err = Stimulus.AngleBetweenDeg(fromEye, (0, 0, 1));
            Assert.That(err, Is.EqualTo(1.80).Within(0.02));
            Assert.Throws<ArgumentOutOfRangeException>(() => StimulusPlacement.Place(0, 0, 1, (0, 0, 0), 0));
        }

        [Test]
        public void DiscMesh_HasCircleArea_AndValidTriangles()
        {
            var (xy, tris) = DiscMesh.Build(64);
            Assert.That(DiscMesh.PolygonArea(xy), Is.EqualTo(Math.PI).Within(1e-9));
            Assert.That(DiscMesh.AreaEquivalentFactor(64), Is.EqualTo(1.0008).Within(0.0001));
            Assert.That(tris.Length, Is.EqualTo(64 * 3));
            Assert.That(tris.All(i => i >= 0 && i <= 64));
            // winding: clockwise seen from -Z, i.e. cross product z < 0 in XY
            int t0 = tris[1], t1 = tris[2];
            double cross = xy[t0 * 2] * xy[t1 * 2 + 1] - xy[t0 * 2 + 1] * xy[t1 * 2];
            Assert.That(cross, Is.LessThan(0));
            Assert.Throws<ArgumentOutOfRangeException>(() => DiscMesh.Build(4));
        }

        private static (double x, double y, double z) Norm((double x, double y, double z) a)
        {
            double n = Math.Sqrt(a.x * a.x + a.y * a.y + a.z * a.z);
            return (a.x / n, a.y / n, a.z / n);
        }

        private static ((double x, double y, double z), (double x, double y, double z)) Basis(double dx, double dy, double dz)
        {
            // any unit vector perpendicular to d
            var u = Math.Abs(dy) < 0.9 ? Norm((dz, 0, -dx)) : Norm((0, dz, -dy));
            return (u, (0, 0, 0));
        }
    }

    public class LuminanceTests
    {
        [Test]
        public void BayerMeanEqualsLevel_OverTileAndFrames()
        {
            foreach (var mode in new[] { DitherMode.Bayer4x4, DitherMode.Bayer4x4Temporal })
                foreach (double level in new[] { 0.3491, 0.35, 0.3523, 0.5, 0.9999 })
                {
                    double sum = 0; int n = 0;
                    for (int y = 0; y < 4; y++)
                        for (int x = 0; x < 4; x++) { sum += Dither.Code(level, x, y, 0, 8, mode); n++; }
                    Assert.That(sum / n, Is.EqualTo(level * 255).Within(1 / 32.0 + 1e-9), $"{mode} {level}");
                }
            // temporal: each pixel cycles through all 16 thresholds
            var th = Enumerable.Range(0, 16).Select(f => Dither.Threshold(1, 2, f, DitherMode.Bayer4x4Temporal)).Distinct().Count();
            Assert.That(th, Is.EqualTo(16));
            Assert.That(Dither.Code(0.35, 0, 0, 0, 8, DitherMode.None), Is.EqualTo((int)Math.Floor(0.35 * 255 + 0.5)));
            Assert.That(Dither.Code(2, 0, 0, 0, 8, DitherMode.Bayer4x4), Is.EqualTo(255));
            Assert.That(Dither.Parse("bayer4x4Temporal"), Is.EqualTo(DitherMode.Bayer4x4Temporal));
            Assert.Throws<FormatException>(() => Dither.Parse("fs"));
            Assert.That(Dither.LinearToSrgb(Dither.SrgbToLinear(0.42)), Is.EqualTo(0.42).Within(1e-12));
            Assert.That(Dither.LinearToSrgb(Dither.SrgbToLinear(0.01)), Is.EqualTo(0.01).Within(1e-12));
        }

        /// <summary>
        /// With an 8-bit panel, one code step near the 10 cd/m² background is ≈0.23 cd/m² (device ≈26 dB).
        /// Dithering must make the rendered mean luminance follow the requested dB across the whole
        /// stimulus range; without it the error is many dB at the dim end.
        /// </summary>
        [Test]
        public void DitheredLuminance_TracksDbScale_0To30()
        {
            var cfg = Env.Config.Display;
            var lum = new LuminanceMapper(cfg);
            var scale = lum.ScaleFor(Eye.OD);
            double worstDither = 0, worstNone = 0;
            var none = new LuminanceMapper(new Core.Config.DisplayConfig
            {
                BackgroundCdm2 = cfg.BackgroundCdm2, NominalMaxLuminanceCdm2 = cfg.NominalMaxLuminanceCdm2, HfaMaxStimulusAsb = cfg.HfaMaxStimulusAsb,
                PanelBitsPerChannel = 8, Dithering = "none",
            });
            // background is rendered dithered too: increments are measured against the rendered background
            double bgD = lum.RenderedMeanCdm2(Eye.OD, scale.BackgroundCdm2), bgN = none.RenderedMeanCdm2(Eye.OD, scale.BackgroundCdm2);
            for (int db = 0; db <= 30; db++)
            {
                double target = scale.DbToStimulusLuminance(db);
                double incD = lum.RenderedMeanCdm2(Eye.OD, target) - bgD;
                double incN = none.RenderedMeanCdm2(Eye.OD, target) - bgN;
                worstDither = Math.Max(worstDither, Math.Abs(scale.IncrementToDb(incD) - db));
                worstNone = Math.Max(worstNone, incN <= 0 ? 99 : Math.Abs(scale.IncrementToDb(incN) - db));
            }
            Assert.That(worstDither, Is.LessThan(0.5), "dithered error (dB)");
            Assert.That(worstNone, Is.GreaterThan(3), "without dithering the dim end collapses");
            Assert.That(lum.IsCalibrated, Is.False);
            Assert.That(lum.CalibrationRef, Is.Null);
        }

        [Test]
        public void MeasuredCalibration_IsUsedForScaleAndLevels()
        {
            var cal = LuminanceCalibration.NominalSrgb(80);
            cal.IsMeasured = true; cal.DeviceSerial = "Q3-123"; cal.MeasuredAtUtc = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            var lum = new LuminanceMapper(Env.Config.Display, cal);
            Assert.That(lum.IsCalibrated);
            Assert.That(lum.CalibrationRef, Does.StartWith("Q3-123@2026-10-01"));
            Assert.That(lum.ScaleFor(Eye.OS).MaxLuminanceCdm2, Is.EqualTo(80));
            Assert.That(lum.LevelFor(Eye.OD, 80), Is.EqualTo(1));
        }
    }

    public class StorageAndConfigTests
    {
        private string _dir;

        [SetUp] public void SetUp() => _dir = Path.Combine(Path.GetTempPath(), "perimetry-tests-" + Guid.NewGuid().ToString("N"));
        [TearDown] public void TearDown() { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); }

        [Test]
        public void ExamStorage_WritesSignedJson_DetectsTampering()
        {
            var key = ExamStorage.LoadOrCreateKey(Path.Combine(_dir, "keys", ExamStorage.KeyFileName));
            Assert.That(key, Has.Length.EqualTo(32));
            Assert.That(ExamStorage.LoadOrCreateKey(Path.Combine(_dir, "keys", ExamStorage.KeyFileName)), Is.EqualTo(key), "key persists");
            var store = new ExamStorage(Path.Combine(_dir, "exams"), key);
            var rec = new ExamRecord { ExamId = "OD-24-2-Zest-1", StartedUtc = new DateTime(2026, 10, 18, 9, 30, 0), Settings = new ExamSettings() };
            rec.Locations.Add(new LocationResult { ThresholdDb = double.NaN });
            store.SaveExam(rec);
            string path = store.LastWrittenPath;
            Assert.That(Path.GetFileName(path), Is.EqualTo("exam_20261018T093000_OD_OD-24-2-Zest-1.json"));
            Assert.That(store.Verify(path));
            string json = File.ReadAllText(path);
            Assert.That(json, Does.Contain("\"Strategy\": \"Zest\""));
            Assert.That(json, Does.Contain("não registrado na ANVISA"));
            var back = Newtonsoft.Json.JsonConvert.DeserializeObject<ExamRecord>(json, ExamStorage.Json);
            Assert.That(double.IsNaN(back.Locations[0].ThresholdDb));

            File.WriteAllText(path, json.Replace("Zest", "Zesty"));
            Assert.That(store.Verify(path), Is.False);
            Assert.That(new ExamStorage(Path.Combine(_dir, "exams"), new byte[32]).Verify(path), Is.False, "other key");
            Assert.That(store.Verify(path + ".missing"), Is.False);
            Assert.Throws<ArgumentException>(() => new ExamStorage(_dir, new byte[8]));
            Assert.Throws<ArgumentNullException>(() => new ExamStorage(_dir, null));

            store.SaveSessionLog(new SessionLog { SessionId = "abc", StartedUtc = new DateTime(2026, 10, 18) });
            Assert.That(store.Verify(store.LastWrittenPath));
            Assert.That(Directory.GetFiles(Path.Combine(_dir, "exams"), "*.tmp"), Is.Empty);
        }

        [Test]
        public void HeadsetConfig_LoadsFromRepoConfig_AndValidates()
        {
            var h = Env.Headset;
            Assert.That(h.EyeOrder, Is.EqualTo(new[] { Eye.OD, Eye.OS }));
            Assert.That(h.Training.Trials.Count, Is.InRange(3, 5), "SPEC §8: 3-5 training stimuli");
            Assert.That(h.Training.Trials.Any(t => !t.StimulusShown), "includes a blank trial");
            Assert.That(h.Preset("24-2-zest").Strategy, Is.EqualTo(Core.Strategies.StrategyKind.Zest));
            Assert.That(h.Presets.All(p => Env.Config.Grids.ContainsKey(p.GridId)));
            Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => h.Preset("x"));
            Assert.Throws<FormatException>(() => HeadsetConfig.Parse("{\"presets\": []}"));
            Assert.Throws<FormatException>(() => HeadsetConfig.Parse("{\"presets\": [{\"id\":\"a\",\"eyes\":[]}], \"training\": {\"trials\": [{}]}}"));
            Assert.Throws<FormatException>(() => HeadsetConfig.Parse("{\"presets\": [{\"id\":\"a\",\"eyes\":[\"OD\"]}], \"training\": {\"trials\": []}}"));
            Assert.Throws<FormatException>(() => HeadsetConfig.Parse("{\"presets\": [{\"id\":\"a\",\"eyes\":[\"OD\"]}], \"training\": {\"trials\": [{}]}, \"eyeOrder\": []}"));
            Assert.That(ExamOrder.Parse("{\"presetId\":\"24-2-zest\",\"ageYears\":71}").AgeYears, Is.EqualTo(71));
            Assert.That(Env.Config.Display.RefreshRateHz, Is.EqualTo(90));
            Assert.That(Env.Config.Timing.PauseHoldMs, Is.EqualTo(1500));
        }

        [Test]
        public void Messages_CompleteInBothLanguages_AndNeverShowResults()
        {
            var pt = PatientMessages.Keys(PatientMessages.PtBr).ToList();
            Assert.That(PatientMessages.Keys(PatientMessages.EnUs), Is.EquivalentTo(pt));
            Assert.That(PatientMessages.Get("done"), Is.EqualTo("Exame concluído. Aguarde o médico."));
            Assert.That(PatientMessages.Get("done", PatientMessages.EnUs), Does.StartWith("Test complete"));
            Assert.That(PatientMessages.Get("done", "fr-FR"), Is.EqualTo(PatientMessages.Get("done")), "falls back to pt-BR");
            Assert.That(PatientMessages.Get("missing.key"), Is.EqualTo("missing.key"));
            Assert.That(PatientMessages.EyeName(Eye.OS, PatientMessages.PtBr), Is.EqualTo("esquerdo"));
            Assert.That(PatientMessages.Has("welcome") && !PatientMessages.Has("nope"));
            foreach (var k in pt)
            {
                string m = PatientMessages.Get(k).ToLowerInvariant();
                foreach (var banned in new[] { " db", "md ", "psd", "glaucoma", "normal", "resultado", "limiar" })
                    Assert.That(m, Does.Not.Contain(banned), $"message {k} must not reveal results");
            }
        }
    }
}
