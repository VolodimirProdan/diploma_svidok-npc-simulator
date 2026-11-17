using Svidok.Core.Config;
using Svidok.Core.Llm;
using Svidok.Llm;
using Xunit;

namespace Svidok.Tests;

public sealed class CacheTests
{
    [Fact]
    public async Task Second_Identical_Request_Comes_From_Cache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "svidok_cache_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mock = new MockLlmClient(0);
            var cached = new CachedLlmClient(mock, LlmMode.LiveWithCache, directory);
            var request = new LlmRequest("deepseek-flash", 0.3, 400, "system json", "ti khto?");

            var first = await cached.CompleteAsync(request, CancellationToken.None);
            var second = await cached.CompleteAsync(request, CancellationToken.None);

            Assert.False(first.FromCache);
            Assert.True(second.FromCache);
            Assert.Equal(1, mock.NetworkCalls);
            Assert.NotNull(second.CacheFile);
            Assert.True(File.Exists(second.CacheFile));
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Truncated_Answer_Is_Requested_Again_With_Bigger_Limit()
    {
        var directory = Path.Combine(Path.GetTempPath(), "svidok_cache_trunc_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mock = new MockLlmClient(0);
            var cached = new CachedLlmClient(mock, LlmMode.LiveWithCache, directory);
            var small = new LlmRequest("deepseek-flash", 0.3, 20, "system json", "ti khto?");
            var big = small with { MaxTokens = 1200 };

            await cached.CompleteAsync(small, CancellationToken.None);
            var again = await cached.CompleteAsync(big, CancellationToken.None);
            var third = await cached.CompleteAsync(big, CancellationToken.None);

            Assert.False(again.FromCache);
            Assert.True(third.FromCache);
            Assert.Equal(2, mock.NetworkCalls);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }

    [Fact]
    public async Task Replay_Only_Without_Cache_Gives_CacheMiss()
    {
        var directory = Path.Combine(Path.GetTempPath(), "svidok_cache_miss_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var mock = new MockLlmClient(0);
            var replay = new CachedLlmClient(mock, LlmMode.ReplayOnly, directory);
            var request = new LlmRequest("deepseek-flash", 0.3, 400, "system", "user");

            var result = await replay.CompleteAsync(request, CancellationToken.None);

            Assert.Contains("CacheMiss", result.Error);
            Assert.Equal(0, mock.NetworkCalls);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, true);
            }
            catch (IOException)
            {
            }
        }
    }
}
