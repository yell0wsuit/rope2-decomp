using System.IO;

using Ctr2.Engine;

using Xunit;

namespace Ctr2.Tests
{
    /// <summary>Checks the <c>ZString</c> accessor semantics the level readers share.</summary>
    public sealed class NativeTextTests
    {
        /// <summary>Integer reads keep the leading digits of a well-formed decimal.</summary>
        /// <param name="text">Authored text.</param>
        /// <param name="expected">Native <c>intValue</c> result.</param>
        [Theory]
        [InlineData(null, 0)]
        [InlineData("42", 42)]
        [InlineData(" -42", -42)]
        [InlineData("280.27", 280)]
        [InlineData("-0.5", 0)]
        [InlineData(".5", 0)]
        [InlineData("1e2", 1)]
        public void IntValueTruncatesDecimals(string? text, int expected)
        {
            Assert.Equal(expected, NativeText.IntValue(text));
        }

        /// <summary>Flags read with <c>std::boolalpha</c>: only lowercase <c>true</c> after optional whitespace is true.</summary>
        /// <param name="text">Authored text.</param>
        /// <param name="expected">Native <c>boolValue</c> result.</param>
        [Theory]
        [InlineData(null, false)]
        [InlineData("true", true)]
        [InlineData("  true", true)]
        [InlineData("True", false)]
        [InlineData("1", false)]
        [InlineData("0", false)]
        [InlineData("false", false)]
        public void BoolValueAcceptsOnlyLowercaseTrue(string? text, bool expected)
        {
            Assert.Equal(expected, NativeText.BoolValue(text));
        }

        /// <summary>Absent numbers read zero, but malformed or unbounded text fails instead of reading zero.</summary>
        /// <param name="text">Malformed text.</param>
        [Theory]
        [InlineData("NaN")]
        [InlineData("abc")]
        [InlineData("1e7")]
        public void MalformedNumbersFail(string text)
        {
            Assert.Equal(0, NativeText.FloatValue(null));
            _ = Assert.Throws<InvalidDataException>(() => NativeText.FloatValue(text));
            _ = Assert.Throws<InvalidDataException>(() => NativeText.IntValue(text));
        }
    }
}
