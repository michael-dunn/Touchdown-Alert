using TouchdownAlert.Overlay.Services;

namespace TouchdownAlert.Overlay.Tests;

public class ScreenPlacementTests
{
    private static readonly ScreenInfo Primary1080 = new(true, 0, 0, 1920, 1080, 1.0);
    private static readonly ScreenInfo Secondary1440 = new(false, 1920, 0, 1440, 900, 1.0);
    private static readonly ScreenInfo SecondaryHiDpi = new(false, 1920, 0, 2880, 1800, 1.5); // 1440x900 DIP at 150%

    [Fact]
    public void SelectScreen_primary_returns_the_primary_screen()
    {
        var screens = new[] { Primary1080, Secondary1440 };
        var result = ScreenPlacement.SelectScreen(screens, "primary");
        Assert.Equal(Primary1080, result);
    }

    [Fact]
    public void SelectScreen_secondary_returns_first_non_primary()
    {
        var screens = new[] { Primary1080, Secondary1440 };
        var result = ScreenPlacement.SelectScreen(screens, "secondary");
        Assert.Equal(Secondary1440, result);
    }

    [Fact]
    public void SelectScreen_secondary_falls_back_to_primary_when_only_one_screen()
    {
        var screens = new[] { Primary1080 };
        var result = ScreenPlacement.SelectScreen(screens, "secondary");
        Assert.Equal(Primary1080, result);
    }

    [Fact]
    public void SelectScreen_numeric_index_picks_that_screen()
    {
        var screens = new[] { Primary1080, Secondary1440 };
        var result = ScreenPlacement.SelectScreen(screens, "1");
        Assert.Equal(Secondary1440, result);
    }

    [Fact]
    public void SelectScreen_numeric_index_out_of_range_is_clamped()
    {
        var screens = new[] { Primary1080, Secondary1440 };
        var result = ScreenPlacement.SelectScreen(screens, "9");
        Assert.Equal(Secondary1440, result);
    }

    [Fact]
    public void SelectScreen_unrecognized_value_defaults_to_primary()
    {
        var screens = new[] { Primary1080, Secondary1440 };
        var result = ScreenPlacement.SelectScreen(screens, "banana");
        Assert.Equal(Primary1080, result);
    }

    [Fact]
    public void SelectScreen_empty_list_returns_fallback()
    {
        var result = ScreenPlacement.SelectScreen(Array.Empty<ScreenInfo>(), "primary");
        Assert.Equal(ScreenInfo.Fallback, result);
    }

    [Fact]
    public void DefaultTopRight_places_window_top_right_with_margin_at_96dpi()
    {
        var (left, top) = ScreenPlacement.DefaultTopRight(Primary1080, windowWidthDip: 300, windowHeightDip: 200, marginDip: 16);

        Assert.Equal(1920 - 300 - 16, left, 3);
        Assert.Equal(16, top, 3);
    }

    [Fact]
    public void DefaultTopRight_corrects_for_monitor_dpi_scale()
    {
        // Physical bounds 2880x1800 at 150% scale = 1920x1200 DIP, offset at x=1920 px = 1280 DIP.
        var (left, top) = ScreenPlacement.DefaultTopRight(SecondaryHiDpi, windowWidthDip: 300, windowHeightDip: 200, marginDip: 16);

        Assert.Equal(1920 / 1.5, 1280, 3);
        Assert.Equal(1280 + 1920 - 300 - 16, left, 3);
        Assert.Equal(16, top, 3);
    }
}
