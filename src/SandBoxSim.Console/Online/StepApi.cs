using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using SandBoxSim.Core;
using SandBoxSim.Core.Environment;
using SandBoxSim.Core.Foundation;

namespace SandBoxSim.ConsoleApp.Online;

public sealed class ApiError : Exception
{
    public int Status { get; }
    public ApiError(int status, string message) : base(message) { Status = status; }
}

/// <summary>Caller-driven sessions. No background clock; the HTTP adapter serializes access.</summary>
public sealed class StepApi
{
    private sealed class Session
    {
        public Simulation Sim;
        public long Revision;
        public long Calls, Steps, Actions;
        public bool Faulted;
        public readonly Dictionary<string, (string input, object output)> Replies = new(StringComparer.Ordinal);
        public Session(Simulation sim) { Sim = sim; }
    }
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    public const int MaxSessions = 8;
    public const int MaxTicks = 1000;

    public object Dispatch(string method, string path, JsonElement body)
    {
        var parts = path.Trim('/').Split('/');
        if (method == "POST" && path == "/v1/sessions")
        {
            if (_sessions.Count >= MaxSessions) { throw new ApiError(429, "Session limit reached; delete an old session"); }
            var sim = Create(body);
            string id = Guid.NewGuid().ToString("N");
            var session = new Session(sim); _sessions.Add(id, session);
            return new { schemaVersion = 1, sessionId = id, observation = Observe(session) };
        }
        if (parts.Length < 3 || parts[0] != "v1" || parts[1] != "sessions" || !_sessions.TryGetValue(parts[2], out var s))
            throw new ApiError(404, "Unknown endpoint or session");
        if (parts.Length == 3 && method == "DELETE") { _sessions.Remove(parts[2]); return new { deleted = true }; }
        if (parts.Length == 3 && method == "GET") { return Observe(s); }
        if (parts.Length == 4 && parts[3] == "map" && method == "GET")
        {
            var w = s.Sim.World;
            return new { revision = s.Revision, width = w.Width, height = w.Height,
                tiles = w.Tiles.Select(t => new { walkable = t.Walkable, terrain = t.Terrain.ToString(), traversalHeight = t.Temperature,
                    moveCost = TerrainInfo.MoveCost(t.Terrain) }).ToArray() };
        }
        if (parts.Length != 4 || method != "POST") { throw new ApiError(404, "Unknown endpoint"); }
        if (s.Faulted && parts[3] != "reset") { throw new ApiError(409, "Session faulted; reset required"); }
        if (parts[3] == "path") { return Paths(s, body); }
        if (parts[3] != "step" && parts[3] != "reset") { throw new ApiError(404, "Unknown endpoint"); }
        string idempotency = Text(body, "requestId", 1, 64);
        string input = body.GetRawText();
        if (s.Replies.TryGetValue(idempotency, out var reply))
        {
            if (input != reply.input) { throw new ApiError(409, "requestId reused with different input"); }
            return reply.output;
        }
        Revision(s, body);
        object result;
        if (parts[3] == "reset")
        {
            Fields(body, "requestId", "expectedRevision", "seed", "width", "height", "agents");
            var sim = Create(body, false); s.Sim = sim; s.Revision++; s.Steps = s.Actions = s.Calls = 0; s.Faulted = false;
            result = Observe(s);
        }
        else
        {
            Fields(body, "requestId", "expectedRevision", "ticks", "actions");
            int ticks = Int(body, "ticks", 0, MaxTicks);
            if (s.Steps + ticks > 1000000) { throw new ApiError(429, "Episode tick budget exhausted; reset required"); }
            var operations = new List<Action>();
            if (body.TryGetProperty("actions", out var actions))
            {
                if (actions.ValueKind != JsonValueKind.Array || actions.GetArrayLength() > 64) { throw new ApiError(400, "actions must be an array of at most 64 items"); }
                foreach (var action in actions.EnumerateArray()) { operations.Add(ValidateAction(s.Sim, action)); }
            }
            // All arguments are validated before any mutation. Unexpected engine errors quarantine the session.
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            var watch = Stopwatch.StartNew();
            try
            {
                foreach (var operation in operations) { operation(); }
                s.Sim.Tick(ticks);
                s.Sim.ValidateInvariants();
                if (!s.Sim.LastInvariantCheckPassed) { throw new InvalidOperationException("Simulation invariant failed"); }
            }
            catch { s.Faulted = true; throw; }
            watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            s.Revision++; s.Calls++; s.Steps += ticks; s.Actions += operations.Count;
            result = new { schemaVersion = 1, observation = Observe(s), metrics = new { executionMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocated, ticks, actions = operations.Count } };
        }
        if (s.Replies.Count >= 64) { s.Replies.Remove(s.Replies.Keys.First()); }
        s.Replies.Add(idempotency, (input, result));
        return result;
    }

    private static Simulation Create(JsonElement body, bool checkFields = true)
    {
        if (checkFields) { Fields(body, "seed", "width", "height", "agents"); }
        int seed = Int(body, "seed", int.MinValue, int.MaxValue);
        int width = OptionalInt(body, "width", 16, 128, 48), height = OptionalInt(body, "height", 16, 128, 48);
        int agents = OptionalInt(body, "agents", 0, 200, 24);
        var config = new SimConfig(); config.World.Width = width; config.World.Height = height; config.WorldGen.Seed = seed;
        config.Debug.AssertInvariants = true;
        var sim = new Simulation(config, width, height, seed);
        sim.InterveneSpawnHumans(width / 2, height / 2, agents, Math.Min(width, height) / 3);
        return sim;
    }

    private static object Observe(Session s)
    {
        var sample = s.Sim.Observe();
        return new { schemaVersion = 1, revision = s.Revision, tick = s.Sim.Clock, digest = s.Sim.StateDigestString(), faulted = s.Faulted,
            population = sample.Population, food = sample.Food, wood = sample.Wood, stone = sample.Stone, buildings = sample.BuildingCount,
            episode = new { stepCalls = s.Calls, ticks = s.Steps, actions = s.Actions } };
    }

    private static void Revision(Session s, JsonElement body)
    {
        if (!body.TryGetProperty("expectedRevision", out var r) || r.ValueKind != JsonValueKind.Number || !r.TryGetInt64(out long revision) || revision != s.Revision)
            throw new ApiError(409, "expectedRevision must match the current observation");
    }

    private static Action ValidateAction(Simulation sim, JsonElement action)
    {
        string kind = Text(action, "kind", 1, 32);
        int x = Int(action, "x", 0, sim.World.Width - 1), y = Int(action, "y", 0, sim.World.Height - 1);
        if (kind == "terrain")
        {
            Fields(action, "kind", "x", "y", "terrain");
            string name = Text(action, "terrain", 1, 32);
            if (!Enum.TryParse<TerrainKind>(name, false, out var terrain) || !Enum.IsDefined(terrain) || terrain.ToString() != name)
                throw new ApiError(400, "Unknown terrain");
            return () => sim.InterveneSetTerrain(x, y, terrain);
        }
        if (kind == "resource")
        {
            Fields(action, "kind", "x", "y", "resource", "amount");
            string name = Text(action, "resource", 1, 32);
            if (!Enum.TryParse<ResourceKind>(name, false, out var resource) || resource == ResourceKind.None || !Enum.IsDefined(resource) || resource.ToString() != name)
                throw new ApiError(400, "Unknown resource");
            float amount = (float)Number(action, "amount", 0, 10000);
            return () => sim.InterveneAddResource(x, y, resource, amount);
        }
        if (kind == "fertility")
        {
            Fields(action, "kind", "x", "y", "radius", "delta");
            int radius = Int(action, "radius", 0, 8); float delta = (float)Number(action, "delta", -1, 1);
            return () => sim.InterveneSetFertility(x, y, radius, delta);
        }
        throw new ApiError(400, "Unknown action kind");
    }

    private static object Paths(Session s, JsonElement body)
    {
        Fields(body, "expectedRevision", "queries"); Revision(s, body);
        if (!body.TryGetProperty("queries", out var queries) || queries.ValueKind != JsonValueKind.Array || queries.GetArrayLength() is < 1 or > 128)
            throw new ApiError(400, "queries must contain 1-128 coordinates");
        var coords = new List<(int x, int y, int gx, int gy)>();
        foreach (var q in queries.EnumerateArray())
        {
            Fields(q, "x", "y", "goalX", "goalY");
            coords.Add((Int(q, "x", 0, s.Sim.World.Width - 1), Int(q, "y", 0, s.Sim.World.Height - 1),
                Int(q, "goalX", 0, s.Sim.World.Width - 1), Int(q, "goalY", 0, s.Sim.World.Height - 1)));
        }
        string before = s.Sim.StateDigestString(); var paths = new List<object>();
        var buffer = new Int2[s.Sim.World.Width * s.Sim.World.Height];
        long allocated = GC.GetAllocatedBytesForCurrentThread(); var watch = Stopwatch.StartNew();
        foreach (var q in coords)
        {
            var p = s.Sim.Pathfinder.FindPath(q.x, q.y, q.gx, q.gy, buffer);
            paths.Add(new { success = p.Success, cost = p.Cost, expandedNodes = p.ExpandedNodes,
                points = p.Success ? buffer.Take(p.Length).Select(v => new[] { v.X, v.Y }).ToArray() : Array.Empty<int[]>() });
        }
        watch.Stop(); allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        string after = s.Sim.StateDigestString();
        if (before != after) { s.Faulted = true; throw new InvalidOperationException("Path query mutated simulation"); }
        return new { schemaVersion = 1, revision = s.Revision, digest = after, paths,
            metrics = new { executionMs = watch.Elapsed.TotalMilliseconds, allocatedBytes = allocated, queries = coords.Count } };
    }

    public static void Fields(JsonElement body, params string[] allowed)
    {
        if (body.ValueKind != JsonValueKind.Object) { throw new ApiError(400, "JSON object required"); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
            if (!allowed.Contains(property.Name) || !names.Add(property.Name)) { throw new ApiError(400, "Unknown or duplicate field: " + property.Name); }
    }
    private static string Text(JsonElement body, string name, int min, int max)
    {
        if (body.ValueKind != JsonValueKind.Object || !body.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.String || p.GetString() is not string value || value.Length < min || value.Length > max)
            throw new ApiError(400, "Invalid string: " + name);
        return value;
    }
    private static int Int(JsonElement body, string name, int min, int max)
    {
        if (!body.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetInt32(out int value) || value < min || value > max)
            throw new ApiError(400, "Invalid integer: " + name);
        return value;
    }
    private static int OptionalInt(JsonElement body, string name, int min, int max, int fallback) => body.TryGetProperty(name, out _) ? Int(body, name, min, max) : fallback;
    private static double Number(JsonElement body, string name, double min, double max)
    {
        if (!body.TryGetProperty(name, out var p) || p.ValueKind != JsonValueKind.Number || !p.TryGetDouble(out double value) || !double.IsFinite(value) || value < min || value > max)
            throw new ApiError(400, "Invalid number: " + name);
        return value;
    }
}
