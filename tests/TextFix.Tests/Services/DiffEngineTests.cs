using TextFix.Services;

namespace TextFix.Tests.Services;

public class DiffEngineTests
{
    [Fact]
    public void DiffSegment_HoldsKindAndText()
    {
        var seg = new DiffSegment(DiffKind.Equal, "hello");
        Assert.Equal(DiffKind.Equal, seg.Kind);
        Assert.Equal("hello", seg.Text);
    }
}
