using StageZero.DataAdapters.AccessServiceTokens;
using StageZero.DataAdapters.TunnelRoutes;
using StageZero.Models;
using StageZero.Services.Access;

namespace StageZero.Tests.Fakes;

/// <summary>In-memory tunnel route store, standing in for the EF-backed writer.</summary>
public class FakeTunnelRouteWriter : ITunnelRouteWriter
{
    private int _nextId = 1;

    public List<TunnelRoute> Routes { get; } = new();

    public int UpdateCount { get; private set; }

    public Task<TunnelRoute> InsertAsync(TunnelRoute route)
    {
        route.Id = _nextId++;
        Routes.Add(route);
        return Task.FromResult(route);
    }

    public Task UpdateAsync(TunnelRoute route)
    {
        UpdateCount++;

        var existing = Routes.FirstOrDefault(r => r.Id == route.Id);
        if (existing is not null)
        {
            Routes[Routes.IndexOf(existing)] = route;
        }

        return Task.CompletedTask;
    }

    public Task DeleteAsync(TunnelRoute route)
    {
        Routes.RemoveAll(r => r.Id == route.Id);
        return Task.CompletedTask;
    }
}

/// <summary>
/// In-memory service token store. The route count it reports is what decides whether
/// teardown may delete a token, so tests drive it directly.
/// </summary>
public class FakeAccessServiceTokenStore : IAccessServiceTokenReader, IAccessServiceTokenWriter
{
    public List<AccessServiceToken> Tokens { get; } = new();

    /// <summary>Routes using a token, keyed by Cloudflare token ID, excluding the one torn down.</summary>
    public Dictionary<string, int> OtherRouteCounts { get; } = new();

    public Task<List<AccessServiceToken>> GetAllAsync() => Task.FromResult(Tokens.ToList());

    public Task<AccessServiceToken?> GetByCloudflareIdAsync(string cloudflareTokenId) =>
        Task.FromResult(Tokens.FirstOrDefault(t => t.CloudflareTokenId == cloudflareTokenId));

    public Task<AccessServiceToken?> GetByNameAsync(string name) =>
        Task.FromResult(Tokens.FirstOrDefault(t => t.Name == name));

    public Task<int> CountRoutesUsingAsync(string cloudflareTokenId, int? excludingRouteId = null) =>
        Task.FromResult(OtherRouteCounts.TryGetValue(cloudflareTokenId, out var count) ? count : 0);

    public Task<AccessServiceToken> UpsertAsync(AccessServiceToken token)
    {
        var existing = Tokens.FirstOrDefault(t => t.CloudflareTokenId == token.CloudflareTokenId);

        if (existing is null)
        {
            Tokens.Add(token);
            return Task.FromResult(token);
        }

        existing.Name = token.Name;
        existing.ClientId = token.ClientId ?? existing.ClientId;
        existing.CreatedByStageZero = existing.CreatedByStageZero || token.CreatedByStageZero;

        return Task.FromResult(existing);
    }

    public Task DeleteAsync(string cloudflareTokenId)
    {
        Tokens.RemoveAll(t => t.CloudflareTokenId == cloudflareTokenId);
        return Task.CompletedTask;
    }
}

/// <summary>Records what the provisioning service handed to the secret hook.</summary>
public class RecordingAccessSecretSink : IAccessSecretSink
{
    public List<AccessSecretContext> Stored { get; } = new();

    public Task StoreAsync(AccessSecretContext context, CancellationToken cancellationToken = default)
    {
        Stored.Add(context);
        return Task.CompletedTask;
    }
}
