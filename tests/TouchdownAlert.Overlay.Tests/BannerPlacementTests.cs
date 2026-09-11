using TouchdownAlert.Overlay.Services;

namespace TouchdownAlert.Overlay.Tests;

public class BannerPlacementTests
{
    [Fact]
    public void TopStrip_spans_the_full_width_flush_with_the_top_of_the_screen()
    {
        var screen = new ScreenInfo(true, 0, 0, 1920, 1080, 1.0);

        var (left, top, width) = ScreenPlacement.TopStrip(screen);

        Assert.Equal(0, left);
        Assert.Equal(0, top);
        Assert.Equal(1920, width);
    }

    [Fact]
    public void TopStrip_converts_a_secondary_hidpi_screen_to_dips()
    {
        // 2880x1800 physical at 150% sitting to the right of a 1920-wide primary.
        var screen = new ScreenInfo(false, 1920, 0, 2880, 1800, 1.5);

        var (left, top, width) = ScreenPlacement.TopStrip(screen);

        Assert.Equal(1280, left);
        Assert.Equal(0, top);
        Assert.Equal(1920, width);
    }

    [Fact]
    public void TopStrip_treats_a_missing_scale_as_one()
    {
        var screen = new ScreenInfo(true, 100, 50, 1000, 500, 0);

        var (left, top, width) = ScreenPlacement.TopStrip(screen);

        Assert.Equal(100, left);
        Assert.Equal(50, top);
        Assert.Equal(1000, width);
    }
}
