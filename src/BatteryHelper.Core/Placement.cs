namespace BatteryHelper.Core;

public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public bool Intersects(PixelRect other) => Left < other.Right && Right > other.Left && Top < other.Bottom && Bottom > other.Top;
}

public static class Placement
{
    public static PixelRect? FindTaskbarSlot(PixelRect monitor, PixelRect? taskbar,
        IReadOnlyList<PixelRect> occupied, bool reliable, Corner corner, int width, int height, int margin)
    {
        if (taskbar is { } bar && bar.Width > bar.Height && bar.Top >= monitor.Top + monitor.Height / 2 && reliable) {
            var blocks = occupied.Where(r => r.Intersects(bar)).OrderBy(r => r.Left).ToArray();
            var gaps = new List<(int Left, int Right)>();
            var cursor = bar.Left + margin;
            foreach (var block in blocks) {
                if (block.Left - margin > cursor) gaps.Add((cursor, block.Left - margin));
                cursor = Math.Max(cursor, block.Right + margin);
            }
            if (cursor < bar.Right - margin) gaps.Add((cursor, bar.Right - margin));
            var middle = bar.Left + bar.Width / 2;
            var candidates = gaps.Where(g => g.Right - g.Left >= width &&
                (corner == Corner.Left ? g.Left + width <= middle : g.Right - width >= middle)).ToArray();
            if (candidates.Length > 0 && bar.Height >= height) {
                var gap = corner == Corner.Left ? candidates[0] : candidates[^1];
                var left = corner == Corner.Left ? gap.Left : gap.Right - width;
                var top = bar.Top + (bar.Height - height) / 2;
                return new(left, top, left + width, top + height);
            }
        }
        return null;
    }
}
