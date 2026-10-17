using System;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Tests
{
    public class MathTests
    {
        // Reference values from R 4.3: pnorm(), qnorm()
        [TestCase(0, 0.5)]
        [TestCase(1, 0.8413447460685429)]
        [TestCase(-2, 0.02275013194817921)]
        [TestCase(-6, 9.865876450376981e-10)]
        [TestCase(8, 0.9999999999999993)]
        [TestCase(-40, 0)]
        public void StandardCdf_MatchesR(double x, double expected) =>
            Assert.That(NormalDistribution.StandardCdf(x), Is.EqualTo(expected).Within(Math.Max(1e-14, expected * 1e-12)));

        [TestCase(0.975, 1.959963984540054)]
        [TestCase(0.005, -2.575829303548900)]
        [TestCase(0.5, 0)]
        [TestCase(0.99, 2.326347874040841)]
        public void Quantile_MatchesR(double p, double expected) =>
            Assert.That(NormalDistribution.StandardQuantile(p), Is.EqualTo(expected).Within(1e-9));

        [Test]
        public void QuantileEdges()
        {
            Assert.That(double.IsNegativeInfinity(NormalDistribution.StandardQuantile(0)));
            Assert.That(double.IsPositiveInfinity(NormalDistribution.StandardQuantile(1)));
            Assert.That(NormalDistribution.Pdf(0, 0, 1), Is.EqualTo(0.3989422804014327).Within(1e-15));
            Assert.That(NormalDistribution.Cdf(25, 20, 2.5), Is.EqualTo(NormalDistribution.StandardCdf(2)).Within(1e-15));
        }

        [Test]
        public void Rng_IsDeterministicAndInRange()
        {
            var a = new Rng(42); var b = new Rng(42);
            for (int i = 0; i < 100; i++) Assert.That(a.NextUInt64(), Is.EqualTo(b.NextUInt64()));
            var r = new Rng(7);
            for (int i = 0; i < 1000; i++)
            {
                double d = r.NextDouble();
                Assert.That(d, Is.GreaterThanOrEqualTo(0).And.LessThan(1));
                int k = r.NextInt(-3, 4);
                Assert.That(k, Is.InRange(-3, 3));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => r.NextInt(3, 3));
            Assert.That(new Rng(1).NextUInt64(), Is.Not.EqualTo(new Rng(2).NextUInt64()));
            Assert.That(new Rng(5).Seed, Is.EqualTo(5UL));
        }

        [Test]
        public void Rng_GaussianMoments()
        {
            var r = new Rng(3);
            var v = Enumerable.Range(0, 20000).Select(_ => r.Gaussian(10, 2)).ToArray();
            Assert.That(Stats.Mean(v), Is.EqualTo(10).Within(0.05));
            Assert.That(Stats.Sd(v), Is.EqualTo(2).Within(0.05));
            var u = Enumerable.Range(0, 20000).Select(_ => r.Bernoulli(0.3) ? 1.0 : 0.0).Average();
            Assert.That(u, Is.EqualTo(0.3).Within(0.015));
        }

        [Test]
        public void Stats_QuantileType7_MatchesR()
        {
            var x = new double[] { 5, 1, 4, 2, 3 };
            Assert.That(Stats.Quantile(x, 0.25), Is.EqualTo(2));   // quantile(1:5, .25)
            Assert.That(Stats.Quantile(x, 0.1), Is.EqualTo(1.4).Within(1e-12));
            Assert.That(Stats.Quantile(new double[] { 7 }, 0.3), Is.EqualTo(7));
            Assert.That(double.IsNaN(Stats.Quantile(new double[0], 0.5)));
            Assert.That(Stats.WeightedMean(new double[] { 1, 3 }, new double[] { 3, 1 }), Is.EqualTo(1.5));
            Assert.That(double.IsNaN(Stats.Sd(new double[] { 1 })));
            Assert.That(double.IsNaN(Stats.Mean(new double[0])));
            var (a, b) = Stats.LinearFit(new double[] { 0, 1, 2 }, new double[] { 1, 3, 5 });
            Assert.That(a, Is.EqualTo(1).Within(1e-12));
            Assert.That(b, Is.EqualTo(2).Within(1e-12));
            Assert.Throws<ArgumentException>(() => Stats.LinearFit(new double[] { 1 }, new double[] { 1 }));
        }
    }
}
