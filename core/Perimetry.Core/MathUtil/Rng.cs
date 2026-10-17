using System;

namespace Perimetry.Core.MathUtil
{
    /// <summary>
    /// Deterministic pseudo-random generator (xoshiro256**, seeded with SplitMix64).
    /// Used instead of System.Random so that a seed reproduces an exam identically on
    /// .NET, Mono and IL2CPP (Unity) runtimes.
    /// </summary>
    public sealed class Rng
    {
        private ulong _s0, _s1, _s2, _s3;
        private double? _spareGaussian;

        public ulong Seed { get; }

        public Rng(ulong seed)
        {
            Seed = seed;
            ulong x = seed;
            _s0 = SplitMix(ref x);
            _s1 = SplitMix(ref x);
            _s2 = SplitMix(ref x);
            _s3 = SplitMix(ref x);
        }

        private static ulong SplitMix(ref ulong x)
        {
            ulong z = x += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        private static ulong Rotl(ulong x, int k) => (x << k) | (x >> (64 - k));

        public ulong NextUInt64()
        {
            ulong result = Rotl(_s1 * 5, 7) * 9;
            ulong t = _s1 << 17;
            _s2 ^= _s0;
            _s3 ^= _s1;
            _s1 ^= _s2;
            _s0 ^= _s3;
            _s2 ^= t;
            _s3 = Rotl(_s3, 45);
            return result;
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => (NextUInt64() >> 11) * (1.0 / (1UL << 53));

        public double Uniform(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            ulong range = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt64() % range);
        }

        public bool Bernoulli(double p) => NextDouble() < p;

        /// <summary>Standard normal deviate (Marsaglia polar method).</summary>
        public double NextGaussian()
        {
            if (_spareGaussian.HasValue)
            {
                double s = _spareGaussian.Value;
                _spareGaussian = null;
                return s;
            }
            double u, v, r2;
            do
            {
                u = 2.0 * NextDouble() - 1.0;
                v = 2.0 * NextDouble() - 1.0;
                r2 = u * u + v * v;
            } while (r2 >= 1.0 || r2 == 0.0);
            double f = Math.Sqrt(-2.0 * Math.Log(r2) / r2);
            _spareGaussian = v * f;
            return u * f;
        }

        public double Gaussian(double mean, double sd) => mean + sd * NextGaussian();

        /// <summary>Derives an independent child generator (for e.g. the simulated observer).</summary>
        public Rng Fork() => new Rng(NextUInt64());
    }
}
