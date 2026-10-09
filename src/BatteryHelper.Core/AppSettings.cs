using System.Text.Json;

namespace BatteryHelper.Core;

public enum Corner { Left, Right }

public sealed record AppSettings
{
    public Corner Corner { get; init; } = Corner.Left;
    public string? MonitorDevice { get; init; }
    public bool Visible { get; init; } = true;
    public bool HideOnFullscreen { get; init; } = true;
    public bool AutoStart { get; init; } = true;

    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BatteryHelper");
    public static string FilePath => Path.Combine(DirectoryPath, "settings.json");

    public static AppSettings Load()
    {
        try { return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return new(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}
