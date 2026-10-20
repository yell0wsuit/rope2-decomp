using System;
using System.Globalization;
using System.IO;

namespace Ctr2.Engine
{
    /// <summary>Reads authored text like the native <c>ZString</c> accessors, which stream it through an
    /// <c>istringstream</c>: absent text reads as zero or false, and a well-formed decimal is read up to the first
    /// character the target type cannot hold.</summary>
    /// <remarks>Blank text reads zero, as the stream finds no number in it; other text that is not a finite decimal within one
    /// million fails instead of silently reading as zero.</remarks>
    internal static class NativeText
    {
        /// <summary>Native <c>ZString::floatValue</c>.</summary>
        /// <param name="text">Authored text, or null when absent.</param>
        /// <returns>Zero when absent, otherwise the decimal value.</returns>
        /// <exception cref="InvalidDataException">The text is not a bounded finite decimal.</exception>
        internal static float FloatValue(string? text)
        {
            return string.IsNullOrWhiteSpace(text) ? 0 : Decimal(text);
        }

        /// <summary>Native <c>ZString::intValue</c>: the integer digits that lead a decimal, so <c>280.27</c> reads 280 and
        /// <c>1e2</c> reads 1.</summary>
        /// <param name="text">Authored text, or null when absent.</param>
        /// <returns>Zero when absent or without leading digits, otherwise the truncated integer.</returns>
        /// <exception cref="InvalidDataException">The text is not a bounded finite decimal.</exception>
        internal static int IntValue(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return 0;
            }
            _ = Decimal(text);
            ReadOnlySpan<char> span = text.AsSpan().TrimStart();
            int end = span.Length > 0 && span[0] is '+' or '-' ? 1 : 0;
            while (end < span.Length && char.IsAsciiDigit(span[end]))
            {
                end++;
            }
            return int.TryParse(span[..end], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int value) ? value : 0;
        }

        /// <summary>Native <c>ZString::boolValue</c>, which reads with <c>std::boolalpha</c>: only the lowercase word
        /// <c>true</c> after optional whitespace is true, so <c>1</c> and <c>True</c> read false.</summary>
        /// <param name="text">Authored text, or null when absent.</param>
        /// <returns>The flag.</returns>
        internal static bool BoolValue(string? text)
        {
            return text != null && text.AsSpan().TrimStart().StartsWith("true", StringComparison.Ordinal);
        }

        private static float Decimal(string text)
        {
            return float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) && float.IsFinite(value) && MathF.Abs(value) <= 1000000
                ? value : throw new InvalidDataException("Invalid level number: " + text);
        }
    }
}
