using SmartCleaner.Core.Startup;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class StartupEngineTests
{
    [Theory]
    [InlineData("\"C:\\Program Files\\My App\\app.exe\" --minimized",
                "C:\\Program Files\\My App\\app.exe", "--minimized")]
    [InlineData("\"C:\\App\\app.exe\"",
                "C:\\App\\app.exe", "")]
    [InlineData("C:\\App\\app.exe /background",
                "C:\\App\\app.exe", "/background")]
    [InlineData("app.exe", "app.exe", "")]
    [InlineData("  \"C:\\Tools\\t.exe\"  run --now  ",
                "C:\\Tools\\t.exe", "run --now")]
    public void ParseCommand_SplitsPathAndArguments(
        string command, string expectedPath, string expectedArgs)
    {
        var (filePath, arguments) = StartupEngine.ParseCommand(command);

        Assert.Equal(expectedPath, filePath);
        Assert.Equal(expectedArgs, arguments);
    }

    [Fact]
    public void ParseCommand_QuotedPathWithNestedQuotes_UsesFirstClosingQuote()
    {
        var (filePath, arguments) = StartupEngine.ParseCommand("\"C:\\A\\b.exe\" \"arg with spaces\"");

        Assert.Equal("C:\\A\\b.exe", filePath);
        Assert.Equal("\"arg with spaces\"", arguments);
    }

    [Fact]
    public void ParseCommand_UnclosedQuote_FallsBackToWholeString()
    {
        var (filePath, arguments) = StartupEngine.ParseCommand("\"C:\\App\\unclosed");

        Assert.Equal("\"C:\\App\\unclosed", filePath);
        Assert.Equal("", arguments);
    }

    [Fact]
    public void ParseCommand_QuotedPathWithArgsAfterSpaces_TrimsArguments()
    {
        var (filePath, arguments) = StartupEngine.ParseCommand("\"C:\\A\\b.exe\"   ");

        Assert.Equal("C:\\A\\b.exe", filePath);
        Assert.Equal("", arguments);
    }
}
