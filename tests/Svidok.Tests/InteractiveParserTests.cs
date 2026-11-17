using Svidok.Core.Decisions;
using Svidok.Llm;
using Xunit;

namespace Svidok.Tests;

public sealed class InteractiveParserTests
{
    [Fact]
    public void Memory_Attitude_And_Gossip_Are_Read()
    {
        var catalog = ActionCatalog.LoadDefault();
        var content = "{\"action\":\"say\",\"say\":\"Іди геть.\",\"memory\":\"Нахамив мені біля вежі\"," +
                      "\"attitude\":-2,\"gossip\":true}";

        var ok = ResponseParser.TryParse(content, catalog, out var decision, out var error);

        Assert.True(ok, error);
        Assert.Equal("Нахамив мені біля вежі", decision.MemoryNote);
        Assert.Equal(-2, decision.AttitudeDelta);
        Assert.True(decision.Gossip);
    }

    [Fact]
    public void Null_Memory_And_Out_Of_Range_Attitude_Are_Normalized()
    {
        var catalog = ActionCatalog.LoadDefault();
        var content = "{\"action\":\"say\",\"say\":\"Добрий день.\",\"memory\":null,\"attitude\":7,\"gossip\":\"false\"}";

        var ok = ResponseParser.TryParse(content, catalog, out var decision, out _);

        Assert.True(ok);
        Assert.Null(decision.MemoryNote);
        Assert.Equal(2, decision.AttitudeDelta);
        Assert.False(decision.Gossip);
    }

    [Fact]
    public void Batch_Answer_Without_New_Fields_Still_Parses()
    {
        var catalog = ActionCatalog.LoadDefault();

        var ok = ResponseParser.TryParse("{\"action\":\"ignore\",\"say\":\"\"}", catalog, out var decision, out _);

        Assert.True(ok);
        Assert.Null(decision.MemoryNote);
        Assert.Null(decision.AttitudeDelta);
        Assert.False(decision.Gossip);
    }
}
