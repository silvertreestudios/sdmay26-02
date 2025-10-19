using BenchmarkDotNet.Attributes;
using System.Collections.Generic;

[MemoryDiagnoser]
[CategoriesColumn]
public class DijkstraBenchmark
{
    private TestGrid grid;
    private TestPathfinder pathfinder;

    [GlobalSetup]
    public void Setup()
    {
        grid = new TestGrid(50, 50);
        pathfinder = new TestPathfinder(grid);
    }

    [Benchmark(Description = "Small Path (5,5)")]
    public void SmallPath()
    {
        var result = pathfinder.FindPath(new Position(0, 0), new Position(5, 5));
    }

    [Benchmark(Description = "Medium Path (25,25)")]
    public void MediumPath()
    {
        var result = pathfinder.FindPath(new Position(0, 0), new Position(25, 25));
    }

    [Benchmark(Description = "Long Path (49,49)")]
    public void LongPath()
    {
        var result = pathfinder.FindPath(new Position(0, 0), new Position(49, 49));
    }
}

public readonly struct Position
{
    public readonly int X, Y;
    public Position(int x, int y) { X = x; Y = y; }

    public static Position operator +(Position a, Position b) => new Position(a.X + b.X, a.Y + b.Y);
}

public class TestGrid
{
    public int Width { get; }
    public int Height { get; }

    public TestGrid(int width, int height)
    {
        Width = width;
        Height = height;
    }

    public bool IsInBounds(Position pos) =>
        (uint)pos.X < (uint)Width && (uint)pos.Y < (uint)Height;
}

public class TestPathfinder
{
    private readonly TestGrid grid;
    private static readonly Position[] Directions = new[]
    {
        new Position(1, 0), new Position(-1, 0),
        new Position(0, 1), new Position(0, -1),
        new Position(1, 1), new Position(1, -1),
        new Position(-1, 1), new Position(-1, -1)
    };

    private static readonly float[] Costs = new[]
    {
        1f, 1f, 1f, 1f,
        1.4142135f, 1.4142135f, 1.4142135f, 1.4142135f
    };

    public TestPathfinder(TestGrid grid)
    {
        this.grid = grid;
    }

    public (bool found, float distance, List<Position> path) FindPath(Position start, Position target)
    {
        int w = grid.Width, h = grid.Height, total = w * h;
        int ToIndex(Position p) => p.X + p.Y * w;

        var dist = new float[total];
        for (int i = 0; i < total; i++) dist[i] = float.PositiveInfinity;

        var prev = new Position?[total];
        var heap = new MinHeap();

        dist[ToIndex(start)] = 0f;
        heap.Push(new Node { pos = start, dist = 0f });

        while (heap.Count > 0)
        {
            var node = heap.Pop();
            var u = node.pos;
            int uIdx = ToIndex(u);

            if (node.dist != dist[uIdx]) continue;
            if (u.X == target.X && u.Y == target.Y)
            {
                var path = new List<Position>();
                var cur = target;
                while (true)
                {
                    path.Add(cur);
                    if (cur.X == start.X && cur.Y == start.Y) break;
                    var p = prev[ToIndex(cur)];
                    if (!p.HasValue) break;
                    cur = p.Value;
                }
                path.Reverse();
                return (true, dist[uIdx], path);
            }

            for (int i = 0; i < Directions.Length; i++)
            {
                var v = u + Directions[i];
                if (!grid.IsInBounds(v)) continue;

                int vIdx = ToIndex(v);
                float alt = dist[uIdx] + Costs[i];
                if (alt < dist[vIdx])
                {
                    dist[vIdx] = alt;
                    prev[vIdx] = u;
                    heap.Push(new Node { pos = v, dist = alt });
                }
            }
        }
        return (false, -1f, null);
    }

    struct Node
    {
        public Position pos;
        public float dist;
    }

    class MinHeap
    {
        readonly List<Node> items = new();
        public int Count => items.Count;

        public void Push(Node n)
        {
            items.Add(n);
            SiftUp(items.Count - 1);
        }

        public Node Pop()
        {
            var result = items[0];
            int lastIdx = items.Count - 1;
            items[0] = items[lastIdx];
            items.RemoveAt(lastIdx);
            if (items.Count > 0) SiftDown(0);
            return result;
        }

        void SiftUp(int i)
        {
            while (i > 0)
            {
                int parent = (i - 1) >> 1;
                if (items[i].dist >= items[parent].dist) break;
                (items[i], items[parent]) = (items[parent], items[i]);
                i = parent;
            }
        }

        void SiftDown(int i)
        {
            for (;;)
            {
                int left = (i << 1) + 1;
                int right = left + 1;
                int smallest = i;

                if (left < items.Count && items[left].dist < items[smallest].dist)
                    smallest = left;
                if (right < items.Count && items[right].dist < items[smallest].dist)
                    smallest = right;

                if (smallest == i) break;
                (items[i], items[smallest]) = (items[smallest], items[i]);
                i = smallest;
            }
        }
    }
}