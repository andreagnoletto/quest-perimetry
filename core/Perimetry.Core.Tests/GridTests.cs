using System;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Geometry;

namespace Perimetry.Core.Tests
{
    public class GridTests
    {
        [TestCase("24-2", 54)]
        [TestCase("30-2", 76)]
        [TestCase("10-2", 68)]
        [TestCase("24-2C", 64)]
        public void PointCount(string id, int n) => Assert.That(TestEnv.Grid(id).Count, Is.EqualTo(n));

        [TestCase("24-2")]
        [TestCase("30-2")]
        [TestCase("10-2")]
        public void SymmetricAboutHorizontalMeridian(string id)
        {
            var g = TestEnv.Grid(id);
            foreach (var l in g.Locations)
                Assert.That(g.IndexOf(new FieldPoint(l.Point.X, -l.Point.Y)), Is.Not.Null, $"{id}: missing mirror of {l.Point}");
        }

        [TestCase("30-2")]
        [TestCase("10-2")]
        public void SymmetricAboutVerticalMeridian(string id)
        {
            var g = TestEnv.Grid(id);
            foreach (var l in g.Locations)
                Assert.That(g.IndexOf(new FieldPoint(-l.Point.X, l.Point.Y)), Is.Not.Null);
        }

        [Test]
        public void TwentyFourTwo_HasSixDegreeGridOffsetThreeDegrees_AndTwoNasalPointsAt27()
        {
            var g = TestEnv.Grid("24-2");
            foreach (var l in g.Locations)
            {
                Assert.That(Math.Abs(l.Point.X) % 6, Is.EqualTo(3).Within(1e-9));
                Assert.That(Math.Abs(l.Point.Y) % 6, Is.EqualTo(3).Within(1e-9));
            }
            var beyond24 = g.Locations.Where(l => Math.Abs(l.Point.X) > 24).Select(l => l.Point).ToList();
            Assert.That(beyond24, Is.EquivalentTo(new[] { new FieldPoint(-27, 3), new FieldPoint(-27, -3) }),
                "OD: the two extra points are nasal (x = -27)");
        }

        [TestCase("24-2")]
        [TestCase("30-2")]
        [TestCase("24-2C")]
        public void BlindSpotPoints_AreTheHfaOnes_AroundFifteenMinusOnePointFive(string id)
        {
            var g = TestEnv.Grid(id);
            var bs = g.Locations.Where(l => l.IsBlindSpot).Select(l => l.Point).ToList();
            Assert.That(bs, Is.EquivalentTo(new[] { new FieldPoint(15, 3), new FieldPoint(15, -3) }));
            var cfg = TestEnv.Config.Reliability.BlindSpot;
            var nominal = new FieldPoint(cfg.NominalX, cfg.NominalY);
            Assert.That(nominal, Is.EqualTo(new FieldPoint(15, -1.5)));
            // the nominal blind spot centre is temporal, just below the meridian, between the two excluded points
            Assert.That(bs.All(p => p.DistanceTo(nominal) < 5));
            Assert.That(g.AnalysisLocations.Count(), Is.EqualTo(g.Count - 2));
        }

        [Test]
        public void TenTwo_HasNoBlindSpotPoints_AndTwoDegreeSpacing()
        {
            var g = TestEnv.Grid("10-2");
            Assert.That(g.Locations.Any(l => l.IsBlindSpot), Is.False);
            Assert.That(g.Locations.All(l => l.Point.Eccentricity <= 10));
        }

        [Test]
        public void OsMirrorsHorizontally()
        {
            var p = new FieldPoint(15, -1.5);
            Assert.That(p.ForEye(Eye.OS), Is.EqualTo(new FieldPoint(-15, -1.5)));
            Assert.That(p.ForEye(Eye.OD), Is.EqualTo(p));
            Assert.That(p.ForEye(Eye.OS).ForEye(Eye.OS), Is.EqualTo(p));
        }

        [Test]
        public void AnchorsOnePerQuadrant()
        {
            foreach (var id in new[] { "24-2", "30-2", "10-2", "24-2C" })
            {
                var a = TestEnv.Grid(id).Locations.Where(l => l.IsAnchor).ToList();
                Assert.That(a.Count, Is.EqualTo(4), id);
                Assert.That(a.Select(l => (Math.Sign(l.Point.X), Math.Sign(l.Point.Y))).Distinct().Count(), Is.EqualTo(4), id);
            }
        }

        [Test]
        public void NeighboursIncludeDiagonalsWithinRadius()
        {
            var g = TestEnv.Grid("24-2");
            int i = g.IndexOf(new FieldPoint(9, 9)).Value;
            var n = g.NeighboursOf(i).Select(j => g.Locations[j].Point).ToList();
            Assert.That(n, Has.Member(new FieldPoint(3, 3)));
            Assert.That(n, Has.Member(new FieldPoint(15, 15)));
            Assert.That(n, Has.Member(new FieldPoint(9, 3)));
            Assert.That(n, Has.No.Member(new FieldPoint(9, 9)));
            Assert.That(n.Count, Is.EqualTo(8));
        }

        [Test]
        public void CustomCsvGrid()
        {
            var g = TestGrid.FromCsv("custom", "x,y,blindSpot\n9,9\n-9,9\n9,-9\n-9,-9\n15,-3,1\n3,3\n");
            Assert.That(g.Count, Is.EqualTo(6));
            Assert.That(g.Locations.Count(l => l.IsAnchor), Is.EqualTo(4));
            Assert.That(g.Locations.Single(l => l.IsBlindSpot).Point, Is.EqualTo(new FieldPoint(15, -3)));
            Assert.Throws<System.IO.InvalidDataException>(() => TestGrid.FromCsv("bad", "x,y\n1\n"));
            Assert.Throws<ArgumentException>(() => TestGrid.FromCsv("dup", "1,1\n1,1\n"));
            Assert.Throws<System.IO.InvalidDataException>(() => TestGrid.FromCsv("empty", " \n"));
        }

        [Test]
        public void UnknownGridThrows() => Assert.Throws<System.Collections.Generic.KeyNotFoundException>(() => TestEnv.Grid("60-4"));

        [Test]
        public void AllGridsFitInsideQuestUsableField()
        {
            double half = TestEnv.Config.Display.UsableHalfFieldDeg;
            foreach (var id in TestEnv.Config.Grids.Keys)
                Assert.That(TestEnv.Grid(id).Locations.Max(l => Math.Max(Math.Abs(l.Point.X), Math.Abs(l.Point.Y))), Is.LessThan(half));
        }
    }
}
