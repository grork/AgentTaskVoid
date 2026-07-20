using Codevoid.AgentTaskVoid.Run;

namespace Codevoid.AgentTaskVoid.LogicTests.Run;

/// <summary>AC1: table-driven coverage of the fixed 6-step pipeline -- ANSI-heavy lines, `\r` progress bars, control chars, blanks, over-long lines.</summary>
[TestClass]
public sealed class LineHygieneTests
{
    private const int DefaultMaxLength = 200;

    [TestMethod]
    [DataRow("plain line", "plain line", DisplayName = "plain text is untouched")]
    [DataRow("  leading and trailing  ", "leading and trailing", DisplayName = "step 4: trims whitespace")]
    [DataRow("[31mred text[0m", "red text", DisplayName = "step 1: strips SGR color codes")]
    [DataRow("[2K[1Gclear-line-then-write", "clear-line-then-write", DisplayName = "step 1: strips cursor/erase CSI sequences")]
    [DataRow("noescapeshere-but-bare-ESC-dropped", "noescapeshere-but-bare-ESC-dropped", DisplayName = "step 1 leaves a bare ESC not followed by a valid escape byte alone; step 3's control-char scrub drops just the ESC, not the next character")]
    [DataRow("10%\r50%\r100%", "100%", DisplayName = "step 2: \\r progress bar collapses to its final value")]
    [DataRow("hello\r", "hello", DisplayName = "step 2: a lone trailing \\r (CRLF remnant) is stripped, not treated as an overwrite")]
    [DataRow("a\tb\tc", "a b c", DisplayName = "step 3: tabs become a single space")]
    [DataRow("bell\a-backspace\b-null\0-formfeed\f-end", "bell-backspace-null-formfeed-end", DisplayName = "step 3: bell/backspace/null/form-feed are dropped outright")]
    [DataRow("   ", null, DisplayName = "step 5: an all-whitespace line is dropped (null)")]
    [DataRow("", null, DisplayName = "step 5: an empty line is dropped (null)")]
    public void Clean_TableDriven(string input, string? expected)
    {
        string? actual = LineHygiene.Clean(input, DefaultMaxLength);
        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Clean_OverLongLine_TruncatedWithEllipsis()
    {
        string input = new string('x', 50);
        string? result = LineHygiene.Clean(input, maxLength: 10);

        Assert.IsNotNull(result);
        Assert.AreEqual(10, result!.Length);
        Assert.IsTrue(result.EndsWith(LineHygiene.Ellipsis, StringComparison.Ordinal));
        Assert.AreEqual(new string('x', 9) + LineHygiene.Ellipsis, result);
    }

    [TestMethod]
    public void Clean_LineExactlyAtMaxLength_NotTruncated()
    {
        string input = new string('y', 10);
        string? result = LineHygiene.Clean(input, maxLength: 10);
        Assert.AreEqual(input, result);
    }

    [TestMethod]
    public void Clean_LineShorterThanMaxLength_Unchanged()
    {
        string? result = LineHygiene.Clean("short", maxLength: 200);
        Assert.AreEqual("short", result);
    }

    [TestMethod]
    public void Clean_NonPositiveMaxLength_TreatedAsUnbounded()
    {
        string input = new string('z', 500);
        Assert.AreEqual(input, LineHygiene.Clean(input, maxLength: 0));
        Assert.AreEqual(input, LineHygiene.Clean(input, maxLength: -5));
    }

    [TestMethod]
    public void Clean_CombinedPipeline_AllStepsApplyInOrder()
    {
        // ANSI color + a progress-bar \r-overwrite + a tab + trailing whitespace,
        // all in one line -- proves the fixed order (strip ANSI -> collapse \r ->
        // scrub controls -> trim) composes correctly rather than just each step
        // in isolation.
        string input = "\x1B[32mProgress:\t0%\r\x1B[32mProgress:\t100%\x1B[0m   ";
        string? result = LineHygiene.Clean(input, DefaultMaxLength);
        Assert.AreEqual("Progress: 100%", result);
    }

}
