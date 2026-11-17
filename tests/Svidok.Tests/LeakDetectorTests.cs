using Svidok.Core.Metrics;
using Xunit;

namespace Svidok.Tests;

public sealed class LeakDetectorTests
{
    [Fact]
    public void Denial_Is_Not_Leak_Or_Claim()
    {
        const string say = "Про срібний кубок нічого не чув.";

        Assert.False(LeakDetector.IsLeak(say, null));
        Assert.False(LeakDetector.ClaimsEvent(say, null));
    }

    [Fact]
    public void Question_Echo_Is_Not_Claim()
    {
        const string say = "Срібний кубок? У мене є свій, і я за ним пильную.";

        Assert.False(LeakDetector.IsLeak(say, null));
        Assert.False(LeakDetector.ClaimsEvent(say, null));
    }

    [Fact]
    public void Reason_Without_Knowledge_Is_Not_Claim()
    {
        const string reason = "Немає жодних відомостей про подію з кубком, тому лише відповідаю гравцеві.";

        Assert.False(LeakDetector.ClaimsEvent("Іди собі, я сплю.", reason));
    }

    [Fact]
    public void Culprit_Name_Is_Leak()
    {
        const string say = "Кажуть, Захар крутився біля складу вночі.";

        Assert.True(LeakDetector.IsLeak(say, null));
        Assert.True(LeakDetector.ClaimsEvent(say, null));
    }

    [Fact]
    public void Rumor_About_Item_Is_Claim_But_Not_Leak()
    {
        const string say = "Чула я щось про кубок від сусідки.";

        Assert.False(LeakDetector.IsLeak(say, null));
        Assert.True(LeakDetector.ClaimsEvent(say, null));
    }
}
