using StageZero.Services.Database;
using StageZero.Services.Database.Docker;

namespace StageZero.Tests.Fakes;

/// <summary>An engine in memory: records what was asked of it and can be told to refuse.</summary>
public sealed class FakeDocker : IDockerApi
{
    public DockerEngine? Engine { get; set; } = new("28.0.0", "/fake/docker.sock");
    public Dictionary<string, ContainerInspection> Containers { get; } = new(StringComparer.Ordinal);
    public List<string> Pulled { get; } = [];
    public List<(string Name, ContainerSpec Spec)> Created { get; } = [];
    public List<string> Started { get; } = [];
    public List<string> Removed { get; } = [];
    public DockerException? CreateRefusal { get; set; }

    public Task<DockerEngine?> PingAsync(CancellationToken cancellationToken = default) => Task.FromResult(Engine);

    public Task<ContainerInspection?> InspectContainerAsync(string nameOrId, CancellationToken cancellationToken = default) =>
        Task.FromResult(Containers.TryGetValue(nameOrId, out var c) ? c : Containers.Values.FirstOrDefault(c => c.Id == nameOrId));

    public Task PullImageAsync(string image, CancellationToken cancellationToken = default)
    {
        Pulled.Add(image);
        return Task.CompletedTask;
    }

    public Task<string> CreateContainerAsync(string name, ContainerSpec spec, CancellationToken cancellationToken = default)
    {
        if (CreateRefusal is not null)
        {
            throw CreateRefusal;
        }

        Created.Add((name, spec));
        var id = $"id-{name}";
        Containers[name] = new ContainerInspection(id, name, "created", false, [], [], new Dictionary<string, int>(), []);
        return Task.FromResult(id);
    }

    public Task StartContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        Started.Add(id);
        return Task.CompletedTask;
    }

    public Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default)
    {
        Removed.Add(id);
        foreach (var name in Containers.Where(c => c.Value.Id == id).Select(c => c.Key).ToList())
        {
            Containers.Remove(name);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Records the setup statements instead of running them.</summary>
public sealed class FakeAdmin : IPostgresAdmin
{
    public List<(string ConnectionString, string Sql)> Statements { get; } = [];
    public bool NeverReady { get; set; }

    public Task WaitUntilReadyAsync(string connectionString, TimeSpan timeout, CancellationToken cancellationToken = default) =>
        NeverReady ? throw new TimeoutException("PostgreSQL did not accept a connection within 90 seconds.") : Task.CompletedTask;

    public Task ExecuteAsync(string connectionString, string sql, CancellationToken cancellationToken = default)
    {
        Statements.Add((connectionString, sql));
        return Task.CompletedTask;
    }
}

/// <summary>Knows which host:port pairs have a PostgreSQL behind them.</summary>
public sealed class FakeProber : IPostgresProber
{
    public HashSet<(string Host, int Port)> Listening { get; } = [];
    public List<(string Host, int Port)> Probed { get; } = [];

    public Task<bool> IsPostgresAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        lock (Probed)
        {
            Probed.Add((host, port));
        }

        return Task.FromResult(Listening.Contains((host, port)));
    }
}
