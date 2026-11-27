// SPDX-FileCopyrightText: 2026 Broiler Platform contributors
// SPDX-License-Identifier: Apache-2.0

using Broiler.Mail.Application.Preview;

namespace Broiler.Mail.Tests;

/// <summary>UX-03.5d: the name Save as .eml… proposes is portable and never names a device or another folder.</summary>
public sealed class EmlFileNameTests
{
    private static readonly DateTimeOffset Date = new(2026, 3, 9, 23, 30, 0, TimeSpan.FromHours(2));

    [Theory]
    [InlineData("Quarterly report", "Quarterly report 2026-03-09.eml")]
    [InlineData("Re: Plan <draft> \"v2\" a/b\\c|d?e*", "Re_ Plan _draft_ _v2_ a_b_c_d_e_ 2026-03-09.eml")]
    [InlineData("  Many \t spaces\r\n  folded  ", "Many spaces folded 2026-03-09.eml")]
    [InlineData("...hidden.", "hidden 2026-03-09.eml")]
    [InlineData("Bell\u0007 and ‮right-to-left", "Bell_ and _right-to-left 2026-03-09.eml")]
    [InlineData("Grüße aus Köln 😀", "Grüße aus Köln 😀 2026-03-09.eml")]
    [InlineData("CON", "_CON 2026-03-09.eml")]
    [InlineData("nul.txt", "_nul.txt 2026-03-09.eml")]
    [InlineData("Com1 ", "_Com1 2026-03-09.eml")]
    [InlineData("Console", "Console 2026-03-09.eml")]
    [InlineData("", "message 2026-03-09.eml")]
    [InlineData(" . . ", "message 2026-03-09.eml")]
    [InlineData(null, "message 2026-03-09.eml")]
    public void SuggestIsPortableAndSafe(string? subject, string expected)
    {
        string name = EmlFileName.Suggest(subject, Date);
        Assert.Equal(expected, name);
        Assert.DoesNotContain(name, c => "<>:\"/\\|?*".Contains(c) || char.IsControl(c));
        Assert.Equal(name, Path.GetFileName(name));
    }

    [Fact]
    public void SuggestCutsLongSubjectsWithoutSplittingAPairAndWorksWithoutADate()
    {
        string name = EmlFileName.Suggest(new string('a', 200), Date);
        Assert.Equal(new string('a', EmlFileName.MaximumSubjectLength) + " 2026-03-09.eml", name);

        // An emoji straddling the cut is dropped whole, never halved.
        string straddling = EmlFileName.Suggest(new string('b', EmlFileName.MaximumSubjectLength - 1) + "😀tail", null);
        Assert.Equal(new string('b', EmlFileName.MaximumSubjectLength - 1) + ".eml", straddling);
        Assert.DoesNotContain(straddling, char.IsSurrogate);

        // A cut that ends in dots trims them again.
        Assert.Equal("word.eml", EmlFileName.Suggest("word" + new string('.', 76) + "rest", null));
        Assert.Equal("_CON.eml", EmlFileName.Suggest("CON", null));
        Assert.Equal("message.eml", EmlFileName.Suggest(null, null));

        // An unpaired surrogate (theory data cannot carry one) is not printable.
        Assert.Equal("lone _ surrogate.eml", EmlFileName.Suggest("lone \ud800 surrogate", null));
        Assert.Equal("_ end.eml", EmlFileName.Suggest("\udc00 end", null));
    }
}
