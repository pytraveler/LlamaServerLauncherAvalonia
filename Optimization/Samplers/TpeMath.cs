using System;

namespace LlamaServerLauncher.Optimization.Samplers;

public static class TpeMath
{
    public static int DefaultGamma(int n) => Math.Min((int)Math.Ceiling(0.1 * n), 25);

    public static double[] DefaultWeights(int x)
    {
        if (x == 0)
            return Array.Empty<double>();
        if (x < 25)
        {
            var ones = new double[x];
            for (int i = 0; i < x; i++) ones[i] = 1.0;
            return ones;
        }

        int rampLen = x - 25;
        var w = new double[x];
        for (int i = 0; i < rampLen; i++)
        {
            double t = rampLen == 1 ? 1.0 : (double)i / (rampLen - 1);
            w[i] = (1.0 / x) + t * (1.0 - 1.0 / x);
        }
        for (int i = rampLen; i < x; i++) w[i] = 1.0;
        return w;
    }
}
