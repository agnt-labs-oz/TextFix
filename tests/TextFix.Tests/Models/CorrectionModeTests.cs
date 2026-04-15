// tests/TextFix.Tests/Models/CorrectionModeTests.cs
using TextFix.Models;

namespace TextFix.Tests.Models;

public class CorrectionModeTests
{
    [Fact]
    public void Defaults_Contains6Modes()
    {
        var modes = CorrectionMode.Defaults;
        Assert.Equal(6, modes.Count);
    }

    [Fact]
    public void Defaults_FirstModeIsFixErrors()
    {
        var first = CorrectionMode.Defaults[0];
        Assert.Equal("Fix errors", first.Name);
        Assert.Contains("Fix all typos", first.SystemPrompt);
    }

    [Fact]
    public void Defaults_AllModesHaveNonEmptyPrompts()
    {
        foreach (var mode in CorrectionMode.Defaults)
        {
            Assert.False(string.IsNullOrWhiteSpace(mode.Name), "Mode name is empty");
            Assert.False(string.IsNullOrWhiteSpace(mode.SystemPrompt), $"Mode '{mode.Name}' has empty prompt");
        }
    }

    [Fact]
    public void Defaults_ContainsExpectedModeNames()
    {
        var names = CorrectionMode.Defaults.Select(m => m.Name).ToList();
        Assert.Contains("Fix errors", names);
        Assert.Contains("Professional", names);
        Assert.Contains("Concise", names);
        Assert.Contains("Friendly", names);
        Assert.Contains("Expand", names);
        Assert.Contains("Prompt enhancer", names);
    }
}
