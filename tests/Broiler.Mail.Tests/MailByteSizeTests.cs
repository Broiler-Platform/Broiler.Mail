using System.Globalization;
using Broiler.Mail.Application.Views;

namespace Broiler.Mail.Tests;

public sealed class MailByteSizeTests
{
    private const long MiB = 1024 * 1024;

    [Theory]
    [InlineData(0L, "0 bytes")]
    [InlineData(1L, "1 byte")]
    [InlineData(1023L, "1023 bytes")]
    [InlineData(1024L, "1 KiB")]
    [InlineData(1025L, "2 KiB")]
    [InlineData(500_000L, "489 KiB")]
    [InlineData(MiB - 1025, "1023 KiB")]
    [InlineData(MiB - 1, "1.0 MiB")]
    [InlineData(MiB, "1.0 MiB")]
    [InlineData(MiB + 1, "1.1 MiB")]
    [InlineData(8 * MiB - 1, "8.0 MiB")]
    [InlineData(8 * MiB, "8.0 MiB")]
    [InlineData(8 * MiB + 1, "8.1 MiB")]
    [InlineData(12 * MiB, "12.0 MiB")]
    public void FormatsInBinaryUnitsRoundingUp(long bytes, string expected) => Assert.Equal(expected, MailByteSize.Format(bytes));

    [Theory]
    [InlineData(0L, "0.0")]
    [InlineData(1L, "0.1")]
    [InlineData(MiB, "1.0")]
    [InlineData(8 * MiB, "8.0")]
    [InlineData(8 * MiB + 1, "8.1")]
    [InlineData(3 * MiB / 2, "1.5")]
    public void MebibytesHasOneDecimal(long bytes, string expected) => Assert.Equal(expected, MailByteSize.Mebibytes(bytes));

    [Fact]
    public void FormatIsInvariantAndRefusesNegativeSizes()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
            Assert.Equal("8.0 MiB", MailByteSize.Format(8 * MiB));
            Assert.Equal("1023 bytes", MailByteSize.Format(1023));
        }
        finally { CultureInfo.CurrentCulture = culture; }
        Assert.Throws<ArgumentOutOfRangeException>(() => MailByteSize.Format(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => MailByteSize.Mebibytes(-1));
    }
}
