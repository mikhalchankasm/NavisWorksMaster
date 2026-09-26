using System;
using System.Collections.Generic;
using System.Globalization;

namespace NavisHelper.Agent.Contracts
{
    public struct ModelColorSchemeRgb
    {
        public ModelColorSchemeRgb(double r, double g, double b)
        {
            R = r;
            G = g;
            B = b;
        }

        public double R { get; }
        public double G { get; }
        public double B { get; }
    }

    public sealed class ModelColorSchemeColorSample
    {
        public ModelColorSchemeRgb Requested { get; set; }

        // Null when Navisworks could not return the color; ReadError says why.
        public ModelColorSchemeRgb? Permanent { get; set; }
        public ModelColorSchemeRgb? Active { get; set; }
        public string ReadError { get; set; }
    }

    public static class ModelColorSchemeColorVerifier
    {
        public const string PermanentMismatchWarning =
            "Navisworks did not retain the requested permanent color on every verification sample.";

        public const string ActiveMismatchWarning =
            "Permanent colors were stored, but another Navisworks display layer still masks some active colors.";

        // Navisworks does not hand a written channel back bit for bit: 32/255.0 from
        // Color.FromByteRGB returns rounded through single precision, so an exact double
        // comparison fails for every channel other than 0 and 255. Rule colors are 8-bit
        // hex values, so colors are compared at that precision.
        public static bool ChannelsMatch(ModelColorSchemeRgb left, ModelColorSchemeRgb right)
        {
            var leftR = ToByteChannel(left.R);
            var leftG = ToByteChannel(left.G);
            var leftB = ToByteChannel(left.B);
            return leftR >= 0 && leftG >= 0 && leftB >= 0 &&
                   leftR == ToByteChannel(right.R) &&
                   leftG == ToByteChannel(right.G) &&
                   leftB == ToByteChannel(right.B);
        }

        // Returns -1 for a channel that is not a finite number.
        public static int ToByteChannel(double channel)
        {
            if (double.IsNaN(channel) || double.IsInfinity(channel))
                return -1;
            var clamped = Math.Max(0.0, Math.Min(1.0, channel));
            return (int)Math.Round(clamped * 255.0, MidpointRounding.AwayFromZero);
        }

        public static string ToHex(ModelColorSchemeRgb color)
        {
            return "#" + HexChannel(color.R) + HexChannel(color.G) + HexChannel(color.B);
        }

        public static void Tally(
            IEnumerable<ModelColorSchemeColorSample> samples,
            ModelColorSchemeResponse response)
        {
            string firstPermanentMismatch = null;
            string firstActiveMismatch = null;
            foreach (var sample in samples ?? new List<ModelColorSchemeColorSample>())
            {
                if (sample == null)
                    continue;
                response.ColorVerificationSampleCount++;
                if (Matches(sample.Requested, sample.Permanent))
                    response.PermanentColorMatchCount++;
                else if (firstPermanentMismatch == null)
                    firstPermanentMismatch = DescribeMismatch(sample, sample.Permanent);
                if (Matches(sample.Requested, sample.Active))
                    response.ActiveColorMatchCount++;
                else if (firstActiveMismatch == null)
                    firstActiveMismatch = DescribeMismatch(sample, sample.Active);
            }

            var count = response.ColorVerificationSampleCount;
            if (count > 0 && response.PermanentColorMatchCount < count)
            {
                response.Warnings.Add(
                    PermanentMismatchWarning + " " +
                    (count - response.PermanentColorMatchCount) + " of " + count +
                    " samples differ; first: " + firstPermanentMismatch + ".");
            }
            else if (count > 0 && response.ActiveColorMatchCount < count)
            {
                response.Warnings.Add(
                    ActiveMismatchWarning + " " +
                    (count - response.ActiveColorMatchCount) + " of " + count +
                    " samples differ; first: " + firstActiveMismatch + ".");
            }
        }

        private static bool Matches(ModelColorSchemeRgb requested, ModelColorSchemeRgb? actual)
        {
            return actual.HasValue && ChannelsMatch(requested, actual.Value);
        }

        private static string DescribeMismatch(
            ModelColorSchemeColorSample sample,
            ModelColorSchemeRgb? actual)
        {
            var read = actual.HasValue
                ? ToHex(actual.Value)
                : "unreadable" + (string.IsNullOrWhiteSpace(sample.ReadError)
                    ? string.Empty
                    : " (" + sample.ReadError.Trim() + ")");
            return "requested " + ToHex(sample.Requested) + ", read " + read;
        }

        private static string HexChannel(double channel)
        {
            var value = ToByteChannel(channel);
            return value < 0 ? "??" : value.ToString("X2", CultureInfo.InvariantCulture);
        }
    }
}
