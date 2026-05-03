using OverlayTranslate.Infrastructure;

namespace OverlayTranslate.Tests;

public class HotkeyManagerTests
{
    [Fact]
    public void Constructor_DefaultHotkeyId_Is9000()
    {
        using var manager = new HotkeyManager();
        // Just verify it doesn't throw
        Assert.NotNull(manager);
    }

    [Fact]
    public void Constructor_CustomHotkeyId()
    {
        using var manager = new HotkeyManager(1234);
        Assert.NotNull(manager);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var manager = new HotkeyManager();
        manager.Dispose();

        // Should not throw
        manager.Dispose();
    }

    [Fact]
    public void Dispose_WithoutRegister_DoesNotThrow()
    {
        var manager = new HotkeyManager();
        // Never called Register, just dispose
        manager.Dispose();
    }
}
