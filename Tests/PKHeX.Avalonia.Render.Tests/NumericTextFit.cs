using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.VisualTree;

namespace PKHeX.Avalonia.Render.Tests;

/// <summary>Checks that a <see cref="NumericUpDown"/> shows its whole number with real Skia text metrics.</summary>
internal static class NumericTextFit
{
    /// <summary>
    /// Fails when the displayed number extends past any clipping ancestor. A field that is too narrow
    /// cuts off the end of the number, so the trailing edge includes ink overhang. Leading ink is a font
    /// property (macOS draws "1" 1 px before the line start) that no width changes, so the leading edge
    /// is the line start.
    /// </summary>
    public static void AssertFits(NumericUpDown input, string expectedText, string name, StringBuilder diagnostics)
    {
        var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
        Assert.Equal(expectedText, presenter.Text);
        var line = Assert.Single(presenter.TextLayout.TextLines);
        var left = line.Start;
        var right = line.Start + line.WidthIncludingTrailingWhitespace - Math.Min(0, line.OverhangTrailing);
        var clips = presenter.GetVisualAncestors().Prepend(presenter).Where(visual => visual.ClipToBounds).ToArray();
        Assert.Contains(presenter.GetVisualAncestors().OfType<ScrollContentPresenter>().First(), clips);
        foreach (var clip in clips)
        {
            var projectedLeft = presenter.TranslatePoint(new Point(left, 0), clip)!.Value.X;
            var projectedRight = presenter.TranslatePoint(new Point(right, 0), clip)!.Value.X;
            diagnostics.AppendLine($"{name} {clip.GetType().Name}: bounds={clip.Bounds}, text=[{projectedLeft}, {projectedRight}]");
            Assert.True(projectedLeft >= 0 && projectedRight <= clip.Bounds.Width,
                $"{name} text \"{presenter.Text}\" [{projectedLeft}, {projectedRight}] crosses {clip.GetType().Name} [0, {clip.Bounds.Width}].");
        }
    }
}
