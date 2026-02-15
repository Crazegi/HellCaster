using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using HellCaster.Runtime;

namespace HellCaster.App;

public partial class MainWindow : Window
{
    private readonly GameEngine game = new();
    private readonly DispatcherTimer timer;
    private readonly HashSet<Key> pressed = [];
    private readonly PersistenceService persistence;

    private readonly List<LeaderboardEntry> leaderboard;
    private GameSettings settings;
    private DateTime lastFrameTime;
    private bool inGame;
    private float mouseTurnAccumulator;
    private bool hasMouseSample;
    private double previousMouseX;
    private bool isFullscreenApplied;

    public MainWindow()
    {
        InitializeComponent();

        persistence = new PersistenceService(PersistenceService.GetDefaultRoot());
        settings = persistence.LoadSettings();
        leaderboard = persistence.LoadLeaderboard();

        ConfigureMenuControls();
        ApplyResolution(settings.ScreenWidth, settings.ScreenHeight);
        ApplyWindowMode(settings.Fullscreen);
        EnterMenuMode();
        game.ApplySettings(settings);

        timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += Tick;

        Loaded += (_, _) =>
        {
            Focus();
            lastFrameTime = DateTime.UtcNow;
            timer.Start();
            RefreshMenuInfo();
        };
    }

    private void ConfigureMenuControls()
    {
        ResolutionComboBox.ItemsSource = new[] { "960x540", "1280x720", "1600x900", "1920x1080" };
        DifficultyComboBox.ItemsSource = Enum.GetNames<DifficultyMode>();
        QualityComboBox.ItemsSource = Enum.GetNames<QualityMode>();

        PlayerNameTextBox.Text = settings.PlayerName;
        ResolutionComboBox.SelectedItem = $"{settings.ScreenWidth}x{settings.ScreenHeight}";
        DifficultyComboBox.SelectedItem = settings.Difficulty.ToString();
        QualityComboBox.SelectedItem = settings.Quality.ToString();
        FullscreenCheckBox.IsChecked = settings.Fullscreen;
        SeedTextBox.Text = string.Empty;

        ShowHomePanel();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var now = DateTime.UtcNow;
        var dt = (float)(now - lastFrameTime).TotalSeconds;
        lastFrameTime = now;

        if (!inGame)
        {
            return;
        }

        var snapshot = game.GetSnapshot();
        if (snapshot.IsGameOver && pressed.Contains(Key.R))
        {
            StartNewCampaignWithCurrentSettings();
            return;
        }

        var input = new InputState(
            pressed.Contains(Key.W),
            pressed.Contains(Key.S),
            pressed.Contains(Key.A),
            pressed.Contains(Key.D),
            pressed.Contains(Key.Left) || pressed.Contains(Key.Q),
            pressed.Contains(Key.Right) || pressed.Contains(Key.E),
            mouseTurnAccumulator,
            pressed.Contains(Key.Space) || Mouse.LeftButton == MouseButtonState.Pressed,
            pressed.Contains(Key.F));

        mouseTurnAccumulator = 0f;

        game.Update(input, dt);

        if (game.TryConsumeAutosaveFlag(out var autosave, settings.PlayerName) && autosave is not null)
        {
            persistence.SaveGame(autosave);
        }

        Render(game.GetSnapshot());
    }

    private void Render(GameSnapshot snapshot)
    {
        GameCanvas.Children.Clear();

        DrawSkyAndFloor();
        DrawWalls(snapshot);
        DrawSprites(snapshot);
        DrawProjectiles(snapshot);
        DrawCrosshair();
        DrawWeaponModel(snapshot);
        DrawMuzzleFlash(snapshot);
        DrawVignette();
        DrawQualityOverlays();

        HudText.Text =
            $"HP {snapshot.PlayerHealth:000} | Score {snapshot.Score:00000} | Level {snapshot.LevelIndex} | Seed {snapshot.CampaignSeed}\n" +
            $"Kills {snapshot.LevelKills}/{snapshot.KillTarget} | Checkpoints left {snapshot.NextCheckpoint} | {settings.Difficulty}\n" +
            "Move W/S + Strafe A/D + Turn Q/E or Arrows + Shoot Space/Click + F interact + ESC menu";

        StatusText.Text =
            $"Task: {snapshot.TaskText}\n" +
            $"Objective complete: {(snapshot.ObjectiveComplete ? "YES" : "NO")}\n" +
            $"At exit: {(snapshot.OnExit ? "YES" : "NO")}\n" +
            $"Challenges\n" +
            $"  No damage level: {(snapshot.Challenges.NoDamageLevel ? "Done" : "Pending")}\n" +
            $"  Fast clear: {(snapshot.Challenges.FastClear ? "Done" : "Pending")}\n" +
            $"  Precision shooter: {(snapshot.Challenges.PrecisionShooter ? "Done" : "Pending")}\n" +
            $"Recent events\n  {string.Join("\n  ", snapshot.RecentEvents)}";

        if (snapshot.IsGameOver)
        {
            GameOverText.Visibility = Visibility.Visible;
            GameOverText.Text = $"YOU DIED\nRespawn in {snapshot.RemainingRespawnSeconds:0.0}s\nR: restart | ESC: menu";

            if (snapshot.RemainingRespawnSeconds <= 0.01f)
            {
                PersistRunToLeaderboard(snapshot);
            }
        }
        else
        {
            GameOverText.Visibility = Visibility.Collapsed;
        }
    }

    private void DrawSkyAndFloor()
    {
        var width = GameCanvas.Width;
        var height = GameCanvas.Height;

        var sky = new Rectangle
        {
            Width = width,
            Height = height * 0.5,
            Fill = new SolidColorBrush(Color.FromRgb(18, 26, 44))
        };
        GameCanvas.Children.Add(sky);

        var floor = new Rectangle
        {
            Width = width,
            Height = height * 0.5,
            Fill = new SolidColorBrush(Color.FromRgb(35, 24, 21))
        };
        Canvas.SetTop(floor, height * 0.5);
        GameCanvas.Children.Add(floor);

        for (var i = 0; i < 18; i++)
        {
            var y = height * 0.5 + i * (height * 0.5 / 18.0);
            var line = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = y,
                Y2 = y,
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(55 - i * 2), 110, 80, 70)),
                StrokeThickness = 1
            };
            GameCanvas.Children.Add(line);
        }
    }

    private void DrawWalls(GameSnapshot snapshot)
    {
        var rays = snapshot.WallDistances;
        var shades = snapshot.WallShades;
        if (rays.Count == 0)
        {
            return;
        }

        var width = GameCanvas.Width;
        var height = GameCanvas.Height;
        var columnWidth = width / rays.Count;

        for (var i = 0; i < rays.Count; i++)
        {
            var dist = Math.Max(0.01f, rays[i]);
            var wallHeight = Math.Clamp((float)(height * 118.0 / dist), 12f, (float)height);
            var top = (height - wallHeight) * 0.5;

            var shadeValue = (int)Math.Clamp(shades[i] * 255.0, 32.0, 255.0);
            var pattern = 0.82 + 0.18 * Math.Sin(i * 0.2 + dist * 0.015);
            var r = (byte)Math.Clamp(shadeValue * pattern, 0, 255);
            var g = (byte)Math.Clamp(shadeValue * 0.78 * pattern, 0, 255);
            var b = (byte)Math.Clamp(shadeValue * 0.68 * pattern, 0, 255);

            var wallColumn = new Rectangle
            {
                Width = Math.Ceiling(columnWidth + 1),
                Height = wallHeight,
                Fill = new SolidColorBrush(Color.FromRgb(r, g, b))
            };

            Canvas.SetLeft(wallColumn, i * columnWidth);
            Canvas.SetTop(wallColumn, top);
            GameCanvas.Children.Add(wallColumn);

            if (settings.Quality >= QualityMode.High && i % 6 == 0)
            {
                var seam = new Line
                {
                    X1 = i * columnWidth,
                    X2 = i * columnWidth,
                    Y1 = top,
                    Y2 = top + wallHeight,
                    Stroke = new SolidColorBrush(Color.FromArgb(75, 20, 20, 24)),
                    StrokeThickness = 1
                };
                GameCanvas.Children.Add(seam);
            }

            if (settings.Quality == QualityMode.Ultra && wallHeight > 40)
            {
                var mortarCount = Math.Max(1, (int)(wallHeight / 42));
                for (var lineIndex = 1; lineIndex <= mortarCount; lineIndex++)
                {
                    var y = top + lineIndex * (wallHeight / (mortarCount + 1));
                    var mortar = new Line
                    {
                        X1 = i * columnWidth,
                        X2 = i * columnWidth + columnWidth,
                        Y1 = y,
                        Y2 = y,
                        Stroke = new SolidColorBrush(Color.FromArgb(45, 30, 30, 30)),
                        StrokeThickness = 1
                    };
                    GameCanvas.Children.Add(mortar);
                }
            }
        }
    }

    private void DrawSprites(GameSnapshot snapshot)
    {
        if (snapshot.WallDistances.Count == 0)
        {
            return;
        }

        var width = GameCanvas.Width;
        var height = GameCanvas.Height;
        var columnWidth = width / snapshot.WallDistances.Count;

        foreach (var sprite in snapshot.VisibleSprites)
        {
            var spriteWidth = sprite.Size * 0.75;
            var left = sprite.ScreenX * columnWidth - spriteWidth * 0.5;
            var top = height * 0.5 - sprite.Size * 0.5;

            var fill = sprite.Kind switch
            {
                "enemy" => new SolidColorBrush(Color.FromRgb(191, 66, 66)),
                "enemy-scout" => new SolidColorBrush(Color.FromRgb(250, 120, 64)),
                "enemy-brute" => new SolidColorBrush(Color.FromRgb(130, 58, 170)),
                "checkpoint" => new SolidColorBrush(Color.FromRgb(88, 198, 255)),
                "exit-open" => new SolidColorBrush(Color.FromRgb(96, 255, 145)),
                _ => new SolidColorBrush(Color.FromRgb(170, 120, 255))
            };

            var body = new Rectangle
            {
                Width = spriteWidth,
                Height = sprite.Size,
                Fill = fill,
                Stroke = Brushes.Black,
                StrokeThickness = 2
            };

            Canvas.SetLeft(body, left);
            Canvas.SetTop(body, top);
            GameCanvas.Children.Add(body);

            if (sprite.Kind.StartsWith("enemy", StringComparison.OrdinalIgnoreCase))
            {
                var eyeSize = Math.Max(4, sprite.Size * 0.1);
                var eyeY = top + sprite.Size * 0.27;

                var eyeLeft = new Ellipse
                {
                    Width = eyeSize,
                    Height = eyeSize,
                    Fill = Brushes.Yellow
                };
                Canvas.SetLeft(eyeLeft, left + spriteWidth * 0.28);
                Canvas.SetTop(eyeLeft, eyeY);
                GameCanvas.Children.Add(eyeLeft);

                var eyeRight = new Ellipse
                {
                    Width = eyeSize,
                    Height = eyeSize,
                    Fill = Brushes.Yellow
                };
                Canvas.SetLeft(eyeRight, left + spriteWidth * 0.62 - eyeSize);
                Canvas.SetTop(eyeRight, eyeY);
                GameCanvas.Children.Add(eyeRight);
            }
        }
    }

    private void DrawProjectiles(GameSnapshot snapshot)
    {
        foreach (var bullet in snapshot.Bullets)
        {
            var dx = bullet.VelocityX;
            var dy = bullet.VelocityY;
            var len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f)
            {
                len = 1f;
            }

            dx /= len;
            dy /= len;
            var worldTrail = 28f;

            var tailX = bullet.X - dx * worldTrail;
            var tailY = bullet.Y - dy * worldTrail;

            var head = ProjectWorldToScreen(snapshot, bullet.X, bullet.Y);
            var tail = ProjectWorldToScreen(snapshot, tailX, tailY);

            if (!head.Visible || !tail.Visible)
            {
                continue;
            }

            var line = new Line
            {
                X1 = tail.X,
                Y1 = tail.Y,
                X2 = head.X,
                Y2 = head.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(210, 255, 214, 110)),
                StrokeThickness = 2
            };
            GameCanvas.Children.Add(line);

            var glow = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(Color.FromArgb(245, 255, 255, 220))
            };
            Canvas.SetLeft(glow, head.X - 2.5);
            Canvas.SetTop(glow, head.Y - 2.5);
            GameCanvas.Children.Add(glow);
        }
    }

    private void DrawMuzzleFlash(GameSnapshot snapshot)
    {
        if (snapshot.MuzzleFlash <= 0.01f)
        {
            return;
        }

        var alpha = (byte)Math.Clamp(snapshot.MuzzleFlash * 130f, 0f, 130f);
        var flash = new Rectangle
        {
            Width = GameCanvas.Width,
            Height = GameCanvas.Height,
            Fill = new SolidColorBrush(Color.FromArgb(alpha, 255, 225, 170))
        };

        GameCanvas.Children.Add(flash);
    }

    private void DrawWeaponModel(GameSnapshot snapshot)
    {
        var width = GameCanvas.Width;
        var height = GameCanvas.Height;
        var kick = snapshot.MuzzleFlash * 18.0;

        var baseShape = new Polygon
        {
            Fill = new SolidColorBrush(Color.FromRgb(38, 41, 52)),
            Stroke = new SolidColorBrush(Color.FromRgb(15, 15, 18)),
            StrokeThickness = 3,
            Points = new PointCollection
            {
                new(width * 0.40, height - 8),
                new(width * 0.46, height - 110 - kick),
                new(width * 0.54, height - 110 - kick),
                new(width * 0.60, height - 8)
            }
        };
        GameCanvas.Children.Add(baseShape);

        var barrelGlow = new Ellipse
        {
            Width = 24 + snapshot.MuzzleFlash * 18,
            Height = 24 + snapshot.MuzzleFlash * 18,
            Fill = new SolidColorBrush(Color.FromArgb((byte)(snapshot.MuzzleFlash * 220), 255, 220, 160))
        };
        Canvas.SetLeft(barrelGlow, width * 0.5 - barrelGlow.Width * 0.5);
        Canvas.SetTop(barrelGlow, height - 124 - kick - barrelGlow.Height * 0.5);
        GameCanvas.Children.Add(barrelGlow);
    }

    private void DrawVignette()
    {
        if (settings.Quality == QualityMode.Low)
        {
            return;
        }

        var width = GameCanvas.Width;
        var height = GameCanvas.Height;

        var top = new Rectangle
        {
            Width = width,
            Height = 70,
            Fill = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0))
        };
        GameCanvas.Children.Add(top);

        var bottom = new Rectangle
        {
            Width = width,
            Height = 100,
            Fill = new SolidColorBrush(Color.FromArgb(140, 0, 0, 0))
        };
        Canvas.SetTop(bottom, height - 100);
        GameCanvas.Children.Add(bottom);
    }

    private void DrawQualityOverlays()
    {
        if (settings.Quality != QualityMode.Ultra)
        {
            return;
        }

        var width = GameCanvas.Width;
        var height = GameCanvas.Height;
        for (var i = 0; i < height; i += 3)
        {
            var line = new Line
            {
                X1 = 0,
                X2 = width,
                Y1 = i,
                Y2 = i,
                Stroke = new SolidColorBrush(Color.FromArgb(14, 0, 0, 0)),
                StrokeThickness = 1
            };
            GameCanvas.Children.Add(line);
        }
    }

    private (bool Visible, double X, double Y) ProjectWorldToScreen(GameSnapshot snapshot, float worldX, float worldY)
    {
        var px = snapshot.Player.X;
        var py = snapshot.Player.Y;
        var dx = worldX - px;
        var dy = worldY - py;
        var distance = MathF.Sqrt(dx * dx + dy * dy);
        if (distance < 0.001f)
        {
            return (false, 0, 0);
        }

        var angleTo = MathF.Atan2(dy, dx) - snapshot.PlayerAngle;
        while (angleTo < -MathF.PI) angleTo += MathF.PI * 2f;
        while (angleTo > MathF.PI) angleTo -= MathF.PI * 2f;

        if (MathF.Abs(angleTo) > snapshot.Fov * 0.56f)
        {
            return (false, 0, 0);
        }

        var normalized = angleTo / (snapshot.Fov / 2f);
        var x = (normalized * 0.5 + 0.5) * GameCanvas.Width;
        var y = GameCanvas.Height * 0.5;
        return (true, x, y);
    }

    private void DrawCrosshair()
    {
        var centerX = GameCanvas.Width * 0.5;
        var centerY = GameCanvas.Height * 0.5;

        var horizontal = new Line
        {
            X1 = centerX - 10,
            X2 = centerX + 10,
            Y1 = centerY,
            Y2 = centerY,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        var vertical = new Line
        {
            X1 = centerX,
            X2 = centerX,
            Y1 = centerY - 10,
            Y2 = centerY + 10,
            Stroke = Brushes.White,
            StrokeThickness = 2
        };

        GameCanvas.Children.Add(horizontal);
        GameCanvas.Children.Add(vertical);
    }

    private void StartNewCampaignWithCurrentSettings()
    {
        var seed = int.TryParse(SeedTextBox.Text, out var customSeed)
            ? customSeed
            : Random.Shared.Next(10000, 999999);

        game.ApplySettings(settings);
        game.StartNewCampaign(seed, settings.Difficulty);
        inGame = true;

        EnterPlayMode();
    }

    private void RefreshMenuInfo()
    {
        var save = persistence.LoadSave();
        var saveInfo = save is null
            ? "No save present"
            : $"Save level {save.LevelIndex} | score {save.Score} | seed {save.CampaignSeed} | {save.Difficulty}";

        HomeInfoText.Text =
            "Goal: kill the level target and reach the exit portal.\n" +
            "Autosave occurs on checkpoints and level transitions.\n\n" +
            $"Current profile: {settings.PlayerName}\n" +
            $"Resolution: {settings.ScreenWidth}x{settings.ScreenHeight}\n" +
            $"Difficulty: {settings.Difficulty}\n" +
            $"Save: {saveInfo}";

        LeaderboardText.Text = BuildLeaderboardText();
        AchievementsText.Text = BuildAchievementsText();
    }

    private string BuildLeaderboardText()
    {
        if (leaderboard.Count == 0)
        {
            return "No entries yet.";
        }

        var lines = leaderboard
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.HighestLevel)
            .Take(10)
            .Select((entry, index) =>
                $"{index + 1,2}. {entry.PlayerName,-12} score {entry.Score,6} level {entry.HighestLevel,2} {entry.Difficulty}");

        return string.Join("\n", lines);
    }

    private string BuildAchievementsText()
    {
        var save = persistence.LoadSave();
        var achievements = save?.Achievements ?? new AchievementState(false, false, false, false, false);
        var challenges = save?.Challenges ?? new ChallengeState(false, false, false);

        return
            $"First Blood: {(achievements.FirstBlood ? "Unlocked" : "Locked")}\n" +
            $"Survivor (no-damage level): {(achievements.Survivor ? "Unlocked" : "Locked")}\n" +
            $"Slayer (100 kills): {(achievements.Slayer ? "Unlocked" : "Locked")}\n" +
            $"Hell Walker: {(achievements.HellWalker ? "Unlocked" : "Locked")}\n" +
            $"Marathon (5+ levels): {(achievements.Marathon ? "Unlocked" : "Locked")}\n\n" +
            $"Challenges\n" +
            $"No Damage Level: {(challenges.NoDamageLevel ? "Done" : "Pending")}\n" +
            $"Fast Clear: {(challenges.FastClear ? "Done" : "Pending")}\n" +
            $"Precision Shooter: {(challenges.PrecisionShooter ? "Done" : "Pending")}\n";
    }

    private void PersistRunToLeaderboard(GameSnapshot snapshot)
    {
        if (leaderboard.Any(e => e.PlayerName == settings.PlayerName && e.Score == snapshot.Score && e.HighestLevel == snapshot.LevelIndex))
        {
            return;
        }

        leaderboard.Add(new LeaderboardEntry(
            settings.PlayerName,
            snapshot.Score,
            snapshot.LevelIndex,
            settings.Difficulty,
            DateTime.UtcNow));

        leaderboard.Sort((a, b) => b.Score.CompareTo(a.Score));
        if (leaderboard.Count > 50)
        {
            leaderboard.RemoveRange(50, leaderboard.Count - 50);
        }

        persistence.SaveLeaderboard(leaderboard);
    }

    private void ShowHomePanel()
    {
        HomePanel.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        LeaderboardPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowSettingsPanel()
    {
        HomePanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;
        LeaderboardPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowLeaderboardPanel()
    {
        HomePanel.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Collapsed;
        LeaderboardPanel.Visibility = Visibility.Visible;
    }

    private void ApplyResolution(int width, int height)
    {
        GameCanvas.Width = width;
        GameCanvas.Height = height;

        if (!isFullscreenApplied)
        {
            Width = Math.Max(980, width + 80);
            Height = Math.Max(700, height + 140);
        }
    }

    private void ApplyWindowMode(bool fullscreen)
    {
        isFullscreenApplied = fullscreen;
        if (fullscreen)
        {
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
            }
        }
    }

    private void EnterPlayMode()
    {
        MenuRoot.Visibility = Visibility.Collapsed;
        PlayRoot.Visibility = Visibility.Visible;
        Mouse.OverrideCursor = Cursors.None;
        hasMouseSample = false;
        Focus();
    }

    private void EnterMenuMode()
    {
        MenuRoot.Visibility = Visibility.Visible;
        PlayRoot.Visibility = inGame ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = Cursors.Arrow;
        hasMouseSample = false;
    }

    private void NewGameButton_Click(object sender, RoutedEventArgs e)
    {
        StartNewCampaignWithCurrentSettings();
    }

    private void LoadGameButton_Click(object sender, RoutedEventArgs e)
    {
        var save = persistence.LoadSave();
        if (save is null)
        {
            MessageBox.Show("No save game found.", "Load", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        settings = settings with
        {
            Difficulty = save.Difficulty,
            PlayerName = save.PlayerName
        };

        game.ApplySettings(settings);
        game.LoadFromSave(save);
        inGame = true;
        EnterPlayMode();
    }

    private void SaveNowButton_Click(object sender, RoutedEventArgs e)
    {
        if (!inGame)
        {
            MessageBox.Show("Start or load a run first.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var save = game.CreateSaveData(settings.PlayerName);
        persistence.SaveGame(save);
        RefreshMenuInfo();
        MessageBox.Show("Game saved.", "Save", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void OpenSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsPanel();
    }

    private void OpenLeaderboardButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshMenuInfo();
        ShowLeaderboardPanel();
    }

    private void ResumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!inGame)
        {
            ShowHomePanel();
            return;
        }

        MenuRoot.Visibility = Visibility.Collapsed;
        EnterPlayMode();
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void ApplySettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Enum.TryParse<DifficultyMode>(DifficultyComboBox.SelectedItem?.ToString(), out var selectedDifficulty))
        {
            selectedDifficulty = DifficultyMode.Medium;
        }

        var resolution = ResolutionComboBox.SelectedItem?.ToString() ?? "1280x720";
        var split = resolution.Split('x');
        var width = split.Length == 2 && int.TryParse(split[0], out var rw) ? rw : 1280;
        var height = split.Length == 2 && int.TryParse(split[1], out var rh) ? rh : 720;

        if (!Enum.TryParse(QualityComboBox.SelectedItem?.ToString(), out QualityMode quality))
        {
            quality = QualityMode.High;
        }

        var fullscreen = FullscreenCheckBox.IsChecked == true;

        var playerName = string.IsNullOrWhiteSpace(PlayerNameTextBox.Text)
            ? "Player"
            : PlayerNameTextBox.Text.Trim();

        settings = new GameSettings(width, height, selectedDifficulty, playerName, quality, fullscreen);
        persistence.SaveSettings(settings);

        ApplyResolution(width, height);
        ApplyWindowMode(fullscreen);
        game.ApplySettings(settings);
        RefreshMenuInfo();

        MessageBox.Show("Settings applied.", "Settings", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        pressed.Add(e.Key);

        if (e.Key == Key.Escape)
        {
            EnterMenuMode();
            RefreshMenuInfo();
            ShowHomePanel();
        }
    }

    private void Window_KeyUp(object sender, KeyEventArgs e)
    {
        pressed.Remove(e.Key);
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        Focus();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!inGame || MenuRoot.Visibility == Visibility.Visible)
        {
            hasMouseSample = false;
            return;
        }

        var position = e.GetPosition(this);
        if (!hasMouseSample)
        {
            previousMouseX = position.X;
            hasMouseSample = true;
            return;
        }

        var deltaX = position.X - previousMouseX;
        previousMouseX = position.X;
        mouseTurnAccumulator += (float)deltaX * 0.09f;
        mouseTurnAccumulator = Math.Clamp(mouseTurnAccumulator, -3f, 3f);
    }
}
