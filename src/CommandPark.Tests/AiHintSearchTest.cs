using CommandPark.Ai;
using JetBrains.Annotations;
using Microsoft.Extensions.AI;

namespace CommandPark.Tests;

[TestSubject(typeof(AiHintSearch))]
public class AiHintSearchTest
{
    private static AiConfig CreateConfig() => new()
    {
        Endpoint = "https://example.com/v1",
        ApiKey = "test-key",
        Model = "test-model",
    };

    [Theory]
    [InlineData("[\"a1b2c3d4\"]", 1)]
    [InlineData("说明文字\n[\"a1b2c3d4\", \"DEADBEEF\"]\n", 2)]
    [InlineData("[]", 0)]
    [InlineData("no json here", 0)]
    [InlineData("```json\n[\"a1b2c3d4\"]\n```", 0)]
    [InlineData("{\"hashes\":[\"a1b2c3d4\"]}", 0)]
    [InlineData("[\"a1b2c3d4\", 42]", 0)]
    public void ParseLastLineHashArray_OnlyAcceptsBareStringArrayOnLastLine(string text, int expected)
    {
        Assert.Equal(expected, AiHintSearch.ParseLastLineHashArray(text).Count);
    }

    [Fact]
    public void ParseLastLineHashArray_IgnoresTrailingBlankLines()
    {
        var hashes = AiHintSearch.ParseLastLineHashArray("说明\n[\"a1b2c3d4\"]\n\n");

        Assert.Single(hashes);
        Assert.Equal("a1b2c3d4", hashes[0]);
    }

    [Fact]
    public void StripTrailingHashLine_RemovesOnlyTheJsonArrayLine()
    {
        Assert.Equal("我找到了这些结果。", AiHintSearch.StripTrailingHashLine("我找到了这些结果。\n[\"a1b2c3d4\"]"));
        Assert.Equal("只有说明", AiHintSearch.StripTrailingHashLine("只有说明"));
    }

    [Fact]
    public async Task RunAsync_ResolvesTheHintBehindTheReturnedHash()
    {
        var hint = new Hint("run:notepad", "记事本", "打开记事本");
        var provider = new FakeHintProvider([hint]);
        var client = new FakeChatClient($"我找到了这个命令。\n[\"{HintHash.Short(hint)}\"]");

        var result = await AiHintSearch.RunAsync(client, provider, "打开记事本", CreateConfig());

        Assert.Single(result.Hints);
        Assert.Same(hint, result.Hints[0]);
        Assert.Equal("我找到了这个命令。", result.Answer);
    }

    [Fact]
    public async Task RunAsync_DropsUnknownHashes()
    {
        var hint = new Hint("run:notepad", "记事本", "打开记事本");
        var provider = new FakeHintProvider([hint]);
        var client = new FakeChatClient("[\"deadbeef\"]");

        var result = await AiHintSearch.RunAsync(client, provider, "打开记事本", CreateConfig());

        Assert.Empty(result.Hints);
    }

    [Fact]
    public async Task RunAsync_CapsHintsAtConfiguredMaxResults()
    {
        var hints = Enumerable.Range(0, 10)
            .Select(i => new Hint($"id:{i}", $"程序 {i}", $"描述 {i}"))
            .ToArray();
        var provider = new FakeHintProvider(hints);
        var hashes = string.Join(",", hints.Select(h => $"\"{HintHash.Short(h)}\""));
        var client = new FakeChatClient($"[{hashes}]");
        var config = new AiConfig
        {
            Endpoint = "https://example.com/v1",
            ApiKey = "test-key",
            Model = "test-model",
            MaxResults = 3,
        };

        var result = await AiHintSearch.RunAsync(client, provider, "程序", config);

        Assert.Equal(3, result.Hints.Count);
    }

    private sealed class FakeHintProvider(IReadOnlyList<IHint> hints) : IHintProvider
    {
        public IReadOnlyList<IHintSource> Sources => [];

        public Task SearchAsync(
            string query,
            Action<IHint[]> onSearched,
            TimeSpan batchSendTime,
            CancellationToken cancellationToken = default)
        {
            onSearched([.. hints]);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeChatClient(string response) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
