using System;

namespace LlamaServerLauncher.Optimization.Samplers;

internal static class TruncatedNormal
{
    private const double Eps = 2.220446049250313e-16;
    private static readonly double LogSqrt2Pi = 0.5 * Math.Log(2.0 * Math.PI);
    private static readonly double NdtriExpApproxC = Math.Sqrt(3.0) / Math.PI;
    private static readonly double Sqrt2 = Math.Sqrt(2.0);


    private static double Log1p(double x) =>
        Math.Abs(x) < 1e-5 ? x - 0.5 * x * x : Math.Log(1.0 + x);

    private static double Expm1(double x) =>
        Math.Abs(x) < 1e-5 ? x + 0.5 * x * x : Math.Exp(x) - 1.0;

    private static double LogAddExp(double p, double q)
    {
        if (double.IsNegativeInfinity(p)) return q;
        if (double.IsNegativeInfinity(q)) return p;
        double m = Math.Max(p, q);
        return m + Log1p(Math.Exp(-Math.Abs(p - q)));
    }

    private static double LogDiff(double p, double q) => p + Log1p(-Math.Exp(q - p));

    private static double Erfc(double x)
    {
        double z = Math.Abs(x);
        double t = 1.0 / (1.0 + 0.5 * z);
        double ans = t * Math.Exp(-z * z - 1.26551223 + t * (1.00002368 + t * (0.37409196 +
            t * (0.09678418 + t * (-0.18628806 + t * (0.27886807 + t * (-1.13520398 +
            t * (1.48851587 + t * (-0.82215223 + t * 0.17087277)))))))));
        return x >= 0.0 ? ans : 2.0 - ans;
    }

    public static double Ndtr(double a) => 0.5 * Erfc(-a / Sqrt2);

    public static double LogNdtr(double a)
    {
        if (a > 6.0)
            return -Ndtr(-a);
        if (a > -20.0)
            return Math.Log(Ndtr(a));

        double logLhs = -0.5 * a * a - Math.Log(-a) - LogSqrt2Pi;
        double lastTotal = 0.0;
        double rhs = 1.0;
        double numerator = 1.0;
        double denomFactor = 1.0;
        double denomCons = 1.0 / (a * a);
        int sign = 1;
        int i = 0;
        while (Math.Abs(lastTotal - rhs) > Eps)
        {
            i++;
            lastTotal = rhs;
            sign = -sign;
            denomFactor *= denomCons;
            numerator *= 2 * i - 1;
            rhs += sign * numerator * denomFactor;
        }
        return logLhs + Math.Log(rhs);
    }

    public static double NormLogPdf(double x) => -(x * x) / 2.0 - LogSqrt2Pi;

    public static double LogGaussMass(double a, double b)
    {
        if (b <= 0.0)
            return LogDiff(LogNdtr(b), LogNdtr(a));
        if (a > 0.0)
            return LogDiff(LogNdtr(-a), LogNdtr(-b));
        return Log1p(-Ndtr(a) - Ndtr(-b));
    }

    public static double NdtriExp(double y)
    {
        bool flipped = y > -1e-2;
        double z = flipped ? Math.Log(-Expm1(y)) : y;

        double x = z < -5.0
            ? -Math.Sqrt(-2.0 * (z + LogSqrt2Pi))
            : -NdtriExpApproxC * Math.Log(Expm1(-z));

        for (int iter = 0; iter < 100; iter++)
        {
            double logNdtrX = LogNdtr(x);
            double logNormPdfX = -0.5 * x * x - LogSqrt2Pi;
            double dx = (logNdtrX - z) * Math.Exp(logNdtrX - logNormPdfX);
            x -= dx;
            if (Math.Abs(dx) < 1e-8 * Math.Abs(x))
                break;
        }

        return flipped ? -x : x;
    }

    public static double Ppf(double q, double a, double b)
    {
        if (a == b) return double.NaN;
        if (q <= 0.0) return a;
        if (q >= 1.0) return b;

        double logMass = LogGaussMass(a, b);
        if (a < 0.0)
        {
            double logPhiX = LogAddExp(LogNdtr(a), Math.Log(q) + logMass);
            return NdtriExp(logPhiX);
        }
        else
        {
            double logPhiX = LogAddExp(LogNdtr(-b), Log1p(-q) + logMass);
            return -NdtriExp(logPhiX);
        }
    }

    public static double Rvs(Random rng, double a, double b, double loc, double scale)
    {
        double u = rng.NextDouble();
        return Ppf(u, a, b) * scale + loc;
    }

    public static double LogPdf(double x, double a, double b, double loc, double scale)
    {
        double xs = (x - loc) / scale;
        if (a == b) return double.NaN;
        if (xs < a || xs > b) return double.NegativeInfinity;
        return NormLogPdf(xs) - LogGaussMass(a, b) - Math.Log(scale);
    }
}
