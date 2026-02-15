namespace HellCaster.Runtime;

public enum DifficultyMode
{
    Easy,
    Medium,
    Hard,
    Hell
}

public enum QualityMode
{
    Low,
    Medium,
    High,
    Ultra
}

public sealed record GameSettings(
    int ScreenWidth,
    int ScreenHeight,
    DifficultyMode Difficulty,
    string PlayerName,
    QualityMode Quality,
    bool Fullscreen)
{
    public static GameSettings Default => new(1280, 720, DifficultyMode.Medium, "Player", QualityMode.High, false);
}

public sealed record ChallengeState(
    bool NoDamageLevel,
    bool FastClear,
    bool PrecisionShooter);

public sealed record AchievementState(
    bool FirstBlood,
    bool Survivor,
    bool Slayer,
    bool HellWalker,
    bool Marathon);

public sealed record LeaderboardEntry(
    string PlayerName,
    int Score,
    int HighestLevel,
    DifficultyMode Difficulty,
    DateTime AchievedAtUtc);

public sealed record SaveGameData(
    int CampaignSeed,
    int LevelIndex,
    DifficultyMode Difficulty,
    int Score,
    int TotalKills,
    int PlayerHealth,
    float PlayerX,
    float PlayerY,
    float PlayerAngle,
    int CheckpointIndex,
    int LevelKills,
    int KillTarget,
    int LastLevelSeed,
    DateTime SavedAtUtc,
    AchievementState Achievements,
    ChallengeState Challenges,
    string PlayerName);

public sealed record InputState(
    bool MoveForward,
    bool MoveBackward,
    bool StrafeLeft,
    bool StrafeRight,
    bool TurnLeft,
    bool TurnRight,
    float MouseTurnDelta,
    bool Fire,
    bool Interact);

public sealed record Vec2(float X, float Y);

public sealed record GeneratedLevel(
    int Width,
    int Height,
    int[] Tiles,
    Vec2 Start,
    Vec2 Exit,
    IReadOnlyList<Vec2> Checkpoints,
    int KillTarget,
    int LevelSeed);

public sealed record EntityView(float X, float Y, float Radius, float Health);

public sealed record BulletView(float X, float Y, float Radius, float VelocityX, float VelocityY, float Life);

public sealed record SpriteRenderView(float ScreenX, float Size, float Depth, float Health, string Kind);

public sealed record GameSnapshot(
    float WorldWidth,
    float WorldHeight,
    EntityView Player,
    IReadOnlyList<EntityView> Enemies,
    IReadOnlyList<BulletView> Bullets,
    IReadOnlyList<float> WallDistances,
    IReadOnlyList<float> WallShades,
    IReadOnlyList<SpriteRenderView> VisibleSprites,
    float PlayerAngle,
    float Fov,
    int LevelIndex,
    int CampaignSeed,
    int KillTarget,
    int LevelKills,
    int NextCheckpoint,
    bool ObjectiveComplete,
    bool OnExit,
    string TaskText,
    IReadOnlyList<string> RecentEvents,
    AchievementState Achievements,
    ChallengeState Challenges,
    int Score,
    int PlayerHealth,
    float MuzzleFlash,
    bool IsGameOver,
    float RemainingRespawnSeconds);
