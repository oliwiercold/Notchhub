using System.Globalization;
using System.Windows;
using NotchHub.Core;
using NotchHub.UI.Converters;
using NotchHub.UI.Modding;

namespace NotchHub.Features.Controls;

/// <summary>Number of mod tiles → UniformGrid columns (1..3; more tiles wrap to new rows).</summary>
public sealed class TileColumns : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Math.Clamp(value is int n ? n : 1, 1, 3);
}

/// <summary>ModTile → whether it has a chevron (OnChevron set).</summary>
public sealed class HasChevron : ConverterBase
{
    public override object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is ModTile { OnChevron: not null };
}

public partial class ControlsView
{
    private void OnModTileClick(object sender, RoutedEventArgs e) => Invoke(sender, chevron: false);
    private void OnModTileChevronClick(object sender, RoutedEventArgs e) => Invoke(sender, chevron: true);

    /// <summary>Mod callbacks are third-party code: never let them take the panel down.</summary>
    private static void Invoke(object sender, bool chevron)
    {
        if (sender is not FrameworkElement { DataContext: ModTile tile }) return;
        var action = chevron ? tile.OnChevron : tile.OnClick;
        if (action is null) return;
        try { action(); }
        catch (Exception ex) { Log.Warn($"Mods: tile {tile.ModId}/{tile.Id} {(chevron ? "chevron" : "click")} failed", ex); }
    }
}
