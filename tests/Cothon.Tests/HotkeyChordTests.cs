using Cothon.Core.HotKeys;
using Xunit;

namespace Cothon.Tests;

/// <summary>
/// The mic hotkey chord: parse "Ctrl+Alt+Space"-style strings, round-trip
/// them for Settings, and map onto Win32 modifier/keydown values.
/// </summary>
public sealed class HotkeyChordTests
{
    [Fact]
    public void Parses_default_chord()
    {
        var chord = HotkeyChord.Parse("Ctrl+Alt+Space");

        Assert.True(chord.Ctrl);
        Assert.True(chord.Alt);
        Assert.False(chord.Shift);
        Assert.False(chord.Win);
        Assert.Equal(0x20u, chord.VirtualKey);
    }

    [Fact]
    public void Parses_case_insensitive_function_key()
    {
        var chord = HotkeyChord.Parse("f9");

        Assert.False(chord.Ctrl);
        Assert.Equal(0x78u, chord.VirtualKey);
    }

    [Fact]
    public void Parses_letters_and_digits()
    {
        Assert.Equal(0x4Du, HotkeyChord.Parse("Ctrl+M").VirtualKey);
        Assert.Equal(0x31u, HotkeyChord.Parse("Ctrl+1").VirtualKey);
    }

    [Fact]
    public void Round_trips_to_string()
    {
        Assert.Equal("Ctrl+Alt+Space", HotkeyChord.Parse("Ctrl+Alt+Space").ToString());
        Assert.Equal("F9", HotkeyChord.Parse("f9").ToString());
    }

    [Fact]
    public void Win32_modifier_flags_match_documented_values()
    {
        var mods = HotkeyChord.Parse("Ctrl+Alt+Space").ModifierFlags();

        Assert.Equal(0x0002u | 0x0001u, mods); // MOD_CONTROL | MOD_ALT
    }

    [Fact]
    public void Rejects_unknown_tokens()
    {
        Assert.Throws<FormatException>(() => HotkeyChord.Parse("Ctrl+Banana~"));
    }

    [Fact]
    public void Requires_at_least_one_modifier()
    {
        Assert.Throws<FormatException>(() => HotkeyChord.Parse("Space"));
    }
}
