namespace HellCaster.Runtime;

public static class LevelGenerator
{
    public static GeneratedLevel Create(int campaignSeed, int levelIndex, DifficultyMode difficulty)
    {
        var levelSeed = campaignSeed + levelIndex * 7919;
        var random = new Random(levelSeed);

        var width = 41;
        var height = 41;
        var tiles = new int[width * height];
        Array.Fill(tiles, 1);

        CarveMaze(tiles, width, height, random);
        WidenCorridors(tiles, width, height, passes: 1, random);
        CarveRooms(tiles, width, height, random, roomCount: 5 + levelIndex / 3);

        var startCell = FindNearestOpen(tiles, width, height, 2, 2);
        var dist = ComputeDistances(tiles, width, height, startCell.X, startCell.Y);
        var farthest = FindFarthestOpenCell(tiles, dist, width, height);

        var path = BuildPath(tiles, width, height, startCell.X, startCell.Y, farthest.X, farthest.Y);
        if (path.Count < 12)
        {
            CarveMainTunnel(tiles, width, height, startCell.X, startCell.Y, farthest.X, farthest.Y);
            path = BuildPath(tiles, width, height, startCell.X, startCell.Y, farthest.X, farthest.Y);
        }

        var checkpoints = CreateCheckpoints(path);
        var killTarget = BaseKillTarget(difficulty) + levelIndex * 2;
        ApplyWallMaterials(tiles, width, height, random, levelIndex);

        return new GeneratedLevel(
            width,
            height,
            tiles,
            new Vec2(startCell.X + 0.5f, startCell.Y + 0.5f),
            new Vec2(farthest.X + 0.5f, farthest.Y + 0.5f),
            checkpoints,
            killTarget,
            levelSeed);
    }

    private static void ApplyWallMaterials(int[] tiles, int width, int height, Random random, int levelIndex)
    {
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                if (tiles[index] == 0)
                {
                    continue;
                }

                if (x == 0 || y == 0 || x == width - 1 || y == height - 1)
                {
                    tiles[index] = 1;
                    continue;
                }

                var openNeighbors = 0;
                if (tiles[y * width + (x - 1)] == 0) openNeighbors++;
                if (tiles[y * width + (x + 1)] == 0) openNeighbors++;
                if (tiles[(y - 1) * width + x] == 0) openNeighbors++;
                if (tiles[(y + 1) * width + x] == 0) openNeighbors++;

                var material = 1;
                if (openNeighbors >= 3)
                {
                    material = 3;
                }
                else if (openNeighbors == 2 && ((x + y + levelIndex) % 3 == 0 || random.NextDouble() < 0.18))
                {
                    material = 2;
                }
                else if (random.NextDouble() < 0.06)
                {
                    material = 3;
                }

                tiles[index] = material;
            }
        }
    }

    private static void CarveMaze(int[] tiles, int width, int height, Random random)
    {
        var stack = new Stack<(int X, int Y)>();
        var start = (X: 1, Y: 1);
        tiles[start.Y * width + start.X] = 0;
        stack.Push(start);

        var dirs = new (int X, int Y)[] { (2, 0), (-2, 0), (0, 2), (0, -2) };

        while (stack.Count > 0)
        {
            var current = stack.Peek();
            var shuffled = dirs.OrderBy(_ => random.Next()).ToArray();
            var carved = false;

            foreach (var dir in shuffled)
            {
                var nx = current.X + dir.X;
                var ny = current.Y + dir.Y;
                if (nx <= 0 || ny <= 0 || nx >= width - 1 || ny >= height - 1)
                {
                    continue;
                }

                if (tiles[ny * width + nx] == 0)
                {
                    continue;
                }

                tiles[(current.Y + dir.Y / 2) * width + (current.X + dir.X / 2)] = 0;
                tiles[ny * width + nx] = 0;
                stack.Push((nx, ny));
                carved = true;
                break;
            }

            if (!carved)
            {
                stack.Pop();
            }
        }
    }

    private static void WidenCorridors(int[] tiles, int width, int height, int passes, Random random)
    {
        for (var pass = 0; pass < passes; pass++)
        {
            var copy = tiles.ToArray();
            for (var y = 1; y < height - 1; y++)
            {
                for (var x = 1; x < width - 1; x++)
                {
                    if (copy[y * width + x] != 0)
                    {
                        continue;
                    }

                    if (random.NextDouble() < 0.35)
                    {
                        tiles[y * width + (x + 1)] = 0;
                    }

                    if (random.NextDouble() < 0.35)
                    {
                        tiles[(y + 1) * width + x] = 0;
                    }
                }
            }
        }
    }

    private static void CarveRooms(int[] tiles, int width, int height, Random random, int roomCount)
    {
        var placed = new List<(int X, int Y, int W, int H)>();

        for (var roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            var roomW = random.Next(4, 9);
            var roomH = random.Next(4, 9);
            var rx = random.Next(2, width - roomW - 2);
            var ry = random.Next(2, height - roomH - 2);

            var overlaps = placed.Any(room =>
                rx < room.X + room.W + 2 &&
                rx + roomW + 2 > room.X &&
                ry < room.Y + room.H + 2 &&
                ry + roomH + 2 > room.Y);

            if (overlaps)
            {
                continue;
            }

            placed.Add((rx, ry, roomW, roomH));

            for (var y = ry; y < ry + roomH; y++)
            {
                for (var x = rx; x < rx + roomW; x++)
                {
                    tiles[y * width + x] = 0;
                }
            }

            var doorX = rx + roomW / 2;
            var doorY = ry + roomH / 2;
            ConnectRoomToNearestCorridor(tiles, width, height, doorX, doorY, random);
        }
    }

    private static void ConnectRoomToNearestCorridor(int[] tiles, int width, int height, int startX, int startY, Random random)
    {
        var dirs = new (int X, int Y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) }
            .OrderBy(_ => random.Next())
            .ToArray();

        foreach (var dir in dirs)
        {
            var x = startX;
            var y = startY;
            for (var radius = 0; radius < 10; radius++)
            {
                x += dir.X;
                y += dir.Y;
                if (x < 1 || y < 1 || x >= width - 1 || y >= height - 1)
                {
                    break;
                }

                tiles[y * width + x] = 0;
                if (NeighborsOpen(tiles, width, height, x, y) >= 2)
                {
                    return;
                }
            }
        }
    }

    private static int NeighborsOpen(int[] tiles, int width, int height, int x, int y)
    {
        var count = 0;
        if (x > 0 && tiles[y * width + (x - 1)] == 0) count++;
        if (x + 1 < width && tiles[y * width + (x + 1)] == 0) count++;
        if (y > 0 && tiles[(y - 1) * width + x] == 0) count++;
        if (y + 1 < height && tiles[(y + 1) * width + x] == 0) count++;
        return count;
    }

    private static (int X, int Y) FindNearestOpen(int[] tiles, int width, int height, int sx, int sy)
    {
        for (var radius = 0; radius < 12; radius++)
        {
            for (var y = Math.Max(1, sy - radius); y <= Math.Min(height - 2, sy + radius); y++)
            {
                for (var x = Math.Max(1, sx - radius); x <= Math.Min(width - 2, sx + radius); x++)
                {
                    if (tiles[y * width + x] == 0)
                    {
                        return (x, y);
                    }
                }
            }
        }

        return (1, 1);
    }

    private static void CarveMainTunnel(int[] tiles, int width, int height, int sx, int sy, int gx, int gy)
    {
        var x = sx;
        var y = sy;

        while (x != gx)
        {
            tiles[y * width + x] = 0;
            x += x < gx ? 1 : -1;
            tiles[y * width + x] = 0;
        }

        while (y != gy)
        {
            tiles[y * width + x] = 0;
            y += y < gy ? 1 : -1;
            tiles[y * width + x] = 0;
        }
    }

    private static int BaseKillTarget(DifficultyMode difficulty)
    {
        return difficulty switch
        {
            DifficultyMode.Easy => 8,
            DifficultyMode.Medium => 12,
            DifficultyMode.Hard => 16,
            _ => 22
        };
    }

    private static int[] ComputeDistances(int[] tiles, int width, int height, int startX, int startY)
    {
        var dist = Enumerable.Repeat(-1, width * height).ToArray();
        var queue = new Queue<(int X, int Y)>();
        dist[startY * width + startX] = 0;
        queue.Enqueue((startX, startY));

        var dirs = new (int X, int Y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            foreach (var dir in dirs)
            {
                var nx = current.X + dir.X;
                var ny = current.Y + dir.Y;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                {
                    continue;
                }

                var index = ny * width + nx;
                if (tiles[index] != 0 || dist[index] >= 0)
                {
                    continue;
                }

                dist[index] = dist[current.Y * width + current.X] + 1;
                queue.Enqueue((nx, ny));
            }
        }

        return dist;
    }

    private static (int X, int Y) FindFarthestOpenCell(int[] tiles, int[] dist, int width, int height)
    {
        var best = (X: 1, Y: 1);
        var bestDistance = -1;

        for (var y = 1; y < height - 1; y++)
        {
            for (var x = 1; x < width - 1; x++)
            {
                var index = y * width + x;
                if (tiles[index] == 0 && dist[index] > bestDistance)
                {
                    bestDistance = dist[index];
                    best = (x, y);
                }
            }
        }

        return best;
    }

    private static IReadOnlyList<(int X, int Y)> BuildPath(int[] tiles, int width, int height, int startX, int startY, int goalX, int goalY)
    {
        var cameFrom = new Dictionary<(int X, int Y), (int X, int Y)>();
        var queue = new Queue<(int X, int Y)>();
        var visited = new HashSet<(int X, int Y)> { (startX, startY) };
        queue.Enqueue((startX, startY));

        var dirs = new (int X, int Y)[] { (1, 0), (-1, 0), (0, 1), (0, -1) };

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current == (goalX, goalY))
            {
                break;
            }

            foreach (var dir in dirs)
            {
                var next = (X: current.X + dir.X, Y: current.Y + dir.Y);
                if (next.X < 0 || next.Y < 0 || next.X >= width || next.Y >= height)
                {
                    continue;
                }

                if (tiles[next.Y * width + next.X] != 0 || visited.Contains(next))
                {
                    continue;
                }

                visited.Add(next);
                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        var path = new List<(int X, int Y)>();
        var cursor = (X: goalX, Y: goalY);
        path.Add(cursor);
        while (cursor != (startX, startY) && cameFrom.TryGetValue(cursor, out var parent))
        {
            cursor = parent;
            path.Add(cursor);
        }

        path.Reverse();
        return path;
    }

    private static IReadOnlyList<Vec2> CreateCheckpoints(IReadOnlyList<(int X, int Y)> path)
    {
        var checkpoints = new List<Vec2>();
        if (path.Count < 10)
        {
            return checkpoints;
        }

        var first = path[path.Count / 4];
        var second = path[path.Count / 2];
        var third = path[(path.Count * 3) / 4];

        checkpoints.Add(new Vec2(first.X + 0.5f, first.Y + 0.5f));
        checkpoints.Add(new Vec2(second.X + 0.5f, second.Y + 0.5f));
        checkpoints.Add(new Vec2(third.X + 0.5f, third.Y + 0.5f));
        return checkpoints;
    }
}
