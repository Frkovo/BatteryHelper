using System.IO;

namespace BatteryHelper.App;

internal static class IconFactory
{
    public static System.Drawing.Icon Create()
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/BatteryHelper.ico"));
        using var stream = resource?.Stream ?? throw new FileNotFoundException("BatteryHelper icon missing.");
        using var icon = new System.Drawing.Icon(stream);
        return (System.Drawing.Icon)icon.Clone();
    }
}
