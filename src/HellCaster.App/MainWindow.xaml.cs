using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using HellCaster.Runtime;
using IOPath = System.IO.Path;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading.Tasks;

namespace HellCaster.App;

public partial class MainWindow : Window
{
    private const float WallWorldSize = 64f;
    private const int WallTextureSize = 128;
    private const float MouseTurnSensitivity = 0.12f;
    private const int ParallelPixelRowThreshold = 480;
    private const int ParallelWallRayThreshold = 240;

    private readonly GameEngine game = new();
    private readonly DispatcherTimer timer;
    private readonly HashSet<Key> pressed = [];
    private readonly PersistenceService persistence;
    private readonly Dictionary<int, Color[]> wallTexturePresets = [];
    private readonly Stopwatch frameClock = Stopwatch.StartNew();
    private readonly string[] textureSearchDirs =
    [
        IOPath.Combine(AppContext.BaseDirectory, "Textures"),
        IOPath.Combine(AppContext.BaseDirectory, "latest", "Textures"),
        IOPath.Combine(Environment.CurrentDirectory, "Textures")
    ];

    private readonly List<LeaderboardEntry> leaderboard;
    private GameSettings settings;
    private long lastFrameTimestamp;
    private bool inGame;
    private float mouseTurnAccumulator;
    private bool hasMouseSample;
    private bool suppressNextMouseSample;
    private bool isFullscreenApplied;
    private Rect restoreWindowBounds;
    private WriteableBitmap? sceneBitmap;
    private byte[]? scenePixels;
    private Image? sceneImage;
    private int sceneWidth;
    private int sceneHeight;

    public MainWindow()
    {
        InitializeComponent();
        InitializeWallTexturePresets();
        LoadExternalWallTextures();
        ApplyMenuBackgroundTexture();

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
            Interval = settings.Quality == QualityMode.Low
                ? TimeSpan.FromMilliseconds(22)
                : TimeSpan.FromMilliseconds(16)
        };
        timer.Tick += Tick;

        Loaded += (_, _) =>
        {
            Focus();
            lastFrameTimestamp = frameClock.ElapsedTicks;
            timer.Start();
            RefreshMenuInfo();
        };
    }

    private void ConfigureMenuControls()
    {
        ResolutionComboBox.ItemsSource = new[] { "960x540", "1280x720", "1600x900", "1920x1080" };
        DifficultyComboBox.ItemsSource = Enum.GetNames<DifficultyMode>();
        QualityComboBox.ItemsSource = Enum.GetNames<QualityMode>();
        PovComboBox.ItemsSource = new[] { "60", "68", "74", "82", "90", "100", "110" };

        PlayerNameTextBox.Text = settings.PlayerName;
        ResolutionComboBox.SelectedItem = $"{settings.ScreenWidth}x{settings.ScreenHeight}";
        if (ResolutionComboBox.SelectedItem is null)
        {
            ResolutionComboBox.SelectedItem = "1280x720";
        }

        DifficultyComboBox.SelectedItem = settings.Difficulty.ToString();
        QualityComboBox.SelectedItem = settings.Quality.ToString();
        PovComboBox.SelectedItem = ((int)MathF.Round(settings.PovDegrees)).ToString();
        if (PovComboBox.SelectedItem is null)
        {
            PovComboBox.SelectedItem = "74";
        }

        FullscreenCheckBox.IsChecked = settings.Fullscreen;
        SeedTextBox.Text = string.Empty;

        ShowHomePanel();
    }

    private void Tick(object? sender, EventArgs e)
    {
        var nowTicks = frameClock.ElapsedTicks;
        var dt = (float)(nowTicks - lastFrameTimestamp) / Stopwatch.Frequency;
        lastFrameTimestamp = nowTicks;

        if (!inGame || MenuRoot.Visibility == Visibility.Visible)
        {
            return;
        }

        if (game.IsGameOver && pressed.Contains(Key.R))
        {
            StartNewCampaignWithCurrentSettings();
            return;
        }

        var allowMouseFire = MenuRoot.Visibility != Visibility.Visible && Mouse.LeftButton == MouseButtonState.Pressed;
        var input = new InputState(
            pressed.Contains(Key.W),
            pressed.Contains(Key.S),
            pressed.Contains(Key.A),
            pressed.Contains(Key.D),
            pressed.Contains(Key.Left) || pressed.Contains(Key.Q),
            pressed.Contains(Key.Right) || pressed.Contains(Key.E),
            mouseTurnAccumulator,
            pressed.Contains(Key.Space) || allowMouseFire,
            pressed.Contains(Key.F));

        mouseTurnAccumulator = 0f;

        game.Update(input, dt);

        if (game.TryConsumeAutosaveFlag(out var autosave, settings.PlayerName) && autosave is not null)
        {
            persistence.SaveGame(autosave);
        }

        var updatedSnapshot = game.GetSnapshot();
        Render(updatedSnapshot);
    }

    private void Render(GameSnapshot snapshot)
    {
        var renderWidth = Math.Max(1, (int)Math.Round(GameCanvas.Width));
        var renderHeight = Math.Max(1, (int)Math.Round(GameCanvas.Height));
        EnsureSceneLayer(renderWidth, renderHeight);
        DrawSceneToBitmap(snapshot, renderWidth, renderHeight);

        GameCanvas.Children.Clear();
        if (sceneImage is not null)
        {
            GameCanvas.Children.Add(sceneImage);
        }

        DrawSprites(snapshot);
        DrawProjectiles(snapshot);
        DrawImpacts(snapshot);
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

    private void EnsureSceneLayer(int width, int height)
    {
        if (sceneBitmap is not null && scenePixels is not null && sceneWidth == width && sceneHeight == height)
        {
            return;
        }

        sceneWidth = width;
        sceneHeight = height;
        sceneBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        scenePixels = new byte[width * height * 4];
        sceneImage = new Image
        {
            Width = width,
            Height = height,
            Source = sceneBitmap,
            Stretch = Stretch.Fill,
            SnapsToDevicePixels = true
        };
    }

    private void DrawSceneToBitmap(GameSnapshot snapshot, int width, int height)
    {
        if (sceneBitmap is null || scenePixels is null)
        {
            return;
        }

        var pixels = scenePixels;
        FillSkyAndFloorPerspective(snapshot, pixels, width, height);

        var rays = snapshot.WallDistances;
        var shades = snapshot.WallShades;
        var textureU = snapshot.WallTextureU;
        var materials = snapshot.WallMaterialIds;

        if (rays.Count > 0)
        {
            var rayCount = rays.Count;
            var fov = Math.Clamp(snapshot.Fov, 0.35f, 2.4f);
            var projectionPlane = (float)(width * 0.5 / Math.Tan(fov * 0.5));

            void RenderWallColumn(int i)
            {
                var dist = Math.Max(0.01f, rays[i]);
                var projectedWallHeight = Math.Max(8f, (WallWorldSize / dist) * projectionPlane);
                var top = (height - projectedWallHeight) * 0.5f;
                var bottom = top + projectedWallHeight;

                var x0 = i * width / rayCount;
                var x1 = (i + 1) * width / rayCount;
                if (x1 <= x0)
                {
                    x1 = Math.Min(width, x0 + 1);
                }

                var y0 = Math.Clamp((int)Math.Floor(top), 0, height - 1);
                var y1 = Math.Clamp((int)Math.Ceiling(bottom), y0 + 1, height);

                var u = i < textureU.Count ? textureU[i] : 0f;
                var material = i < materials.Count ? materials[i] : 1;
                var shade = i < shades.Count ? shades[i] : 1f;

                for (var y = y0; y < y1; y++)
                {
                    var v = projectedWallHeight <= 0.0001f ? 0f : (y - top) / projectedWallHeight;
                    var closeRangeLod = Math.Clamp((projectedWallHeight - (float)height * 0.72f) / ((float)height * 0.85f), 0f, 0.46f);
                    SamplePresetTextureColorFast(material, u, v, shade, dist, closeRangeLod, out var r, out var g, out var b);

                    var rowIndex = y * width * 4;
                    for (var x = x0; x < x1; x++)
                    {
                        var idx = rowIndex + x * 4;
                        pixels[idx + 0] = b;
                        pixels[idx + 1] = g;
                        pixels[idx + 2] = r;
                        pixels[idx + 3] = 255;
                    }
                }
            }

            if (rayCount >= ParallelWallRayThreshold && Environment.ProcessorCount > 1)
            {
                Parallel.For(0, rayCount, RenderWallColumn);
            }
            else
            {
                for (var i = 0; i < rayCount; i++)
                {
                    RenderWallColumn(i);
                }
            }
        }

        sceneBitmap.WritePixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
    }

    private static void FillSkyAndFloorPerspective(GameSnapshot snapshot, byte[] pixels, int width, int height)
    {
        var half = height / 2f;
        var projectionPlane = (float)(width * 0.5 / Math.Tan(Math.Clamp(snapshot.Fov, 0.35f, 2.4f) * 0.5));
        var eyeHeight = WallWorldSize * 0.5f;

        var leftRayAngle = snapshot.PlayerAngle - snapshot.Fov * 0.5f;
        var rightRayAngle = snapshot.PlayerAngle + snapshot.Fov * 0.5f;
        var rayDirX0 = MathF.Cos(leftRayAngle);
        var rayDirY0 = MathF.Sin(leftRayAngle);
        var rayDirX1 = MathF.Cos(rightRayAngle);
        var rayDirY1 = MathF.Sin(rightRayAngle);

        var shadeBands = 18f;

        void FillRow(int y)
        {
            var isFloor = y >= half;
            var p = isFloor ? (y - half + 0.5f) : (half - y + 0.5f);
            var rowDistance = (eyeHeight * projectionPlane) / Math.Max(0.5f, p);

            var rowStartX = snapshot.Player.X + rowDistance * rayDirX0;
            var rowStartY = snapshot.Player.Y + rowDistance * rayDirY0;
            var floorStepX = rowDistance * (rayDirX1 - rayDirX0) / Math.Max(1, width);
            var floorStepY = rowDistance * (rayDirY1 - rayDirY0) / Math.Max(1, width);

            var fog = Math.Clamp(1f - rowDistance / 3000f, 0.2f, 1f);
            var bandedShade = MathF.Floor(fog * shadeBands) / shadeBands;
            if (!isFloor)
            {
                bandedShade *= 0.88f;
            }

            var worldX = rowStartX;
            var worldY = rowStartY;

            var rowIndex = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                SamplePlanarColor(worldX, worldY, isFloor, bandedShade, out var r, out var g, out var b);

                var idx = rowIndex + x * 4;
                pixels[idx + 0] = b;
                pixels[idx + 1] = g;
                pixels[idx + 2] = r;
                pixels[idx + 3] = 255;

                worldX += floorStepX;
                worldY += floorStepY;
            }
        }

        if (height >= ParallelPixelRowThreshold && Environment.ProcessorCount > 1)
        {
            Parallel.For(0, height, FillRow);
        }
        else
        {
            for (var y = 0; y < height; y++)
            {
                FillRow(y);
            }
        }
    }

    private static void SamplePlanarColor(float worldX, float worldY, bool isFloor, float shade, out byte r, out byte g, out byte b)
    {
        var tileX = (int)MathF.Floor(worldX / WallWorldSize);
        var tileY = (int)MathF.Floor(worldY / WallWorldSize);

        var checker = ((tileX ^ tileY) & 1) == 0;
        var grain = ((tileX * 73856093) ^ (tileY * 19349663)) & 31;
        var variation = grain / 31f;

        float baseR;
        float baseG;
        float baseB;

        if (isFloor)
        {
            baseR = checker ? 46f : 34f;
            baseG = checker ? 38f : 28f;
            baseB = checker ? 33f : 24f;
            baseR += variation * 8f;
            baseG += variation * 7f;
            baseB += variation * 6f;
        }
        else
        {
            baseR = checker ? 23f : 17f;
            baseG = checker ? 32f : 24f;
            baseB = checker ? 52f : 40f;
            baseR += variation * 6f;
            baseG += variation * 7f;
            baseB += variation * 9f;
        }

        var finalShade = Math.Clamp(shade, 0.16f, 1f);
        r = (byte)Math.Clamp(baseR * finalShade, 0f, 255f);
        g = (byte)Math.Clamp(baseG * finalShade, 0f, 255f);
        b = (byte)Math.Clamp(baseB * finalShade, 0f, 255f);
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

        var floorBands = settings.Quality switch
        {
            QualityMode.Low => 6,
            QualityMode.Medium => 10,
            QualityMode.High => 14,
            _ => 18
        };

        for (var i = 0; i < floorBands; i++)
        {
            var y = height * 0.5 + i * (height * 0.5 / floorBands);
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
        var textureU = snapshot.WallTextureU;
        var materialIds = snapshot.WallMaterialIds;
        if (rays.Count == 0)
        {
            return;
        }

        var width = GameCanvas.Width;
        var height = GameCanvas.Height;
        var columnWidth = width / rays.Count;
        var baseSamples = settings.Quality switch
        {
            QualityMode.Low => 3,
            QualityMode.Medium => 8,
            QualityMode.High => 14,
            _ => 22
        };

        for (var i = 0; i < rays.Count; i++)
        {
            var dist = Math.Max(0.01f, rays[i]);
            var wallHeight = Math.Clamp((float)(height * 118.0 / dist), 12f, (float)height);
            var top = (height - wallHeight) * 0.5;
            var u = i < textureU.Count ? textureU[i] : 0f;
            var material = i < materialIds.Count ? materialIds[i] : 1;
            var verticalSamples = Math.Clamp(Math.Max(baseSamples, (int)(wallHeight / 8f)), baseSamples, 36);
            var sampleHeight = wallHeight / verticalSamples;

            for (var sample = 0; sample < verticalSamples; sample++)
            {
                var v = verticalSamples == 1 ? 0f : (float)sample / (verticalSamples - 1);
                var color = SamplePresetTextureColor(material, u, v, shades[i], dist);

                var band = new Rectangle
                {
                    Width = Math.Ceiling(columnWidth + 1),
                    Height = sampleHeight + 1,
                    Fill = new SolidColorBrush(color)
                };

                Canvas.SetLeft(band, i * columnWidth);
                Canvas.SetTop(band, top + sample * sampleHeight);
                GameCanvas.Children.Add(band);
            }
        }
    }

    private Color SamplePresetTextureColor(int material, float u, float v, float shade, float distance)
    {
        u = u - MathF.Floor(u);
        v = v - MathF.Floor(v);

        var texture = wallTexturePresets.TryGetValue(material, out var pixels)
            ? pixels
            : wallTexturePresets[1];

        var x = Math.Clamp(u * (WallTextureSize - 1), 0f, WallTextureSize - 1);
        var y = Math.Clamp(v * (WallTextureSize - 1), 0f, WallTextureSize - 1);
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = Math.Min(x0 + 1, WallTextureSize - 1);
        var y1 = Math.Min(y0 + 1, WallTextureSize - 1);
        var fx = x - x0;
        var fy = y - y0;

        var c00 = texture[y0 * WallTextureSize + x0];
        var c10 = texture[y0 * WallTextureSize + x1];
        var c01 = texture[y1 * WallTextureSize + x0];
        var c11 = texture[y1 * WallTextureSize + x1];

        var r0 = c00.R + (c10.R - c00.R) * fx;
        var g0 = c00.G + (c10.G - c00.G) * fx;
        var b0 = c00.B + (c10.B - c00.B) * fx;

        var r1 = c01.R + (c11.R - c01.R) * fx;
        var g1 = c01.G + (c11.G - c01.G) * fx;
        var b1 = c01.B + (c11.B - c01.B) * fx;

        var color = Color.FromRgb(
            (byte)Math.Clamp(r0 + (r1 - r0) * fy, 0, 255),
            (byte)Math.Clamp(g0 + (g1 - g0) * fy, 0, 255),
            (byte)Math.Clamp(b0 + (b1 - b0) * fy, 0, 255));

        var fog = Math.Clamp(1.0 - distance / 3400.0, 0.5, 1.0);
        var finalShade = Math.Clamp(shade * fog, 0.12f, 1f);

        return Color.FromRgb(
            (byte)Math.Clamp(color.R * finalShade, 0, 255),
            (byte)Math.Clamp(color.G * finalShade, 0, 255),
            (byte)Math.Clamp(color.B * finalShade, 0, 255));
    }

    private void SamplePresetTextureColorFast(int material, float u, float v, float shade, float distance, float closeRangeLod, out byte r, out byte g, out byte b)
    {
        u = u - MathF.Floor(u);
        v = Math.Clamp(v, 0f, 1f);

        var texture = wallTexturePresets.TryGetValue(material, out var pixels)
            ? pixels
            : wallTexturePresets[1];

        var x = Math.Clamp(u * (WallTextureSize - 1), 0f, WallTextureSize - 1);
        var y = Math.Clamp(v * (WallTextureSize - 1), 0f, WallTextureSize - 1);
        var x0 = (int)MathF.Floor(x);
        var y0 = (int)MathF.Floor(y);
        var x1 = Math.Min(x0 + 1, WallTextureSize - 1);
        var y1 = Math.Min(y0 + 1, WallTextureSize - 1);
        var fx = x - x0;
        var fy = y - y0;

        var c00 = texture[y0 * WallTextureSize + x0];
        var c10 = texture[y0 * WallTextureSize + x1];
        var c01 = texture[y1 * WallTextureSize + x0];
        var c11 = texture[y1 * WallTextureSize + x1];

        var rr0 = c00.R + (c10.R - c00.R) * fx;
        var gg0 = c00.G + (c10.G - c00.G) * fx;
        var bb0 = c00.B + (c10.B - c00.B) * fx;

        var rr1 = c01.R + (c11.R - c01.R) * fx;
        var gg1 = c01.G + (c11.G - c01.G) * fx;
        var bb1 = c01.B + (c11.B - c01.B) * fx;

        var baseR = rr0 + (rr1 - rr0) * fy;
        var baseG = gg0 + (gg1 - gg0) * fy;
        var baseB = bb0 + (bb1 - bb0) * fy;

        if (closeRangeLod > 0.001f)
        {
            var centerX = (int)MathF.Round(x);
            var centerY = (int)MathF.Round(y);
            var xm = Math.Max(0, centerX - 1);
            var xp = Math.Min(WallTextureSize - 1, centerX + 1);
            var ym = Math.Max(0, centerY - 1);
            var yp = Math.Min(WallTextureSize - 1, centerY + 1);

            var cL = texture[centerY * WallTextureSize + xm];
            var cR = texture[centerY * WallTextureSize + xp];
            var cT = texture[ym * WallTextureSize + centerX];
            var cB = texture[yp * WallTextureSize + centerX];

            var blurR = (cL.R + cR.R + cT.R + cB.R) * 0.25f;
            var blurG = (cL.G + cR.G + cT.G + cB.G) * 0.25f;
            var blurB = (cL.B + cR.B + cT.B + cB.B) * 0.25f;

            var lod = Math.Clamp(closeRangeLod, 0f, 0.5f);
            baseR = baseR + (blurR - baseR) * lod;
            baseG = baseG + (blurG - baseG) * lod;
            baseB = baseB + (blurB - baseB) * lod;
        }

        var fog = Math.Clamp(1.0 - distance / 3400.0, 0.5, 1.0);
        var finalShade = Math.Clamp(shade * fog, 0.12f, 1f);

        r = (byte)Math.Clamp(baseR * finalShade, 0, 255);
        g = (byte)Math.Clamp(baseG * finalShade, 0, 255);
        b = (byte)Math.Clamp(baseB * finalShade, 0, 255);
    }

    private void InitializeWallTexturePresets()
    {
        wallTexturePresets[1] = BuildBrickTexture();
        wallTexturePresets[2] = BuildConcreteTexture();
        wallTexturePresets[3] = BuildMetalTexture();
        wallTexturePresets[4] = BuildMetalTexture();
    }

    private void LoadExternalWallTextures()
    {
        TryLoadExternalTexture(1, "wall_brick");
        TryLoadExternalTexture(2, "wall_concrete");
        TryLoadExternalTexture(3, "wall_metal");
        TryLoadExternalTextureFile(4, "wall_pentagram_1.jpg");
    }

    private void ApplyMenuBackgroundTexture()
    {
        var menuPath = TryResolveTexturePath("pentagram_menu.jpg");
        if (menuPath is null)
        {
            return;
        }

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(menuPath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();

            MenuRoot.Background = new ImageBrush(image)
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0.68
            };
        }
        catch
        {
        }
    }

    private void TryLoadExternalTexture(int materialId, string baseName)
    {
        foreach (var dir in textureSearchDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var candidates = new[]
            {
                IOPath.Combine(dir, $"{baseName}.png"),
                IOPath.Combine(dir, $"{baseName}.jpg"),
                IOPath.Combine(dir, $"{baseName}.jpeg")
            };

            foreach (var candidate in candidates)
            {
                if (!File.Exists(candidate))
                {
                    continue;
                }

                var loaded = TryLoadTexturePixels(candidate);
                if (loaded is not null)
                {
                    wallTexturePresets[materialId] = loaded;
                }

                return;
            }
        }
    }

    private void TryLoadExternalTextureFile(int materialId, string fileName)
    {
        var texturePath = TryResolveTexturePath(fileName);
        if (texturePath is null)
        {
            return;
        }

        var loaded = TryLoadTexturePixels(texturePath);
        if (loaded is not null)
        {
            wallTexturePresets[materialId] = loaded;
        }
    }

    private string? TryResolveTexturePath(string fileName)
    {
        foreach (var dir in textureSearchDirs)
        {
            if (!Directory.Exists(dir))
            {
                continue;
            }

            var candidate = IOPath.Combine(dir, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static Color[]? TryLoadTexturePixels(string filePath)
    {
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();

            var source = new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
            source.Freeze();

            var srcWidth = Math.Max(1, source.PixelWidth);
            var srcHeight = Math.Max(1, source.PixelHeight);
            var srcStride = srcWidth * 4;
            var srcBuffer = new byte[srcHeight * srcStride];
            source.CopyPixels(srcBuffer, srcStride, 0);

            var pixels = new Color[WallTextureSize * WallTextureSize];
            for (var y = 0; y < WallTextureSize; y++)
            {
                var srcY = Math.Clamp((int)((y + 0.5f) * srcHeight / WallTextureSize), 0, srcHeight - 1);
                for (var x = 0; x < WallTextureSize; x++)
                {
                    var srcX = Math.Clamp((int)((x + 0.5f) * srcWidth / WallTextureSize), 0, srcWidth - 1);
                    var idx = srcY * srcStride + srcX * 4;
                    var b = srcBuffer[idx + 0];
                    var g = srcBuffer[idx + 1];
                    var r = srcBuffer[idx + 2];
                    pixels[y * WallTextureSize + x] = Color.FromRgb(r, g, b);
                }
            }

            return pixels;
        }
        catch
        {
            return null;
        }
    }

    private static Color[] BuildBrickTexture()
    {
        var pixels = new Color[WallTextureSize * WallTextureSize];
        const int brickW = 16;
        const int brickH = 10;
        const int mortar = 1;

        for (var y = 0; y < WallTextureSize; y++)
        {
            var row = y / brickH;
            var offset = (row % 2) * (brickW / 2);

            for (var x = 0; x < WallTextureSize; x++)
            {
                var bx = (x + offset) % brickW;
                var by = y % brickH;

                if (bx < mortar || by < mortar)
                {
                    pixels[y * WallTextureSize + x] = Color.FromRgb(70, 65, 62);
                    continue;
                }

                var n = Hash((x + 1) * 0.23f, (y + 1) * 0.19f, 11);
                var r = (byte)Math.Clamp(132 + n * 52, 0, 255);
                var g = (byte)Math.Clamp(58 + n * 26, 0, 255);
                var b = (byte)Math.Clamp(44 + n * 18, 0, 255);
                pixels[y * WallTextureSize + x] = Color.FromRgb(r, g, b);
            }
        }

        return pixels;
    }

    private static Color[] BuildConcreteTexture()
    {
        var pixels = new Color[WallTextureSize * WallTextureSize];
        for (var y = 0; y < WallTextureSize; y++)
        {
            for (var x = 0; x < WallTextureSize; x++)
            {
                var n1 = Hash((x + 3) * 0.15f, (y + 7) * 0.15f, 31);
                var n2 = Hash((x + 5) * 0.5f, (y + 9) * 0.5f, 47);
                var tone = (byte)Math.Clamp(104 + n1 * 34 + n2 * 22, 0, 255);
                pixels[y * WallTextureSize + x] = Color.FromRgb(tone, tone, (byte)Math.Clamp(tone + 8, 0, 255));
            }
        }

        return pixels;
    }

    private static Color[] BuildMetalTexture()
    {
        var pixels = new Color[WallTextureSize * WallTextureSize];
        for (var y = 0; y < WallTextureSize; y++)
        {
            for (var x = 0; x < WallTextureSize; x++)
            {
                var brushed = 0.68f + 0.32f * MathF.Sin(y * 0.8f);
                var n = Hash((x + 13) * 0.35f, (y + 4) * 0.35f, 73);
                var seam = (x % 12 == 0 || y % 12 == 0) ? 0.58f : 1f;
                var baseTone = Math.Clamp((98 + 64 * brushed + 20 * n) * seam, 0, 255);
                var tone = (byte)baseTone;
                pixels[y * WallTextureSize + x] = Color.FromRgb((byte)Math.Clamp(tone * 0.82f, 0, 255), tone, (byte)Math.Clamp(tone * 1.06f, 0, 255));
            }
        }

        return pixels;
    }

    private static float Hash(float x, float y, int seed)
    {
        var value = MathF.Sin(x * 12.9898f + y * 78.233f + seed * 37.719f) * 43758.5453f;
        return value - MathF.Floor(value);
    }

    private void DrawSprites(GameSnapshot snapshot)
    {
        if (snapshot.WallDistances.Count == 0)
        {
            return;
        }

        var width = GameCanvas.Width;
        var height = GameCanvas.Height;
        var wallDepths = snapshot.WallDistances;
        var rayCount = Math.Max(1, wallDepths.Count);
        var columnWidth = width / rayCount;

        foreach (var sprite in snapshot.VisibleSprites)
        {
            var spriteWidth = sprite.Size * 0.75;
            var left = sprite.ScreenX * columnWidth - spriteWidth * 0.5;
            var right = left + spriteWidth;
            var bob = Math.Sin(sprite.MotionPhase * 2.0) * Math.Clamp(sprite.Size * 0.035, 0, 9);
            var top = height * 0.5 - sprite.Size * 0.5 - bob;

            var xStart = Math.Clamp((int)Math.Floor(left), 0, Math.Max(0, (int)width - 1));
            var xEnd = Math.Clamp((int)Math.Ceiling(right), xStart + 1, Math.Max(xStart + 1, (int)width));

            var visibleLeft = -1;
            var visibleRight = -1;

            for (var x = xStart; x < xEnd; x++)
            {
                var rayIndex = Math.Clamp((int)(x / Math.Max(1e-6, columnWidth)), 0, rayCount - 1);
                if (sprite.Depth <= wallDepths[rayIndex] - 1.5f)
                {
                    visibleLeft = visibleLeft < 0 ? x : visibleLeft;
                    visibleRight = x;
                }
            }

            if (visibleLeft < 0 || visibleRight < visibleLeft)
            {
                continue;
            }

            var clippedWidth = Math.Max(1, visibleRight - visibleLeft + 1);

            if (sprite.Kind.StartsWith("enemy", StringComparison.OrdinalIgnoreCase))
            {
                DrawEnemyModel(sprite, visibleLeft, top, clippedWidth);
                continue;
            }

            DrawObjectiveSprite(sprite, visibleLeft, top, clippedWidth);
        }
    }

    private void DrawEnemyModel(SpriteRenderView sprite, double visibleLeft, double top, double clippedWidth)
    {
        var hit = Math.Clamp(sprite.HitFlash, 0f, 1f);
        var armor = sprite.Kind switch
        {
            "enemy-scout" => Color.FromRgb(203, 66, 34),
            "enemy-brute" => Color.FromRgb(96, 46, 120),
            _ => Color.FromRgb(132, 38, 38)
        };

        var flesh = Color.FromRgb(47, 10, 10);
        var accent = sprite.Kind switch
        {
            "enemy-scout" => Color.FromRgb(255, 161, 72),
            "enemy-brute" => Color.FromRgb(188, 112, 255),
            _ => Color.FromRgb(244, 84, 66)
        };

        var flashMix = (byte)(hit * 180f);
        var armorBrush = new SolidColorBrush(Color.FromRgb(
            (byte)Math.Clamp(armor.R + flashMix, 0, 255),
            (byte)Math.Clamp(armor.G + flashMix * 0.36f, 0, 255),
            (byte)Math.Clamp(armor.B + flashMix * 0.24f, 0, 255)));

        var core = new Rectangle
        {
            Width = clippedWidth,
            Height = sprite.Size,
            Fill = new SolidColorBrush(flesh),
            Stroke = armorBrush,
            StrokeThickness = Math.Clamp(sprite.Size * 0.04, 1, 4),
            RadiusX = 6,
            RadiusY = 6
        };
        Canvas.SetLeft(core, visibleLeft);
        Canvas.SetTop(core, top);
        GameCanvas.Children.Add(core);

        var chest = new Polygon
        {
            Fill = armorBrush,
            Points =
            [
                new Point(visibleLeft + clippedWidth * 0.22, top + sprite.Size * 0.36),
                new Point(visibleLeft + clippedWidth * 0.50, top + sprite.Size * 0.16),
                new Point(visibleLeft + clippedWidth * 0.78, top + sprite.Size * 0.36),
                new Point(visibleLeft + clippedWidth * 0.66, top + sprite.Size * 0.82),
                new Point(visibleLeft + clippedWidth * 0.34, top + sprite.Size * 0.82)
            ]
        };
        GameCanvas.Children.Add(chest);

        var hornColor = new SolidColorBrush(Color.FromRgb(28, 9, 9));
        var leftHorn = new Polygon
        {
            Fill = hornColor,
            Points =
            [
                new Point(visibleLeft + clippedWidth * 0.14, top + sprite.Size * 0.18),
                new Point(visibleLeft + clippedWidth * 0.29, top - sprite.Size * 0.09),
                new Point(visibleLeft + clippedWidth * 0.36, top + sprite.Size * 0.26)
            ]
        };
        var rightHorn = new Polygon
        {
            Fill = hornColor,
            Points =
            [
                new Point(visibleLeft + clippedWidth * 0.86, top + sprite.Size * 0.18),
                new Point(visibleLeft + clippedWidth * 0.71, top - sprite.Size * 0.09),
                new Point(visibleLeft + clippedWidth * 0.64, top + sprite.Size * 0.26)
            ]
        };
        GameCanvas.Children.Add(leftHorn);
        GameCanvas.Children.Add(rightHorn);

        var eyeSize = Math.Max(4, sprite.Size * 0.1);
        var blink = MathF.Abs(MathF.Sin(sprite.MotionPhase * 3.1f));
        var eyeHeight = Math.Max(2.0, eyeSize * (0.38 + blink * 0.62));
        var eyeY = top + sprite.Size * 0.30;
        var eyeBrush = new SolidColorBrush(accent);

        var eyeLeft = new Ellipse
        {
            Width = eyeSize,
            Height = eyeHeight,
            Fill = eyeBrush
        };
        Canvas.SetLeft(eyeLeft, visibleLeft + clippedWidth * 0.29);
        Canvas.SetTop(eyeLeft, eyeY);
        GameCanvas.Children.Add(eyeLeft);

        var eyeRight = new Ellipse
        {
            Width = eyeSize,
            Height = eyeHeight,
            Fill = eyeBrush
        };
        Canvas.SetLeft(eyeRight, visibleLeft + clippedWidth * 0.62 - eyeSize);
        Canvas.SetTop(eyeRight, eyeY);
        GameCanvas.Children.Add(eyeRight);

        var sigil = new Rectangle
        {
            Width = Math.Max(2, clippedWidth * 0.07),
            Height = sprite.Size * 0.28,
            Fill = new SolidColorBrush(Color.FromArgb((byte)(170 + hit * 80), accent.R, accent.G, accent.B))
        };
        Canvas.SetLeft(sigil, visibleLeft + clippedWidth * 0.5 - sigil.Width * 0.5);
        Canvas.SetTop(sigil, top + sprite.Size * 0.45);
        GameCanvas.Children.Add(sigil);
    }

    private void DrawObjectiveSprite(SpriteRenderView sprite, double visibleLeft, double top, double clippedWidth)
    {
        var fill = sprite.Kind switch
        {
            "checkpoint" => new SolidColorBrush(Color.FromRgb(115, 52, 170)),
            "exit-open" => new SolidColorBrush(Color.FromRgb(255, 138, 74)),
            _ => new SolidColorBrush(Color.FromRgb(84, 56, 122))
        };

        var body = new Rectangle
        {
            Width = clippedWidth,
            Height = sprite.Size,
            Fill = fill,
            Stroke = new SolidColorBrush(Color.FromRgb(20, 10, 10)),
            StrokeThickness = 2,
            RadiusX = 8,
            RadiusY = 8
        };

        Canvas.SetLeft(body, visibleLeft);
        Canvas.SetTop(body, top);
        GameCanvas.Children.Add(body);

        var centerGlow = new Ellipse
        {
            Width = Math.Max(8, clippedWidth * 0.34),
            Height = Math.Max(8, clippedWidth * 0.34),
            Fill = new SolidColorBrush(sprite.Kind == "exit-open"
                ? Color.FromArgb(210, 255, 199, 124)
                : Color.FromArgb(210, 177, 96, 255))
        };
        Canvas.SetLeft(centerGlow, visibleLeft + clippedWidth * 0.5 - centerGlow.Width * 0.5);
        Canvas.SetTop(centerGlow, top + sprite.Size * 0.45 - centerGlow.Height * 0.5);
        GameCanvas.Children.Add(centerGlow);
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
                Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 136, 72)),
                StrokeThickness = 2.6
            };
            GameCanvas.Children.Add(line);

            var ember = new Line
            {
                X1 = (tail.X + head.X) * 0.5,
                Y1 = (tail.Y + head.Y) * 0.5,
                X2 = head.X,
                Y2 = head.Y,
                Stroke = new SolidColorBrush(Color.FromArgb(220, 255, 218, 170)),
                StrokeThickness = 1.4
            };
            GameCanvas.Children.Add(ember);

            var glow = new Ellipse
            {
                Width = 7,
                Height = 7,
                Fill = new SolidColorBrush(Color.FromArgb(250, 255, 238, 206))
            };
            Canvas.SetLeft(glow, head.X - 3.5);
            Canvas.SetTop(glow, head.Y - 3.5);
            GameCanvas.Children.Add(glow);
        }
    }

    private void DrawImpacts(GameSnapshot snapshot)
    {
        foreach (var impact in snapshot.Impacts)
        {
            var projected = ProjectWorldToScreen(snapshot, impact.X, impact.Y);
            if (!projected.Visible)
            {
                continue;
            }

            var strength = Math.Clamp(impact.Life / 0.24f, 0f, 1f);
            var size = Math.Clamp(impact.Radius * 0.9f, 4f, 24f) * (1.0 + (1.0 - strength) * 0.6);

            var color = impact.Kind switch
            {
                "enemy-hit" => Color.FromArgb((byte)(180 * strength), 255, 72, 62),
                _ => Color.FromArgb((byte)(170 * strength), 255, 172, 96)
            };

            var burst = new Ellipse
            {
                Width = size,
                Height = size,
                Fill = new SolidColorBrush(color),
                Stroke = new SolidColorBrush(Color.FromArgb((byte)(110 * strength), 255, 235, 180)),
                StrokeThickness = 1
            };

            Canvas.SetLeft(burst, projected.X - size * 0.5);
            Canvas.SetTop(burst, projected.Y - size * 0.5);
            GameCanvas.Children.Add(burst);
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
            Fill = new SolidColorBrush(Color.FromArgb(alpha, 255, 188, 132))
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
            "Rite objective: purge the marked hostiles and claim the exit sigil.\n" +
            "Autosave binds at checkpoints and level ascension.\n\n" +
            $"Current slayer: {settings.PlayerName}\n" +
            $"Resolution: {settings.ScreenWidth}x{settings.ScreenHeight}\n" +
            $"POV: {settings.PovDegrees:0}°\n" +
            $"Difficulty: {settings.Difficulty}\n" +
            $"Bound save: {saveInfo}";

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

        if (!isFullscreenApplied && WindowState == WindowState.Normal)
        {
            Width = Math.Max(980, width + 80);
            Height = Math.Max(700, height + 140);
        }
    }

    private void ApplyWindowMode(bool fullscreen)
    {
        if (fullscreen == isFullscreenApplied)
        {
            return;
        }

        isFullscreenApplied = fullscreen;
        if (fullscreen)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
            }

            restoreWindowBounds = new Rect(Left, Top, Width, Height);
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
        }
        else
        {
            WindowState = WindowState.Normal;
            WindowStyle = WindowStyle.SingleBorderWindow;
            ResizeMode = ResizeMode.CanResize;

            if (restoreWindowBounds.Width > 0 && restoreWindowBounds.Height > 0)
            {
                Left = restoreWindowBounds.Left;
                Top = restoreWindowBounds.Top;
                Width = restoreWindowBounds.Width;
                Height = restoreWindowBounds.Height;
            }

            ApplyResolution(settings.ScreenWidth, settings.ScreenHeight);
        }
    }

    private void EnterPlayMode()
    {
        MenuRoot.Visibility = Visibility.Collapsed;
        PlayRoot.Visibility = Visibility.Visible;
        Mouse.OverrideCursor = Cursors.None;
        Mouse.Capture(this, CaptureMode.Element);
        hasMouseSample = false;
        suppressNextMouseSample = true;
        Focus();
        CenterCursorInWindow();
    }

    private void EnterMenuMode()
    {
        MenuRoot.Visibility = Visibility.Visible;
        PlayRoot.Visibility = inGame ? Visibility.Visible : Visibility.Collapsed;
        Mouse.OverrideCursor = Cursors.Arrow;
        if (Mouse.Captured == this)
        {
            Mouse.Capture(null);
        }

        hasMouseSample = false;
        suppressNextMouseSample = true;
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

        var pov = float.TryParse(PovComboBox.SelectedItem?.ToString(), out var parsedPov)
            ? parsedPov
            : 74f;
        pov = Math.Clamp(pov, 60f, 110f);

        var fullscreen = FullscreenCheckBox.IsChecked == true;

        var playerName = string.IsNullOrWhiteSpace(PlayerNameTextBox.Text)
            ? "Player"
            : PlayerNameTextBox.Text.Trim();

        settings = new GameSettings(width, height, selectedDifficulty, playerName, quality, fullscreen, pov);
        persistence.SaveSettings(settings);

        timer.Interval = settings.Quality == QualityMode.Low
            ? TimeSpan.FromMilliseconds(22)
            : TimeSpan.FromMilliseconds(16);

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
        if (inGame && MenuRoot.Visibility != Visibility.Visible)
        {
            Mouse.Capture(this, CaptureMode.Element);
            suppressNextMouseSample = true;
            CenterCursorInWindow();
        }
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (!inGame || MenuRoot.Visibility == Visibility.Visible)
        {
            hasMouseSample = false;
            suppressNextMouseSample = true;
            return;
        }

        if (Mouse.Captured != this)
        {
            Mouse.Capture(this, CaptureMode.Element);
            suppressNextMouseSample = true;
            CenterCursorInWindow();
            return;
        }

        var position = e.GetPosition(this);
        var centerX = Math.Max(1.0, ActualWidth) * 0.5;

        if (suppressNextMouseSample)
        {
            suppressNextMouseSample = false;
            hasMouseSample = true;
            return;
        }

        if (!hasMouseSample)
        {
            hasMouseSample = true;
            return;
        }

        var deltaX = position.X - centerX;
        mouseTurnAccumulator += (float)deltaX * MouseTurnSensitivity;
        mouseTurnAccumulator = Math.Clamp(mouseTurnAccumulator, -5f, 5f);

        CenterCursorInWindow();
        suppressNextMouseSample = true;
    }

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int x, int y);

    private void CenterCursorInWindow()
    {
        if (!IsVisible || WindowState == WindowState.Minimized)
        {
            return;
        }

        var center = PointToScreen(new Point(ActualWidth * 0.5, ActualHeight * 0.5));
        SetCursorPos((int)Math.Round(center.X), (int)Math.Round(center.Y));
    }
}
