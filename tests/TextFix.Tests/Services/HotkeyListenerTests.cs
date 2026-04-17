using TextFix.Services;

namespace TextFix.Tests.Services;

public class HotkeyListenerTests
{
    private const uint MOD_ALT = 0x0001;
    private const uint MOD_CONTROL = 0x0002;
    private const uint MOD_SHIFT = 0x0004;
    private const uint MOD_WIN = 0x0008;

    [Theory]
    [InlineData("Ctrl+Shift+Z", MOD_CONTROL | MOD_SHIFT)]
    [InlineData("Ctrl+Z", MOD_CONTROL)]
    [InlineData("Alt+Shift+Z", MOD_ALT | MOD_SHIFT)]
    public void ParseHotkey_ReturnsCorrectModifiers(string input, uint expectedMods)
    {
        var (modifiers, vk) = HotkeyListener.ParseHotkey(input);
        Assert.Equal(expectedMods, modifiers);
        Assert.NotEqual(0u, vk);
    }

    [Fact]
    public void ParseHotkey_DefaultHotkey_ReturnsVkForZ()
    {
        var (modifiers, vk) = HotkeyListener.ParseHotkey("Ctrl+Shift+Z");
        Assert.Equal(MOD_CONTROL | MOD_SHIFT, modifiers);
        Assert.Equal(0x5Au, vk); // Z = 0x5A
    }

    [Fact]
    public void ParseHotkey_ControlAlias_MatchesCtrl()
    {
        var (mod1, vk1) = HotkeyListener.ParseHotkey("Ctrl+Z");
        var (mod2, vk2) = HotkeyListener.ParseHotkey("Control+Z");
        Assert.Equal(mod1, mod2);
        Assert.Equal(vk1, vk2);
    }

    [Fact]
    public void ParseHotkey_WinModifier()
    {
        var (modifiers, vk) = HotkeyListener.ParseHotkey("Win+Space");
        Assert.Equal(MOD_WIN, modifiers);
        Assert.NotEqual(0u, vk);
    }

    [Fact]
    public void ParseHotkey_EmptyString_ReturnsZeroVk()
    {
        var (_, vk) = HotkeyListener.ParseHotkey("");
        Assert.Equal(0u, vk);
    }

    [Fact]
    public void ParseHotkey_InvalidKeyName_ReturnsZeroVk()
    {
        var (_, vk) = HotkeyListener.ParseHotkey("Ctrl+BadKey");
        Assert.Equal(0u, vk);
    }

    [Fact]
    public void ParseHotkey_NoModifier_ReturnsZeroModifiers()
    {
        var (modifiers, vk) = HotkeyListener.ParseHotkey("F12");
        Assert.Equal(0u, modifiers);
        Assert.NotEqual(0u, vk);
    }

    [Fact]
    public void ParseHotkey_CaseInsensitive()
    {
        var (mod1, vk1) = HotkeyListener.ParseHotkey("ctrl+shift+z");
        var (mod2, vk2) = HotkeyListener.ParseHotkey("CTRL+SHIFT+Z");
        Assert.Equal(mod1, mod2);
        Assert.Equal(vk1, vk2);
    }
}
