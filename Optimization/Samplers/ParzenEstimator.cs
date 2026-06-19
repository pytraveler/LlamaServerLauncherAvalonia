using System;
using System.Collections.Generic;
using LlamaServerLauncher.Optimization.Distributions;

namespace LlamaServerLauncher.Optimization.Samplers;

public sealed class ParzenEstimatorParameters
{
    public double PriorWeight { get; init; } = 1.0;
    public bool ConsiderMagicClip { get; init; } = true;
    public bool ConsiderEndpoints { get; init; } = false;
    public Func<int, double[]> Weights { get; init; } = TpeMath.DefaultWeights;
}

public sealed class ParzenEstimator
{
    private readonly Distribution _distribution;

    private readonly double[] _weights;

    private readonly bool _isCategorical;
    private readonly bool _log;
    private readonly double _step;
    private readonly double _origLow;
    private readonly double _origHigh;
    private readonly double _boundLowT;
    private readonly double _boundHighT;
    private readonly double[] _mus;
    private readonly double[] _sigmas;

    private readonly int _nChoices;
    private readonly double[][] _catWeights;

    public ParzenEstimator(double[] observations, Distribution distribution, ParzenEstimatorParameters parameters)
    {
        _distribution = distribution;

        if (distribution is CategoricalDistribution cat)
        {
            _isCategorical = true;
            _nChoices = cat.Choices.Count;
            (_weights, _catWeights) = BuildCategorical(observations, _nChoices, parameters);
            _mus = Array.Empty<double>();
            _sigmas = Array.Empty<double>();
            return;
        }

        _isCategorical = false;
        (_log, _step, _origLow, _origHigh) = NumericBounds(distribution);

        double low = _origLow;
        double high = _origHigh;
        if (_step > 0)
        {
            low -= _step / 2.0;
            high += _step / 2.0;
        }
        if (_log)
        {
            low = Math.Log(low);
            high = Math.Log(high);
        }
        _boundLowT = low;
        _boundHighT = high;

        double[] mus = TransformObservations(observations);
        (_mus, _sigmas, _weights) = BuildNumeric(mus, low, high, parameters);
        _catWeights = Array.Empty<double[]>();
    }

    private static (bool log, double step, double low, double high) NumericBounds(Distribution d) => d switch
    {
        FloatDistribution f => (f.Log, f.Step ?? 0.0, f.Low, f.High),
        IntDistribution n => (n.Log, n.Step, n.Low, n.High),
        _ => throw new NotSupportedException($"ParzenEstimator: unsupported numeric distribution '{d.GetType().Name}'.")
    };

    private double[] TransformObservations(double[] observations)
    {
        var mus = new double[observations.Length];
        for (int i = 0; i < observations.Length; i++)
            mus[i] = _log ? Math.Log(observations[i]) : observations[i];
        return mus;
    }

    public double[] Sample(Random rng, int size)
    {
        var result = new double[size];
        for (int s = 0; s < size; s++)
        {
            int k = PickKernel(rng, _weights);
            result[s] = _isCategorical ? SampleCategorical(rng, k) : SampleNumeric(rng, k);
        }
        return result;
    }

    public double[] LogPdf(double[] samples)
    {
        var result = new double[samples.Length];
        for (int s = 0; s < samples.Length; s++)
            result[s] = _isCategorical ? LogPdfCategorical(samples[s]) : LogPdfNumeric(samples[s]);
        return result;
    }


    private static (double[] mus, double[] sigmas, double[] weights) BuildNumeric(
        double[] mus, double low, double high, ParzenEstimatorParameters p)
    {
        int n = mus.Length;
        double priorMu = 0.5 * (low + high);
        double priorSigma = high - low;

        if (n == 0)
            return (new[] { priorMu }, new[] { priorSigma }, new[] { 1.0 });

        int[] order = ArgSort(mus);
        var withEnds = new double[n + 2];
        withEnds[0] = low;
        for (int i = 0; i < n; i++) withEnds[i + 1] = mus[order[i]];
        withEnds[n + 1] = high;

        var sortedSigmas = new double[n];
        for (int i = 0; i < n; i++)
        {
            double left = withEnds[i + 1] - withEnds[i];
            double right = withEnds[i + 2] - withEnds[i + 1];
            sortedSigmas[i] = Math.Max(left, right);
        }
        if (!p.ConsiderEndpoints && withEnds.Length >= 4)
        {
            sortedSigmas[0] = withEnds[2] - withEnds[1];
            sortedSigmas[n - 1] = withEnds[n] - withEnds[n - 1];
        }

        var sigmas = new double[n];
        for (int i = 0; i < n; i++)
            sigmas[order[i]] = sortedSigmas[i];

        double maxSigma = high - low;
        double minSigma = p.ConsiderMagicClip
            ? (high - low) / Math.Min(100.0, 1.0 + (n + 1))
            : 1e-12;
        for (int i = 0; i < n; i++)
            sigmas[i] = Math.Clamp(sigmas[i], minSigma, maxSigma);

        var outMus = new double[n + 1];
        var outSigmas = new double[n + 1];
        Array.Copy(mus, outMus, n);
        Array.Copy(sigmas, outSigmas, n);
        outMus[n] = priorMu;
        outSigmas[n] = priorSigma;

        double[] w = p.Weights(n);
        var weights = new double[n + 1];
        Array.Copy(w, weights, n);
        weights[n] = p.PriorWeight;
        Normalize(weights);

        return (outMus, outSigmas, weights);
    }

    private double SampleNumeric(Random rng, int k)
    {
        double mu = _mus[k];
        double sigma = _sigmas[k];
        double a = (_boundLowT - mu) / sigma;
        double b = (_boundHighT - mu) / sigma;
        double z = TruncatedNormal.Rvs(rng, a, b, mu, sigma);
        if (_log) z = Math.Exp(z);
        if (_step > 0)
        {
            z = _origLow + Math.Round((z - _origLow) / _step) * _step;
            z = Math.Clamp(z, _origLow, _origHigh);
        }
        return z;
    }

    private double LogPdfNumeric(double x)
    {
        if (_step > 0)
        {
            double xLowT = _log ? Math.Log(x - _step / 2.0) : x - _step / 2.0;
            double xHighT = _log ? Math.Log(x + _step / 2.0) : x + _step / 2.0;
            double acc = double.NegativeInfinity;
            for (int k = 0; k < _mus.Length; k++)
            {
                double mu = _mus[k], sigma = _sigmas[k];
                double num = TruncatedNormal.LogGaussMass((xLowT - mu) / sigma, (xHighT - mu) / sigma);
                double den = TruncatedNormal.LogGaussMass((_boundLowT - mu) / sigma, (_boundHighT - mu) / sigma);
                acc = LogAddExp(acc, Math.Log(_weights[k]) + num - den);
            }
            return acc;
        }
        else
        {
            double xt = _log ? Math.Log(x) : x;
            double acc = double.NegativeInfinity;
            for (int k = 0; k < _mus.Length; k++)
            {
                double mu = _mus[k], sigma = _sigmas[k];
                double a = (_boundLowT - mu) / sigma;
                double b = (_boundHighT - mu) / sigma;
                double lp = TruncatedNormal.LogPdf(xt, a, b, mu, sigma);
                acc = LogAddExp(acc, Math.Log(_weights[k]) + lp);
            }
            return acc;
        }
    }


    private static (double[] weights, double[][] catWeights) BuildCategorical(
        double[] observations, int nChoices, ParzenEstimatorParameters p)
    {
        if (observations.Length == 0)
        {
            var uni = new double[nChoices];
            for (int c = 0; c < nChoices; c++) uni[c] = 1.0 / nChoices;
            return (new[] { 1.0 }, new[] { uni });
        }

        int nKernels = observations.Length + 1;
        var catWeights = new double[nKernels][];
        for (int k = 0; k < nKernels; k++)
        {
            var row = new double[nChoices];
            for (int c = 0; c < nChoices; c++) row[c] = p.PriorWeight / nKernels;
            catWeights[k] = row;
        }
        for (int i = 0; i < observations.Length; i++)
        {
            int idx = (int)Math.Round(observations[i]);
            catWeights[i][idx] += 1.0;
        }
        foreach (var row in catWeights)
            Normalize(row);

        double[] w = p.Weights(observations.Length);
        var weights = new double[nKernels];
        Array.Copy(w, weights, observations.Length);
        weights[nKernels - 1] = p.PriorWeight;
        Normalize(weights);

        return (weights, catWeights);
    }

    private double SampleCategorical(Random rng, int k)
    {
        double u = rng.NextDouble();
        double[] row = _catWeights[k];
        double cum = 0.0;
        for (int c = 0; c < row.Length; c++)
        {
            cum += row[c];
            if (u < cum) return c;
        }
        return row.Length - 1;
    }

    private double LogPdfCategorical(double x)
    {
        int idx = (int)Math.Round(x);
        double acc = double.NegativeInfinity;
        for (int k = 0; k < _weights.Length; k++)
            acc = LogAddExp(acc, Math.Log(_weights[k]) + Math.Log(_catWeights[k][idx]));
        return acc;
    }

    private static int PickKernel(Random rng, double[] weights)
    {
        double u = rng.NextDouble();
        double cum = 0.0;
        for (int k = 0; k < weights.Length; k++)
        {
            cum += weights[k];
            if (u < cum) return k;
        }
        return weights.Length - 1;
    }

    private static int[] ArgSort(double[] values)
    {
        var idx = new int[values.Length];
        for (int i = 0; i < idx.Length; i++) idx[i] = i;
        Array.Sort(idx, (x, y) => values[x].CompareTo(values[y]));
        return idx;
    }

    private static void Normalize(double[] w)
    {
        double sum = 0.0;
        foreach (var v in w) sum += v;
        if (sum <= 0) return;
        for (int i = 0; i < w.Length; i++) w[i] /= sum;
    }

    private static double LogAddExp(double p, double q)
    {
        if (double.IsNegativeInfinity(p)) return q;
        if (double.IsNegativeInfinity(q)) return p;
        double m = Math.Max(p, q);
        return m + Math.Log(Math.Exp(p - m) + Math.Exp(q - m));
    }
}
