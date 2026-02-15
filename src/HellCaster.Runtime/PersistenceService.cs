using System.Text.Json;

namespace HellCaster.Runtime;

public sealed class PersistenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private readonly string rootDir;

    public PersistenceService(string rootDir)
    {
        this.rootDir = rootDir;
        Directory.CreateDirectory(rootDir);
    }

    public static string GetDefaultRoot()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "HellCaster");
    }

    public GameSettings LoadSettings()
    {
        var path = Path.Combine(rootDir, "settings.json");
        if (!File.Exists(path))
        {
            return GameSettings.Default;
        }

        var loaded = JsonSerializer.Deserialize<GameSettings>(File.ReadAllText(path), JsonOptions);
        if (loaded is null)
        {
            return GameSettings.Default;
        }

        var width = loaded.ScreenWidth <= 0 ? GameSettings.Default.ScreenWidth : loaded.ScreenWidth;
        var height = loaded.ScreenHeight <= 0 ? GameSettings.Default.ScreenHeight : loaded.ScreenHeight;
        var playerName = string.IsNullOrWhiteSpace(loaded.PlayerName) ? "Player" : loaded.PlayerName;
        var quality = Enum.IsDefined(typeof(QualityMode), loaded.Quality) ? loaded.Quality : QualityMode.High;
        var difficulty = Enum.IsDefined(typeof(DifficultyMode), loaded.Difficulty) ? loaded.Difficulty : DifficultyMode.Medium;
        var povDegrees = loaded.PovDegrees < 45f || loaded.PovDegrees > 120f ? GameSettings.Default.PovDegrees : loaded.PovDegrees;

        return loaded with
        {
            ScreenWidth = width,
            ScreenHeight = height,
            PlayerName = playerName,
            Quality = quality,
            Difficulty = difficulty,
            PovDegrees = povDegrees
        };
    }

    public void SaveSettings(GameSettings settings)
    {
        var path = Path.Combine(rootDir, "settings.json");
        File.WriteAllText(path, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public SaveGameData? LoadSave()
    {
        var path = Path.Combine(rootDir, "savegame.json");
        if (!File.Exists(path))
        {
            return null;
        }

        return JsonSerializer.Deserialize<SaveGameData>(File.ReadAllText(path), JsonOptions);
    }

    public void SaveGame(SaveGameData save)
    {
        var path = Path.Combine(rootDir, "savegame.json");
        File.WriteAllText(path, JsonSerializer.Serialize(save, JsonOptions));
    }

    public List<LeaderboardEntry> LoadLeaderboard()
    {
        var path = Path.Combine(rootDir, "leaderboard.json");
        if (!File.Exists(path))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<LeaderboardEntry>>(File.ReadAllText(path), JsonOptions) ?? [];
    }

    public void SaveLeaderboard(List<LeaderboardEntry> entries)
    {
        var path = Path.Combine(rootDir, "leaderboard.json");
        File.WriteAllText(path, JsonSerializer.Serialize(entries, JsonOptions));
    }
}
