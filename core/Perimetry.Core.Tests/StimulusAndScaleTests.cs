using System;
using NUnit.Framework;
using Perimetry.Core.Geometry;
using Perimetry.Core.Luminance;

namespace Perimetry.Core.Tests
{
    public class StimulusAndScaleTests
    {
        [Test]
        public void GoldmannIII_Is043Degrees()
        {
            double d = Stimulus.DiameterDeg(TestEnv.Config.Stimulus, GoldmannSize.III);
            Assert.That(d, Is.EqualTo(0.431).Within(0.001));
        }

        [Test]
        public void GoldmannSizesDoubleInDiameter()
        {
            var c = TestEnv.Config.Stimulus;
            for (int s = 0; s < 4; s++)
                Assert.That(Stimulus.DiameterDeg(c, (GoldmannSize)(s + 1)) / Stimulus.DiameterDeg(c, (GoldmannSize)s), Is.EqualTo(2).Within(0.01));
        }

        /// <summary>
        /// Automatic angular-size check (acceptance: error &lt; 5%): render the disc at the virtual distance
        /// at several field positions and measure the angle subtended between opposite edge points.
        /// </summary>
        [TestCase(0, 0)]
        [TestCase(27, 3)]
        [TestCase(-21, -21)]
        [TestCase(3, 27)]
        public void RenderedDiscSubtendsGoldmannAngle_AnywhereInTheField(double x, double y)
        {
            var cfg = TestEnv.Config;
            foreach (GoldmannSize size in Enum.GetValues(typeof(GoldmannSize)))
            {
                double target = Stimulus.DiameterDeg(cfg.Stimulus, size);
                double dist = cfg.Display.VirtualStimulusDistanceM;
                double r = Stimulus.RenderRadiusM(target, dist);
                // disc is tangent to the sphere at the stimulus direction; take two opposite edge points
                var c = Stimulus.Direction(new FieldPoint(x, y));
                var up = Normalize(Cross(c, Math.Abs(c.y) > 0.99 ? (1.0, 0.0, 0.0) : (0.0, 1.0, 0.0)));
                var e1 = (c.x * dist + up.x * r, c.y * dist + up.y * r, c.z * dist + up.z * r);
                var e2 = (c.x * dist - up.x * r, c.y * dist - up.y * r, c.z * dist - up.z * r);
                double measured = Stimulus.AngleBetweenDeg(Normalize(e1), Normalize(e2));
                Assert.That(Math.Abs(measured - target) / target, Is.LessThan(0.05), $"size {size} at ({x},{y})");
            }
        }

        [Test]
        public void DirectionOfFixationIsForward()
        {
            var d = Stimulus.Direction(new FieldPoint(0, 0));
            Assert.That(d.z, Is.EqualTo(1).Within(1e-12));
            var right = Stimulus.Direction(new FieldPoint(90, 0));
            Assert.That(right.x, Is.EqualTo(1).Within(1e-12));
            Assert.That(Stimulus.AngleBetweenDeg(d, Stimulus.Direction(new FieldPoint(15, 0))), Is.EqualTo(15).Within(1e-9));
        }

        private static (double x, double y, double z) Cross((double x, double y, double z) a, (double x, double y, double z) b) =>
            (a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        private static (double x, double y, double z) Normalize((double x, double y, double z) v)
        {
            double n = Math.Sqrt(v.x * v.x + v.y * v.y + v.z * v.z);
            return (v.x / n, v.y / n, v.z / n);
        }

        [Test]
        public void DbScale_ZeroIsMaxIncrement_TenDbIsOneTenth()
        {
            var s = TestEnv.Scale;
            Assert.That(s.MaxIncrementCdm2, Is.EqualTo(90).Within(1e-9));
            Assert.That(s.DbToIncrement(0), Is.EqualTo(90).Within(1e-9));
            Assert.That(s.DbToIncrement(10), Is.EqualTo(9).Within(1e-9));
            Assert.That(s.DbToStimulusLuminance(0), Is.EqualTo(100).Within(1e-9));
            Assert.That(s.IncrementToDb(0.9), Is.EqualTo(20).Within(1e-9));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.IncrementToDb(0));
            Assert.That(s.IsCalibrated, Is.False);
        }

        [Test]
        public void DbScale_RoundTrip([Values(-3, 0, 1.5, 17, 30)] double db)
        {
            var s = TestEnv.Scale;
            Assert.That(s.IncrementToDb(s.DbToIncrement(db)), Is.EqualTo(db).Within(1e-9));
            Assert.That(s.FromHfaEquivalent(s.ToHfaEquivalent(db)), Is.EqualTo(db).Within(1e-12));
        }

        [Test]
        public void HfaEquivalentOffset_NominalQuestIsAbout15Point5Db()
        {
            // 10000 asb / pi = 3183 cd/m^2 HFA max increment vs 90 cd/m^2 on the device
            Assert.That(TestEnv.Scale.HfaOffsetDb, Is.EqualTo(10 * Math.Log10(10000 / Math.PI / 90)).Within(1e-9));
            Assert.That(TestEnv.Scale.HfaOffsetDb, Is.EqualTo(15.49).Within(0.01));
        }

        [Test]
        public void DbScale_MatchesOpiDbTocd()
        {
            // OPI: dbTocd(db, maxStim = 10000/pi) = maxStim * 10^(-db/10). With the HFA max increment the
            // HFA-equivalent dB of an increment reproduces OPI exactly.
            var hfaLike = new DbScale(0, 10000 / Math.PI, 10000, true);
            Assert.That(hfaLike.HfaOffsetDb, Is.EqualTo(0).Within(1e-12));
            Assert.That(hfaLike.DbToIncrement(30), Is.EqualTo(10000 / Math.PI * Math.Pow(10, -3)).Within(1e-9));
            Assert.Throws<ArgumentException>(() => new DbScale(10, 10, 10000, false));
        }

        [Test]
        public void Calibration_NominalSrgb_IsMonotonicAndInvertible()
        {
            var cal = LuminanceCalibration.NominalSrgb(100);
            Assert.That(cal.IsMeasured, Is.False);
            Assert.That(cal.MaxLuminance(Eye.OD), Is.EqualTo(100).Within(1e-9));
            Assert.That(cal.LuminanceAt(Eye.OS, 0.5), Is.EqualTo(21.4).Within(0.1)); // sRGB mid-grey
            foreach (var l in new[] { 0.1, 10.0, 10.09, 55.0, 99.9 })
                Assert.That(cal.LuminanceAt(Eye.OD, cal.LevelFor(Eye.OD, l)), Is.EqualTo(l).Within(1e-6));
            Assert.That(cal.LevelFor(Eye.OD, -1), Is.EqualTo(0));
            Assert.That(cal.LevelFor(Eye.OD, 1000), Is.EqualTo(1));
            Assert.That(cal.LuminanceAt(Eye.OD, -1), Is.EqualTo(0));
            Assert.That(cal.LuminanceAt(Eye.OD, 2), Is.EqualTo(100));
        }

        [Test]
        public void Calibration_FineStepsNearThresholdAreResolvableInFloat()
        {
            // 1 dB steps around 20 dB device (0.9 cd/m^2 increment over a 10 cd/m^2 background) map to distinct drive levels
            var cal = LuminanceCalibration.NominalSrgb(100);
            var s = TestEnv.Scale;
            double a = cal.LevelFor(Eye.OD, s.DbToStimulusLuminance(20)), b = cal.LevelFor(Eye.OD, s.DbToStimulusLuminance(21));
            Assert.That(a, Is.GreaterThan(b));
            // and the step is below one 8-bit code, which is why the renderer must dither
            Assert.That((a - b) * 255, Is.LessThan(1));
        }

        [Test]
        public void Calibration_JsonRoundTripAndValidation()
        {
            var cal = LuminanceCalibration.NominalSrgb(80);
            cal.IsMeasured = true;
            cal.DeviceSerial = "Q3-TEST";
            cal.ValidUntilUtc = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var back = LuminanceCalibration.FromJson(cal.ToJson());
            Assert.That(back.DeviceSerial, Is.EqualTo("Q3-TEST"));
            Assert.That(back.MaxLuminance(Eye.OS), Is.EqualTo(80).Within(1e-9));
            Assert.That(back.IsExpired(new DateTime(2027, 2, 1)), Is.True);
            Assert.That(back.IsExpired(new DateTime(2026, 2, 1)), Is.False);
            var scale = DbScale.FromCalibration(TestEnv.Config.Display, back, Eye.OD);
            Assert.That(scale.IsCalibrated, Is.True);
            Assert.That(scale.MaxLuminanceCdm2, Is.EqualTo(80).Within(1e-9));

            Assert.Throws<FormatException>(() => LuminanceCalibration.FromJson("{\"Eyes\":{\"OD\":{\"Samples\":[{\"Level\":0,\"Cdm2\":0}]}}}"));
            Assert.Throws<FormatException>(() => LuminanceCalibration.FromJson("{\"Eyes\":{\"OD\":{\"Samples\":[{\"Level\":0.5,\"Cdm2\":0},{\"Level\":0.4,\"Cdm2\":1}]}}}"));
            Assert.Throws<FormatException>(() => LuminanceCalibration.FromJson("{\"Eyes\":{\"OD\":{\"Samples\":[{\"Level\":0,\"Cdm2\":5},{\"Level\":1,\"Cdm2\":1}]}}}"));
        }
    }
}
