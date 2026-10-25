using Avalonia;
using Avalonia.Styling;
using Xunit;

namespace Cothon.Tests;

/// <summary>Settings string to runtime theme variant mapping.</summary>
public sealed class UiAppearanceTests
{
    [Theory]
    [InlineData("light")]
    [InlineData("banana")]
    [InlineData("")]
    public void Unknown_or_light_resolves_light(string setting)
        => Assert.Same(ThemeVariant.Light, Cothon.UiAppearance.Resolve(setting));

    [Fact]
    public void Dark_resolves_dark()
        => Assert.Same(ThemeVariant.Dark, Cothon.UiAppearance.Resolve("dark"));

    [Fact]
    public void System_resolves_the_os_setting_not_default()
    {
        var resolved = Cothon.UiAppearance.Resolve("system");

        var isConcrete = ReferenceEquals(resolved, ThemeVariant.Dark)
            || ReferenceEquals(resolved, ThemeVariant.Light);
        Assert.True(isConcrete,
            "system means the OS decision (light/dark), provided before windows build.");
    }
}
