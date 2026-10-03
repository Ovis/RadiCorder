using System.Text;
using System.Text.RegularExpressions;

namespace RadiCorder.Logics.Domain.DuplicateDetection;

/// <summary>
/// タイトルと音声指紋の類似度を計算する。
/// </summary>
public static class DuplicateSimilarity
{
    public const int AudioSampleRate = 8000;
    private const int MinOverlapSeconds = 120;
    private const int MaxShiftSeconds = 120;
    private static readonly Regex NonAlphaNumericRegex = new(@"[^\p{L}\p{Nd}]+", RegexOptions.Compiled);
    private static readonly Regex BracketRegex = new(@"[\(\[（【].*?[\)\]）】]", RegexOptions.Compiled);
    private static readonly Regex MultiSpaceRegex = new(@"\s+", RegexOptions.Compiled);


    public static double[] BuildEnergyBins(byte[] pcmBytes)
    {
        if (pcmBytes.Length < 2)
        {
            return [];
        }

        var sampleCount = pcmBytes.Length / 2;
        if (sampleCount < AudioSampleRate)
        {
            return [];
        }

        var seconds = sampleCount / AudioSampleRate;
        var bins = new double[seconds];

        for (var sec = 0; sec < seconds; sec++)
        {
            long sum = 0;
            var offset = sec * AudioSampleRate * 2;
            for (var i = 0; i < AudioSampleRate; i++)
            {
                var index = offset + (i * 2);
                var value = BitConverter.ToInt16(pcmBytes, index);
                // short.MinValue (-32768) に対する Math.Abs(short) の OverflowException を回避する
                sum += Math.Abs((int)value);
            }

            bins[sec] = sum / (double)AudioSampleRate;
        }

        SmoothInPlace(bins);
        NormalizeInPlace(bins);
        return bins;
    }

    private static void SmoothInPlace(double[] values)
    {
        if (values.Length < 5)
        {
            return;
        }

        var source = values.ToArray();
        for (var i = 0; i < values.Length; i++)
        {
            var from = Math.Max(0, i - 2);
            var to = Math.Min(values.Length - 1, i + 2);
            var sum = 0d;
            for (var j = from; j <= to; j++)
            {
                sum += source[j];
            }

            values[i] = sum / (to - from + 1);
        }
    }

    private static void NormalizeInPlace(double[] values)
    {
        if (values.Length == 0)
        {
            return;
        }

        var mean = values.Average();
        var variance = values.Sum(v => Math.Pow(v - mean, 2d)) / values.Length;
        var std = Math.Sqrt(variance);
        if (std < 1e-9)
        {
            return;
        }

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = (values[i] - mean) / std;
        }
    }

    public static double CalculateBestCorrelationScore(double[] left, double[] right)
    {
        if (left.Length < MinOverlapSeconds || right.Length < MinOverlapSeconds)
        {
            return 0d;
        }

        var best = double.MinValue;
        for (var shift = -MaxShiftSeconds; shift <= MaxShiftSeconds; shift++)
        {
            var correlation = CalculatePearsonWithShift(left, right, shift, out var overlap);
            if (overlap < MinOverlapSeconds)
            {
                continue;
            }

            if (correlation > best)
            {
                best = correlation;
            }
        }

        if (double.IsNegativeInfinity(best) || best == double.MinValue)
        {
            return 0d;
        }

        return Math.Clamp((best + 1d) / 2d, 0d, 1d);
    }

    private static double CalculatePearsonWithShift(double[] left, double[] right, int shift, out int overlap)
    {
        var leftStart = Math.Max(0, shift);
        var rightStart = Math.Max(0, -shift);
        overlap = Math.Min(left.Length - leftStart, right.Length - rightStart);
        if (overlap <= 1)
        {
            return 0d;
        }

        var leftSpan = left.AsSpan(leftStart, overlap);
        var rightSpan = right.AsSpan(rightStart, overlap);

        var meanLeft = 0d;
        var meanRight = 0d;
        for (var i = 0; i < overlap; i++)
        {
            meanLeft += leftSpan[i];
            meanRight += rightSpan[i];
        }

        meanLeft /= overlap;
        meanRight /= overlap;

        var numerator = 0d;
        var denLeft = 0d;
        var denRight = 0d;
        for (var i = 0; i < overlap; i++)
        {
            var l = leftSpan[i] - meanLeft;
            var r = rightSpan[i] - meanRight;
            numerator += l * r;
            denLeft += l * l;
            denRight += r * r;
        }

        var denominator = Math.Sqrt(denLeft * denRight);
        if (denominator < 1e-9)
        {
            return 0d;
        }

        return numerator / denominator;
    }

    public static string NormalizeTitle(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var value = raw.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        value = BracketRegex.Replace(value, " ");
        value = MultiSpaceRegex.Replace(value, " ").Trim();
        value = NonAlphaNumericRegex.Replace(value, string.Empty);
        return value;
    }

    public static double CalculateTitleSimilarity(string left, string right)
    {
        if (string.IsNullOrEmpty(left) || string.IsNullOrEmpty(right))
        {
            return 0d;
        }

        if (left == right)
        {
            return 1d;
        }

        var distance = LevenshteinDistance(left, right);
        var maxLen = Math.Max(left.Length, right.Length);
        if (maxLen == 0)
        {
            return 1d;
        }

        return 1d - (distance / (double)maxLen);
    }

    private static int LevenshteinDistance(string source, string target)
    {
        var m = source.Length;
        var n = target.Length;
        var d = new int[m + 1, n + 1];

        for (var i = 0; i <= m; i++)
        {
            d[i, 0] = i;
        }

        for (var j = 0; j <= n; j++)
        {
            d[0, j] = j;
        }

        for (var i = 1; i <= m; i++)
        {
            for (var j = 1; j <= n; j++)
            {
                var cost = source[i - 1] == target[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[m, n];
    }
}
