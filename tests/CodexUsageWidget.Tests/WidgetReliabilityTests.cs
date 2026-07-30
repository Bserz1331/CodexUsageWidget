using System.Drawing;
using Xunit;

namespace CodexUsageWidget.Tests;

public sealed class WidgetReliabilityTests
{
    [Fact]
    public void ParseWithBackup_RecoversFromDamagedPrimarySettings()
    {
        WidgetSettings result = WidgetSettings.ParseWithBackup(
            "{ damaged json",
            """{"NormalMinutes":3,"IdleAfterMinutes":12,"IdleMinutes":8,"OpacityPercent":90}""");

        Assert.Equal(3, result.NormalMinutes);
        Assert.Equal(12, result.IdleAfterMinutes);
        Assert.Equal(8, result.IdleMinutes);
        Assert.Equal(90, result.OpacityPercent);
    }

    [Fact]
    public void Normalize_RepairsUnsafeSettingValues()
    {
        var settings = new WidgetSettings
        {
            NormalMinutes = 0,
            IdleAfterMinutes = -1,
            IdleMinutes = 9999,
            OpacityPercent = 0,
            HistoryRetentionDays = -5,
            UpdateManifestUrl = null
        };

        Assert.True(settings.Normalize());
        Assert.Equal(1, settings.NormalMinutes);
        Assert.Equal(10, settings.IdleAfterMinutes);
        Assert.Equal(10, settings.IdleMinutes);
        Assert.Equal(95, settings.OpacityPercent);
        Assert.Equal(90, settings.HistoryRetentionDays);
        Assert.Equal("", settings.UpdateManifestUrl);
    }

    [Theory]
    [InlineData(9.9, 239, 68, 68)]
    [InlineData(10, 245, 158, 11)]
    [InlineData(29.9, 245, 158, 11)]
    [InlineData(30, 34, 197, 94)]
    public void QuotaColor_UsesExactWarningBoundaries(double remaining, int red, int green, int blue)
    {
        Color result = WidgetUiPolicy.QuotaColor(remaining);

        Assert.Equal(Color.FromArgb(red, green, blue), result);
    }

    [Fact]
    public void SafeLocation_ResetsWindowThatIsOutsideEveryScreen()
    {
        Rectangle primary = new Rectangle(0, 0, 1920, 1040);

        Point result = WidgetUiPolicy.SafeLocation(
            5000, 5000, new Size(212, 42), new[] { primary }, primary);

        Assert.Equal(new Point(1688, 978), result);
    }

    [Fact]
    public void SafeLocation_ResetsWindowWithOnlyATinyVisibleArea()
    {
        Rectangle primary = new Rectangle(0, 0, 1920, 1040);

        Point result = WidgetUiPolicy.SafeLocation(
            1900, 1010, new Size(212, 42), new[] { primary }, primary);

        Assert.Equal(new Point(1688, 978), result);
    }

    [Fact]
    public void SafeLocation_PreservesValidSecondaryMonitorPosition()
    {
        Rectangle primary = new Rectangle(0, 0, 1920, 1040);
        Rectangle secondary = new Rectangle(1920, 0, 2560, 1400);

        Point result = WidgetUiPolicy.SafeLocation(
            2200, 400, new Size(212, 42), new[] { primary, secondary }, primary);

        Assert.Equal(new Point(2200, 400), result);
    }
}
