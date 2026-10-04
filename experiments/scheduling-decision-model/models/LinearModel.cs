using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Harborline.Experiments.SchedulingDecisionModel.Models;

/// <summary>A standardised linear scorer: score = bias + Σ w·(x − mean)/std. Versioned and hashed.</summary>
public sealed record LinearModel(
    string Kind,
    string FeatureVersion,
    string[] FeatureNames,
    double[] Mean,
    double[] Std,
    double[] Weights,
    double Bias,
    string TrainedOn,
    double L2)
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    public double Score(ReadOnlySpan<double> x)
    {
        var s = Bias;
        for (var i = 0; i < Weights.Length; i++)
        {
            s += Weights[i] * ((x[i] - Mean[i]) / Std[i]);
        }

        return s;
    }

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public string Sha256() => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(ToJson())));

    /// <summary>Loads and validates a model; refuses a feature-contract mismatch or non-finite parameters.</summary>
    public static LinearModel Load(string path, string expectedKind, string[] expectedFeatures)
    {
        var model = JsonSerializer.Deserialize<LinearModel>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException("empty model");
        if (model.Kind != expectedKind ||
            model.FeatureVersion != FeatureContext.Version ||
            !model.FeatureNames.SequenceEqual(expectedFeatures) ||
            model.Weights.Length != expectedFeatures.Length ||
            model.Mean.Length != expectedFeatures.Length ||
            model.Std.Length != expectedFeatures.Length ||
            !double.IsFinite(model.Bias) ||
            model.Weights.Concat(model.Mean).Concat(model.Std).Any(v => !double.IsFinite(v)) ||
            model.Std.Any(v => v <= 0))
        {
            throw new InvalidDataException($"{path}: model does not match {expectedKind}/{FeatureContext.Version}");
        }

        return model;
    }
}

/// <summary>Small deterministic logistic-regression trainer (full-batch gradient descent, L2).</summary>
public static class LogisticTrainer
{
    /// <param name="rows">Raw feature rows.</param>
    /// <param name="labels">1 or 0.</param>
    /// <param name="fitBias">False for pairwise (difference) training, where a bias has no meaning.</param>
    public static (double[] Mean, double[] Std, double[] Weights, double Bias) Fit(
        IReadOnlyList<double[]> rows, IReadOnlyList<int> labels, double l2, bool fitBias, int iterations = 400, double rate = 0.5)
    {
        var d = rows[0].Length;
        var mean = new double[d];
        var std = new double[d];
        if (fitBias)
        {
            for (var j = 0; j < d; j++)
            {
                mean[j] = rows.Average(r => r[j]);
                var m = mean[j];
                std[j] = Math.Sqrt(rows.Average(r => (r[j] - m) * (r[j] - m)));
                std[j] = std[j] < 1e-9 ? 1 : std[j];
            }
        }
        else
        {
            // Pairwise differences: scale only, never centre (the sign of a difference is the signal).
            for (var j = 0; j < d; j++)
            {
                std[j] = Math.Sqrt(rows.Average(r => r[j] * r[j]));
                std[j] = std[j] < 1e-9 ? 1 : std[j];
            }
        }

        var z = rows.Select(r => r.Select((v, j) => (v - mean[j]) / std[j]).ToArray()).ToArray();
        var positives = labels.Count(l => l == 1);
        var wPos = positives == 0 ? 1 : labels.Count / (2.0 * positives);
        var wNeg = positives == labels.Count ? 1 : labels.Count / (2.0 * (labels.Count - positives));
        var w = new double[d];
        var b = 0.0;
        for (var it = 0; it < iterations; it++)
        {
            var g = new double[d];
            var gb = 0.0;
            for (var i = 0; i < z.Length; i++)
            {
                var s = b;
                for (var j = 0; j < d; j++)
                {
                    s += w[j] * z[i][j];
                }

                var p = 1 / (1 + Math.Exp(-s));
                var weight = labels[i] == 1 ? wPos : wNeg;
                var e = weight * (p - labels[i]);
                for (var j = 0; j < d; j++)
                {
                    g[j] += e * z[i][j];
                }

                gb += e;
            }

            for (var j = 0; j < d; j++)
            {
                w[j] -= rate * ((g[j] / z.Length) + (l2 * w[j]));
            }

            if (fitBias)
            {
                b -= rate * gb / z.Length;
            }
        }

        return (mean, std, w, b);
    }
}
