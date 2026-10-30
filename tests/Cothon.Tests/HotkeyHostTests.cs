using Cothon.Core.HotKeys;
using Xunit;

public class HotkeyHostTests
{
    private sealed class Ink : DispatcherLike
    {
        public int Taps;
        public void Post(Action action) { Taps++; }
    }

    [Fact]
    public void Two_hosts_register_distinct_chords_without_class_collisions()
    {
        var mic = new Win32HotkeyHost();
        var pill = new Win32HotkeyHost();
        try
        {
            mic.Start(HotkeyChord.Parse("Ctrl+Alt+8"), () => { }, new Ink());
            pill.Start(HotkeyChord.Parse("Ctrl+Alt+9"), () => { }, new Ink());

            // Chord conflicts are environmental and allowed; a window-class
            // collision is OUR bug and must never return.
            Assert.DoesNotContain("RegisterClassExW", mic.Error);
            Assert.DoesNotContain("RegisterClassExW", pill.Error);
        }
        finally
        {
            mic.Dispose();
            pill.Dispose();
        }
    }
}
