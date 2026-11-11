using System;

namespace SimSpares.AdaptiveMotion.Core
{
    /// <summary>
    /// Online estimate of how the platform follows the command: delay (ms), coherence (peak normalized
    /// cross-correlation) and scale (measured units per command unit) — e.g. degrees of seat pitch per
    /// unit of commanded pitch mode. Push one sample pair per IMU frame. Not thread-safe.
    /// </summary>
    public sealed class LagEstimator
    {
        private readonly double[] _cmd, _meas;
        private readonly int _n, _maxLag;
        private readonly double _samplePeriodMs;
        private int _head, _count, _sinceCompute;

        public double DelayMs { get; private set; } = double.NaN;
        public double Coherence { get; private set; }
        public double Scale { get; private set; } = double.NaN;
        public bool Valid => Coherence >= MinCoherence && !double.IsNaN(DelayMs);
        public double MinCoherence { get; set; } = 0.8;
        public double MinCmdStd { get; set; } = 0.01;   // need some excitation

        public LagEstimator(double sampleRateHz = 250, double windowSec = 3, double maxLagMs = 200)
        {
            _samplePeriodMs = 1000.0 / sampleRateHz;
            _n = (int)(windowSec * sampleRateHz);
            _maxLag = (int)(maxLagMs / _samplePeriodMs);
            _cmd = new double[_n]; _meas = new double[_n];
        }

        public void Push(double cmd, double meas)
        {
            _cmd[_head] = cmd; _meas[_head] = meas;
            _head = (_head + 1) % _n;
            if (_count < _n) _count++;
            if (++_sinceCompute >= _n / 6 && _count == _n) { _sinceCompute = 0; Compute(); }
        }

        // index k = 0 oldest .. _n-1 newest
        private double C(int k) => _cmd[(_head + k) % _n];
        private double M(int k) => _meas[(_head + k) % _n];

        private void Compute()
        {
            int len = _n - _maxLag;
            double best = -2, bestScale = double.NaN; int bestLag = 0;
            for (int lag = 0; lag <= _maxLag; lag++)
            {
                // command at k, measurement at k+lag
                double sc = 0, sm = 0, scc = 0, smm = 0, scm = 0;
                for (int k = 0; k < len; k++)
                {
                    double c = C(k), m = M(k + lag);
                    sc += c; sm += m; scc += c * c; smm += m * m; scm += c * m;
                }
                double vc = scc - sc * sc / len, vm = smm - sm * sm / len, cov = scm - sc * sm / len;
                if (vc / len < MinCmdStd * MinCmdStd || vm <= 1e-12) { Coherence = 0; return; }
                double r = cov / Math.Sqrt(vc * vm);
                if (Math.Abs(r) > best) { best = Math.Abs(r); bestLag = lag; bestScale = cov / vc; }
            }
            Coherence = best;
            DelayMs = bestLag * _samplePeriodMs;
            Scale = bestScale;
        }
    }
}
