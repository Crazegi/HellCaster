namespace HellCaster.Runtime;

public sealed class GameEngine
{
    private const float TileSize = 64f;
    private const float MaxRayDistance = 4200f;
    private const float PlayerRadius = 14f;
    private const float ShootCooldown = 0.16f;

    private readonly List<Enemy> enemies = [];
    private readonly List<Bullet> bullets = [];
    private readonly Queue<string> recentEvents = new();

    private GeneratedLevel currentLevel = LevelGenerator.Create(1337, 1, DifficultyMode.Medium);
    private DifficultyMode difficulty;
    private int campaignSeed;
    private int levelIndex;
    private int score;
    private int totalKills;
    private int levelKills;
    private int checkpointIndex;
    private float shootTimer;
    private float spawnTimer;
    private float muzzleFlash;
    private float gameOverTimer;
    private float levelAdvanceTimer;
    private bool exitActivatedAnnounced;
    private bool objectiveComplete;
    private bool onExit;
    private bool pendingAutoSave;
    private bool tookDamageThisLevel;
    private int shotsFired;
    private int shotsHit;
    private AchievementState achievements = new(false, false, false, false, false);
    private ChallengeState challenges = new(false, false, false);

    private float[] rayDistances = [];
    private float[] rayShades = [];
    private float[] rayTextureU = [];
    private int[] rayMaterialIds = [];

    private Player player = new(0f, 0f, PlayerRadius, 100, 0f);

    public float Fov { get; private set; } = 74f * (MathF.PI / 180f);
    public int RayCount { get; private set; } = 320;
    public float WorldWidth => currentLevel.Width * TileSize;
    public float WorldHeight => currentLevel.Height * TileSize;

    public void StartNewCampaign(int seed, DifficultyMode selectedDifficulty)
    {
        campaignSeed = seed;
        levelIndex = 1;
        difficulty = selectedDifficulty;
        score = 0;
        totalKills = 0;
        achievements = new AchievementState(false, false, false, false, false);
        challenges = new ChallengeState(false, false, false);
        LoadLevel(levelIndex, resetHealth: true);
        AddEvent($"Campaign started. Seed {campaignSeed}.");
    }

    public void LoadFromSave(SaveGameData save)
    {
        campaignSeed = save.CampaignSeed;
        difficulty = save.Difficulty;
        levelIndex = save.LevelIndex;
        score = save.Score;
        totalKills = save.TotalKills;
        achievements = save.Achievements;
        challenges = save.Challenges;

        currentLevel = LevelGenerator.Create(campaignSeed, levelIndex, difficulty);
        ResetRuntimeForLevel(resetHealth: false);

        player.X = save.PlayerX;
        player.Y = save.PlayerY;
        player.Angle = save.PlayerAngle;
        player.Health = save.PlayerHealth;
        checkpointIndex = Math.Clamp(save.CheckpointIndex, 0, currentLevel.Checkpoints.Count);
        levelKills = save.LevelKills;
        objectiveComplete = save.LevelKills >= currentLevel.KillTarget;

        AddEvent("Save loaded.");
    }

    public void ApplySettings(GameSettings settings)
    {
        var qualityBase = settings.Quality switch
        {
            QualityMode.Low => 120,
            QualityMode.Medium => 180,
            QualityMode.High => 280,
            _ => 380
        };

        var widthDriven = settings.Quality switch
        {
            QualityMode.Low => settings.ScreenWidth / 9,
            QualityMode.Medium => settings.ScreenWidth / 7,
            QualityMode.High => settings.ScreenWidth / 6,
            _ => settings.ScreenWidth / 5
        };

        RayCount = settings.Quality switch
        {
            QualityMode.Low => Math.Clamp(Math.Max(qualityBase, widthDriven), 96, 180),
            QualityMode.Medium => Math.Clamp(Math.Max(qualityBase, widthDriven), 150, 260),
            QualityMode.High => Math.Clamp(Math.Max(qualityBase, widthDriven), 220, 380),
            _ => Math.Clamp(Math.Max(qualityBase, widthDriven), 300, 520)
        };

        Fov = Math.Clamp(settings.PovDegrees, 60f, 110f) * (MathF.PI / 180f);
        rayDistances = new float[RayCount];
        rayShades = new float[RayCount];
        rayTextureU = new float[RayCount];
        rayMaterialIds = new int[RayCount];
    }

    public SaveGameData CreateSaveData(string playerName)
    {
        return new SaveGameData(
            campaignSeed,
            levelIndex,
            difficulty,
            score,
            totalKills,
            player.Health,
            player.X,
            player.Y,
            player.Angle,
            checkpointIndex,
            levelKills,
            currentLevel.KillTarget,
            currentLevel.LevelSeed,
            DateTime.UtcNow,
            achievements,
            challenges,
            playerName);
    }

    public bool TryConsumeAutosaveFlag(out SaveGameData? save, string playerName)
    {
        if (!pendingAutoSave)
        {
            save = null;
            return false;
        }

        pendingAutoSave = false;
        save = CreateSaveData(playerName);
        return true;
    }

    public void Update(InputState input, float dt)
    {
        dt = MathF.Min(dt, 0.033f);
        muzzleFlash = MathF.Max(0f, muzzleFlash - dt * 5f);

        if (player.Health <= 0)
        {
            gameOverTimer += dt;
            return;
        }

        if (onExit && objectiveComplete)
        {
            levelAdvanceTimer += dt;
            if (levelAdvanceTimer >= 0.8f || input.Interact)
            {
                AdvanceLevel();
            }

            return;
        }

        UpdatePlayer(input, dt);
        UpdateBullets(dt);
        UpdateEnemies(dt);
        SpawnEnemies(dt);
        UpdateCheckpointAndExitState();
        CastRaysAndCacheSprites(out _);
    }

    public GameSnapshot GetSnapshot()
    {
        CastRaysAndCacheSprites(out var sprites);

        var checkpointLeft = Math.Max(0, currentLevel.Checkpoints.Count - checkpointIndex);
        var objectiveText = objectiveComplete
            ? "Objective done. Reach exit portal."
            : $"Eliminate {currentLevel.KillTarget - levelKills} more enemies.";

        return new GameSnapshot(
            WorldWidth,
            WorldHeight,
            new EntityView(player.X, player.Y, player.Radius, player.Health),
            enemies.Select(enemy => new EntityView(enemy.X, enemy.Y, enemy.Radius, enemy.Health)).ToArray(),
            bullets.Select(b => new BulletView(b.X, b.Y, b.Radius, b.VelocityX, b.VelocityY, b.Life)).ToArray(),
            rayDistances.ToArray(),
            rayShades.ToArray(),
            rayTextureU.ToArray(),
            rayMaterialIds.ToArray(),
            sprites,
            player.Angle,
            Fov,
            levelIndex,
            campaignSeed,
            currentLevel.KillTarget,
            levelKills,
            checkpointLeft,
            objectiveComplete,
            onExit,
            objectiveText,
            recentEvents.ToArray(),
            achievements,
            challenges,
            score,
            player.Health,
            muzzleFlash,
            player.Health <= 0,
            player.Health <= 0 ? MathF.Max(0f, 2.5f - gameOverTimer) : 0f);
    }

    private void LoadLevel(int newLevelIndex, bool resetHealth)
    {
        levelIndex = newLevelIndex;
        currentLevel = LevelGenerator.Create(campaignSeed, levelIndex, difficulty);
        ResetRuntimeForLevel(resetHealth);
        AddEvent($"Level {levelIndex} generated (seed {currentLevel.LevelSeed}).");
    }

    private void ResetRuntimeForLevel(bool resetHealth)
    {
        enemies.Clear();
        bullets.Clear();

        var startX = currentLevel.Start.X * TileSize;
        var startY = currentLevel.Start.Y * TileSize;

        player.X = startX;
        player.Y = startY;
        player.Angle = 0f;
        if (resetHealth)
        {
            player.Health = HealthByDifficulty(difficulty);
        }

        if (rayDistances.Length == 0)
        {
            rayDistances = new float[RayCount];
            rayShades = new float[RayCount];
            rayTextureU = new float[RayCount];
            rayMaterialIds = new int[RayCount];
        }

        shootTimer = 0f;
        muzzleFlash = 0f;
        spawnTimer = 1f;
        gameOverTimer = 0f;
        levelAdvanceTimer = 0f;
        exitActivatedAnnounced = false;
        levelKills = 0;
        checkpointIndex = 0;
        objectiveComplete = false;
        onExit = false;
        pendingAutoSave = false;
        tookDamageThisLevel = false;
        shotsFired = 0;
        shotsHit = 0;
        CastRaysAndCacheSprites(out _);
    }

    private void AdvanceLevel()
    {
        EvaluateChallengesForLevel();
        LoadLevel(levelIndex + 1, resetHealth: false);
        pendingAutoSave = true;

        if (levelIndex >= 5)
        {
            achievements = achievements with { Marathon = true };
        }

        if (difficulty == DifficultyMode.Hell && levelIndex >= 3)
        {
            achievements = achievements with { HellWalker = true };
        }
    }

    private void UpdatePlayer(InputState input, float dt)
    {
        var turn = 0f;
        if (input.TurnLeft)
        {
            turn -= 1f;
        }

        if (input.TurnRight)
        {
            turn += 1f;
        }

        turn += input.MouseTurnDelta;

        player.Angle = NormalizeAngle(player.Angle + turn * TurnSpeedByDifficulty(difficulty) * dt);

        var forwardX = MathF.Cos(player.Angle);
        var forwardY = MathF.Sin(player.Angle);
        var rightX = MathF.Cos(player.Angle + MathF.PI / 2f);
        var rightY = MathF.Sin(player.Angle + MathF.PI / 2f);

        var moveX = 0f;
        var moveY = 0f;

        if (input.MoveForward)
        {
            moveX += forwardX;
            moveY += forwardY;
        }

        if (input.MoveBackward)
        {
            moveX -= forwardX;
            moveY -= forwardY;
        }

        if (input.StrafeRight)
        {
            moveX += rightX;
            moveY += rightY;
        }

        if (input.StrafeLeft)
        {
            moveX -= rightX;
            moveY -= rightY;
        }

        var len = MathF.Sqrt(moveX * moveX + moveY * moveY);
        if (len > 0.001f)
        {
            moveX /= len;
            moveY /= len;
            var speed = MoveSpeedByDifficulty(difficulty);
            TryMovePlayer(moveX * speed * dt, moveY * speed * dt);
        }

        shootTimer -= dt;
        if (input.Fire && shootTimer <= 0f)
        {
            SpawnBullet(forwardX, forwardY);
            shootTimer = ShootCooldown;
            shotsFired++;
        }
    }

    private void TryMovePlayer(float dx, float dy)
    {
        var nextX = player.X + dx;
        var nextY = player.Y + dy;

        if (!IsWallAt(nextX, player.Y, player.Radius))
        {
            player.X = nextX;
        }

        if (!IsWallAt(player.X, nextY, player.Radius))
        {
            player.Y = nextY;
        }
    }

    private void SpawnBullet(float dirX, float dirY)
    {
        bullets.Add(new Bullet(
            player.X + dirX * (player.Radius + 10f),
            player.Y + dirY * (player.Radius + 10f),
            dirX * BulletSpeedByDifficulty(difficulty),
            dirY * BulletSpeedByDifficulty(difficulty),
            4f,
            1.1f));
            muzzleFlash = 1f;
    }

    private void UpdateBullets(float dt)
    {
        for (var i = bullets.Count - 1; i >= 0; i--)
        {
            var bullet = bullets[i];
            var prevX = bullet.X;
            var prevY = bullet.Y;
            var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(bullet.VelocityX * bullet.VelocityX + bullet.VelocityY * bullet.VelocityY) * dt / 12f));
            var stepDt = dt / steps;

            for (var step = 0; step < steps; step++)
            {
                prevX = bullet.X;
                prevY = bullet.Y;
                bullet.X += bullet.VelocityX * stepDt;
                bullet.Y += bullet.VelocityY * stepDt;

                if (IsWallAt(bullet.X, bullet.Y, bullet.Radius))
                {
                    bullet.Life = 0f;
                    break;
                }

                if (TryHitEnemyAlongSegment(prevX, prevY, bullet.X, bullet.Y, out var hitEnemyIndex))
                {
                    HandleEnemyHit(i, hitEnemyIndex, ref bullet);
                    break;
                }
            }

            bullet.Life -= dt;

            if (bullet.Life <= 0f)
            {
                bullets.RemoveAt(i);
                continue;
            }

            bullets[i] = bullet;
        }
    }

    private bool TryHitEnemyAlongSegment(float x1, float y1, float x2, float y2, out int enemyIndex)
    {
        enemyIndex = -1;
        var bestDistance = float.MaxValue;

        for (var i = 0; i < enemies.Count; i++)
        {
            var enemy = enemies[i];
            var distance = DistancePointToSegmentSquared(enemy.X, enemy.Y, x1, y1, x2, y2);
            var radius = enemy.Radius + 3f;
            if (distance > radius * radius)
            {
                continue;
            }

            var dx = enemy.X - x1;
            var dy = enemy.Y - y1;
            var toEnemy = dx * dx + dy * dy;
            if (toEnemy < bestDistance)
            {
                bestDistance = toEnemy;
                enemyIndex = i;
            }
        }

        return enemyIndex >= 0;
    }

    private void HandleEnemyHit(int bulletIndex, int enemyIndex, ref Bullet bullet)
    {
        shotsHit++;
        var enemy = enemies[enemyIndex];
        enemy.Health -= enemy.Kind switch
        {
            "enemy-brute" => 28f,
            _ => 35f
        };

        bullet.Life = 0f;

        if (enemy.Health <= 0f)
        {
            enemies.RemoveAt(enemyIndex);
            score += enemy.Kind switch
            {
                "enemy-brute" => 15,
                "enemy-scout" => 12,
                _ => 10
            };
            totalKills++;
            levelKills++;
            if (!achievements.FirstBlood)
            {
                achievements = achievements with { FirstBlood = true };
            }

            if (totalKills >= 100)
            {
                achievements = achievements with { Slayer = true };
            }

            objectiveComplete = levelKills >= currentLevel.KillTarget;
        }
        else
        {
            enemies[enemyIndex] = enemy;
        }
    }

    private void UpdateEnemies(float dt)
    {
        for (var i = enemies.Count - 1; i >= 0; i--)
        {
            var enemy = enemies[i];
            var dx = player.X - enemy.X;
            var dy = player.Y - enemy.Y;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len > 0.001f)
            {
                dx /= len;
                dy /= len;
            }

            var speed = EnemySpeedByDifficulty(difficulty);
            speed *= enemy.Kind switch
            {
                "enemy-scout" => 1.28f,
                "enemy-brute" => 0.78f,
                _ => 1f
            };
            var nextX = enemy.X + dx * speed * dt;
            var nextY = enemy.Y + dy * speed * dt;

            if (!IsWallAt(nextX, enemy.Y, enemy.Radius))
            {
                enemy.X = nextX;
            }

            if (!IsWallAt(enemy.X, nextY, enemy.Radius))
            {
                enemy.Y = nextY;
            }

            enemy.DamageCooldown -= dt;
            var playerDx = player.X - enemy.X;
            var playerDy = player.Y - enemy.Y;
            var impact = enemy.Radius + player.Radius + 2f;
            if (playerDx * playerDx + playerDy * playerDy <= impact * impact && enemy.DamageCooldown <= 0f)
            {
                player.Health = Math.Max(0, player.Health - DamagePerHitByDifficulty(difficulty));
                enemy.DamageCooldown = 0.9f;
                tookDamageThisLevel = true;
                if (player.Health <= 0)
                {
                    AddEvent("You were overrun.");
                }
            }

            enemies[i] = enemy;
        }
    }

    private void SpawnEnemies(float dt)
    {
        spawnTimer -= dt;
        if (spawnTimer > 0f)
        {
            return;
        }

        var baseSpawn = SpawnRateByDifficulty(difficulty);
        spawnTimer = MathF.Max(0.35f, baseSpawn - levelIndex * 0.03f);

        var spawnCount = difficulty switch
        {
            DifficultyMode.Easy => 1,
            DifficultyMode.Medium => 1,
            DifficultyMode.Hard => 2,
            _ => 2
        };

        for (var s = 0; s < spawnCount; s++)
        {
            for (var tries = 0; tries < 40; tries++)
            {
                var x = Random.Shared.Next(1, currentLevel.Width - 1);
                var y = Random.Shared.Next(1, currentLevel.Height - 1);
                if (GetMap(x, y) != 0)
                {
                    continue;
                }

                var wx = (x + 0.5f) * TileSize;
                var wy = (y + 0.5f) * TileSize;
                var dx = wx - player.X;
                var dy = wy - player.Y;
                if (dx * dx + dy * dy < TileSize * TileSize * 6f)
                {
                    continue;
                }

                var roll = Random.Shared.NextDouble();
                var kind = roll switch
                {
                    < 0.2 => "enemy-scout",
                    < 0.35 => "enemy-brute",
                    _ => "enemy"
                };
                var health = EnemyHealthByDifficulty(difficulty) * (kind == "enemy-brute" ? 1.5f : kind == "enemy-scout" ? 0.7f : 1f);
                var radius = kind == "enemy-brute" ? 15f : 11.5f;

                enemies.Add(new Enemy(wx, wy, radius, health, 0f, kind));
                break;
            }
        }
    }

    private void UpdateCheckpointAndExitState()
    {
        if (checkpointIndex < currentLevel.Checkpoints.Count)
        {
            var checkpoint = currentLevel.Checkpoints[checkpointIndex];
            var checkpointWorldX = checkpoint.X * TileSize;
            var checkpointWorldY = checkpoint.Y * TileSize;
            var dx = checkpointWorldX - player.X;
            var dy = checkpointWorldY - player.Y;
            if (dx * dx + dy * dy <= TileSize * 0.25f)
            {
                checkpointIndex++;
                pendingAutoSave = true;
                AddEvent($"Checkpoint {checkpointIndex} reached. Autosaved.");
            }
        }

        var exitX = currentLevel.Exit.X * TileSize;
        var exitY = currentLevel.Exit.Y * TileSize;
        var ex = exitX - player.X;
        var ey = exitY - player.Y;
        onExit = ex * ex + ey * ey <= TileSize * 0.35f;

        if (onExit && objectiveComplete && !exitActivatedAnnounced)
        {
            AddEvent("Exit activated. Advancing...");
            exitActivatedAnnounced = true;
        }

        if (!onExit)
        {
            exitActivatedAnnounced = false;
        }
    }

    private void CastRaysAndCacheSprites(out IReadOnlyList<SpriteRenderView> sprites)
    {
        var startAngle = player.Angle - Fov / 2f;
        var angleStep = Fov / (RayCount - 1);
        var playerMapX = player.X / TileSize;
        var playerMapY = player.Y / TileSize;

        for (var i = 0; i < RayCount; i++)
        {
            var rayAngle = startAngle + i * angleStep;
            var rayDirX = MathF.Cos(rayAngle);
            var rayDirY = MathF.Sin(rayAngle);

            var mapX = (int)MathF.Floor(playerMapX);
            var mapY = (int)MathF.Floor(playerMapY);

            var deltaDistX = MathF.Abs(rayDirX) < 0.0001f ? 1_000_000f : MathF.Abs(1f / rayDirX);
            var deltaDistY = MathF.Abs(rayDirY) < 0.0001f ? 1_000_000f : MathF.Abs(1f / rayDirY);

            int stepX;
            int stepY;
            float sideDistX;
            float sideDistY;

            if (rayDirX < 0f)
            {
                stepX = -1;
                sideDistX = (playerMapX - mapX) * deltaDistX;
            }
            else
            {
                stepX = 1;
                sideDistX = (mapX + 1f - playerMapX) * deltaDistX;
            }

            if (rayDirY < 0f)
            {
                stepY = -1;
                sideDistY = (playerMapY - mapY) * deltaDistY;
            }
            else
            {
                stepY = 1;
                sideDistY = (mapY + 1f - playerMapY) * deltaDistY;
            }

            var side = 0;
            var hit = false;
            var hitWallMaterial = 1;

            for (var guard = 0; guard < 256; guard++)
            {
                if (sideDistX < sideDistY)
                {
                    sideDistX += deltaDistX;
                    mapX += stepX;
                    side = 0;
                }
                else
                {
                    sideDistY += deltaDistY;
                    mapY += stepY;
                    side = 1;
                }

                var material = GetMap(mapX, mapY);
                if (material != 0)
                {
                    hitWallMaterial = material;
                    hit = true;
                    break;
                }
            }

            var perpWallDist = MaxRayDistance / TileSize;
            if (hit)
            {
                var safeRayDirX = MathF.Abs(rayDirX) < 0.0001f
                    ? (rayDirX < 0f ? -0.0001f : 0.0001f)
                    : rayDirX;
                var safeRayDirY = MathF.Abs(rayDirY) < 0.0001f
                    ? (rayDirY < 0f ? -0.0001f : 0.0001f)
                    : rayDirY;

                if (side == 0)
                {
                    perpWallDist = (mapX - playerMapX + (1 - stepX) * 0.5f) / safeRayDirX;
                }
                else
                {
                    perpWallDist = (mapY - playerMapY + (1 - stepY) * 0.5f) / safeRayDirY;
                }

                perpWallDist = MathF.Abs(perpWallDist);
            }

            var corrected = MathF.Max(0.001f, perpWallDist * TileSize);
            rayDistances[i] = corrected;
            rayShades[i] = Math.Clamp(1f - corrected / MaxRayDistance, 0.15f, 1f);

            var wallX = side == 0
                ? playerMapY + perpWallDist * rayDirY
                : playerMapX + perpWallDist * rayDirX;
            wallX -= MathF.Floor(wallX);

            var texU = wallX;
            if ((side == 0 && rayDirX > 0f) || (side == 1 && rayDirY < 0f))
            {
                texU = 1f - wallX;
            }

            rayTextureU[i] = Math.Clamp(texU, 0f, 1f);
            rayMaterialIds[i] = Math.Clamp(hitWallMaterial, 1, 3);
        }

        var spriteList = new List<SpriteRenderView>();
        foreach (var enemy in enemies)
        {
            AddSprite(enemy.X, enemy.Y, enemy.Health, enemy.Kind, spriteList);
        }

        for (var i = checkpointIndex; i < currentLevel.Checkpoints.Count; i++)
        {
            var checkpoint = currentLevel.Checkpoints[i];
            AddSprite(checkpoint.X * TileSize, checkpoint.Y * TileSize, 100f, "checkpoint", spriteList);
        }

        AddSprite(currentLevel.Exit.X * TileSize, currentLevel.Exit.Y * TileSize, 100f, objectiveComplete ? "exit-open" : "exit-locked", spriteList);

        sprites = spriteList.OrderByDescending(s => s.Depth).ToArray();
    }

    private void AddSprite(float worldX, float worldY, float health, string kind, List<SpriteRenderView> spriteList)
    {
        var dx = worldX - player.X;
        var dy = worldY - player.Y;
        var worldDistance = MathF.Sqrt(dx * dx + dy * dy);
        if (worldDistance <= 0.001f)
        {
            return;
        }

        var angleTo = NormalizeAngle(MathF.Atan2(dy, dx) - player.Angle);
        if (angleTo > MathF.PI)
        {
            angleTo -= 2f * MathF.PI;
        }

        if (MathF.Abs(angleTo) > Fov * 0.62f)
        {
            return;
        }

        var normalized = angleTo / (Fov / 2f);
        var screenX = (normalized * 0.5f + 0.5f) * RayCount;
        var rayIndex = Math.Clamp((int)screenX, 0, RayCount - 1);

        if (worldDistance >= rayDistances[rayIndex])
        {
            return;
        }

        var size = Math.Clamp((TileSize * 320f) / (worldDistance + 0.1f), 14f, 390f);
        spriteList.Add(new SpriteRenderView(screenX, size, worldDistance, health, kind));
    }

    private bool IsWallAt(float worldX, float worldY, float radius)
    {
        var left = (int)MathF.Floor((worldX - radius) / TileSize);
        var right = (int)MathF.Floor((worldX + radius) / TileSize);
        var top = (int)MathF.Floor((worldY - radius) / TileSize);
        var bottom = (int)MathF.Floor((worldY + radius) / TileSize);

        return GetMap(left, top) != 0 ||
               GetMap(right, top) != 0 ||
               GetMap(left, bottom) != 0 ||
               GetMap(right, bottom) != 0;
    }

    private int GetMap(int x, int y)
    {
        if (x < 0 || y < 0 || x >= currentLevel.Width || y >= currentLevel.Height)
        {
            return 1;
        }

        return currentLevel.Tiles[y * currentLevel.Width + x];
    }

    private void EvaluateChallengesForLevel()
    {
        if (!tookDamageThisLevel)
        {
            challenges = challenges with { NoDamageLevel = true };
            achievements = achievements with { Survivor = true };
        }

        if (levelKills >= currentLevel.KillTarget && spawnTimer > 0.6f)
        {
            challenges = challenges with { FastClear = true };
        }

        if (shotsFired >= 8 && (float)shotsHit / Math.Max(1, shotsFired) > 0.7f)
        {
            challenges = challenges with { PrecisionShooter = true };
        }
    }

    private void AddEvent(string message)
    {
        if (recentEvents.Count >= 4)
        {
            recentEvents.Dequeue();
        }

        recentEvents.Enqueue(message);
    }

    private static float DistancePointToSegmentSquared(float px, float py, float x1, float y1, float x2, float y2)
    {
        var dx = x2 - x1;
        var dy = y2 - y1;
        if (MathF.Abs(dx) < 0.0001f && MathF.Abs(dy) < 0.0001f)
        {
            var ex = px - x1;
            var ey = py - y1;
            return ex * ex + ey * ey;
        }

        var t = ((px - x1) * dx + (py - y1) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0f, 1f);
        var cx = x1 + t * dx;
        var cy = y1 + t * dy;
        var ox = px - cx;
        var oy = py - cy;
        return ox * ox + oy * oy;
    }

    private static float NormalizeAngle(float angle)
    {
        while (angle < 0f)
        {
            angle += 2f * MathF.PI;
        }

        while (angle >= 2f * MathF.PI)
        {
            angle -= 2f * MathF.PI;
        }

        return angle;
    }

    private static int HealthByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 130,
        DifficultyMode.Medium => 100,
        DifficultyMode.Hard => 85,
        _ => 70
    };

    private static float MoveSpeedByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 225f,
        DifficultyMode.Medium => 215f,
        DifficultyMode.Hard => 205f,
        _ => 195f
    };

    private static float TurnSpeedByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 2.5f,
        DifficultyMode.Medium => 2.35f,
        DifficultyMode.Hard => 2.2f,
        _ => 2.05f
    };

    private static float BulletSpeedByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 780f,
        DifficultyMode.Medium => 740f,
        DifficultyMode.Hard => 710f,
        _ => 680f
    };

    private static float EnemySpeedByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 86f,
        DifficultyMode.Medium => 98f,
        DifficultyMode.Hard => 115f,
        _ => 136f
    };

    private static float EnemyHealthByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 55f,
        DifficultyMode.Medium => 70f,
        DifficultyMode.Hard => 82f,
        _ => 94f
    };

    private static int DamagePerHitByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 7,
        DifficultyMode.Medium => 10,
        DifficultyMode.Hard => 13,
        _ => 16
    };

    private static float SpawnRateByDifficulty(DifficultyMode selectedDifficulty) => selectedDifficulty switch
    {
        DifficultyMode.Easy => 1.25f,
        DifficultyMode.Medium => 1.0f,
        DifficultyMode.Hard => 0.86f,
        _ => 0.72f
    };

    private sealed record Player(float X, float Y, float Radius, int Health, float Angle)
    {
        public float X { get; set; } = X;
        public float Y { get; set; } = Y;
        public float Radius { get; } = Radius;
        public int Health { get; set; } = Health;
        public float Angle { get; set; } = Angle;
    }

    private sealed record Enemy(float X, float Y, float Radius, float Health, float DamageCooldown, string Kind)
    {
        public float X { get; set; } = X;
        public float Y { get; set; } = Y;
        public float Radius { get; } = Radius;
        public float Health { get; set; } = Health;
        public float DamageCooldown { get; set; } = DamageCooldown;
        public string Kind { get; } = Kind;
    }

    private sealed record Bullet(float X, float Y, float VelocityX, float VelocityY, float Radius, float Life)
    {
        public float X { get; set; } = X;
        public float Y { get; set; } = Y;
        public float VelocityX { get; } = VelocityX;
        public float VelocityY { get; } = VelocityY;
        public float Radius { get; } = Radius;
        public float Life { get; set; } = Life;
    }
}
