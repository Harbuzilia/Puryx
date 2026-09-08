using SmartCleaner.Core.AiAssistant;
using Xunit;

namespace SmartCleaner.Core.Tests;

public class NaturalLanguageQueryEngineTests
{
    private readonly NaturalLanguageQueryEngine _engine = new();

    [Theory]
    [InlineData("найди видео больше 2 гб", AiActionType.OpenPage, "LargeFiles")]
    [InlineData("освободи 20 гб под игру", AiActionType.TriggerScan, "All")]
    [InlineData("почисти мусор от rust и node", AiActionType.OpenPage, "DevClean")]
    [InlineData("найди дубликаты", AiActionType.OpenPage, "Duplicates")]
    [InlineData("сожми игры в steam", AiActionType.OpenPage, "Compact")]
    [InlineData("удали старую программу и почисти хвосты", AiActionType.OpenPage, "Uninstaller")]
    public async Task ProcessUserQueryAsync_KnownPatterns_ReturnsCorrectAction(string query, AiActionType expectedAction, string expectedPayload)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);

        Assert.NotNull(reply);
        Assert.False(reply.IsUser);
        Assert.Equal(expectedAction, reply.ActionType);
        Assert.Equal(expectedPayload, reply.ActionPayload);
        Assert.NotEmpty(reply.Text);
    }

    [Theory]
    [InlineData("фильм на 5 гб")]
    [InlineData("video files")]
    [InlineData("movie collection")]
    public async Task ProcessUserQueryAsync_VideoKeywords_ReturnsLargeFiles(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.OpenPage, reply.ActionType);
        Assert.Equal("LargeFiles", reply.ActionPayload);
    }

    [Theory]
    [InlineData("программы")]
    [InlineData("деинсталляция")]
    [InlineData("хвосты")]
    [InlineData("uninstall chrome")]
    public async Task ProcessUserQueryAsync_UninstallKeywords_ReturnsUninstaller(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.OpenPage, reply.ActionType);
        Assert.Equal("Uninstaller", reply.ActionPayload);
    }

    [Theory]
    [InlineData("node_modules cache")]
    [InlineData("gradle build")]
    [InlineData("maven clean")]
    [InlineData("wsl space")]
    [InlineData("разработка")]
    public async Task ProcessUserQueryAsync_DevKeywords_ReturnsDevClean(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.OpenPage, reply.ActionType);
        Assert.Equal("DevClean", reply.ActionPayload);
    }

    [Theory]
    [InlineData("драйверы")]
    [InlineData("winsxs очистка")]
    [InlineData("driverstore")]
    [InlineData("ядро системы")]
    public async Task ProcessUserQueryAsync_WinSxSKeywords_ReturnsDeepClean(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.OpenPage, reply.ActionType);
        Assert.Equal("DeepClean", reply.ActionPayload);
    }

    [Theory]
    [InlineData("дубликаты")]
    [InlineData("копии файлов")]
    [InlineData("повторяющиеся")]
    [InlineData("duplicate files")]
    public async Task ProcessUserQueryAsync_DuplicateKeywords_ReturnsDuplicates(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.OpenPage, reply.ActionType);
        Assert.Equal("Duplicates", reply.ActionPayload);
    }

    [Theory]
    [InlineData("сжатие")]
    [InlineData("сожми папку")]
    [InlineData("compact os")]
    [InlineData("lzx")]
    public async Task ProcessUserQueryAsync_CompressKeywords_ReturnsCompact(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.OpenPage, reply.ActionType);
        Assert.Equal("Compact", reply.ActionPayload);
    }

    [Theory]
    [InlineData("освободи место")]
    [InlineData("очистка")]
    [InlineData("free up space")]
    [InlineData("clean")]
    [InlineData("нужно место")]
    public async Task ProcessUserQueryAsync_GenericClean_ReturnsTriggerScanAll(string query)
    {
        var reply = await _engine.ProcessUserQueryAsync(query);
        Assert.Equal(AiActionType.TriggerScan, reply.ActionType);
        Assert.Equal("All", reply.ActionPayload);
    }

    [Fact]
    public async Task ProcessUserQueryAsync_UnknownQuery_ReturnsGenericResponse()
    {
        var reply = await _engine.ProcessUserQueryAsync("как дела?");
        Assert.Equal(AiActionType.TriggerScan, reply.ActionType);
        Assert.Equal("General", reply.ActionPayload);
        Assert.NotEmpty(reply.Text);
    }

    [Fact]
    public async Task ProcessUserQueryAsync_EmptyQuery_ReturnsGenericResponse()
    {
        var reply = await _engine.ProcessUserQueryAsync("");
        Assert.NotEmpty(reply.Text);
    }

    [Fact]
    public async Task ProcessUserQueryAsync_WhitespaceQuery_ReturnsGenericResponse()
    {
        var reply = await _engine.ProcessUserQueryAsync("   ");
        Assert.NotEmpty(reply.Text);
    }

    [Fact]
    public async Task ProcessUserQueryAsync_Message_HasCorrectProperties()
    {
        var reply = await _engine.ProcessUserQueryAsync("найди дубликаты");

        Assert.False(reply.IsUser);
        Assert.NotEmpty(reply.Id);
        Assert.NotEqual(default, reply.Timestamp);
        Assert.NotEmpty(reply.ActionButtonText);
    }
}