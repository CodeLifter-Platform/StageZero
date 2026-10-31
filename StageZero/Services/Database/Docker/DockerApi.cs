using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace StageZero.Services.Database.Docker;

/// <summary>
/// The few Docker Engine calls the Database page needs: is there an engine, what is this
/// container, pull an image, create and start a container, remove one that failed.
/// </summary>
public interface IDockerApi
{
    /// <summary>The engine behind the socket, or null when there is no socket or it doesn't answer.</summary>
    Task<DockerEngine?> PingAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Why <see cref="PingAsync"/> answered null, for the operator: no socket, a socket this
    /// process may not open (and the group that may), or an engine that doesn't answer.
    /// </summary>
    Task<string> ExplainUnavailableAsync(bool inContainer, CancellationToken cancellationToken = default);

    /// <summary>A container by name or id, or null when there is none.</summary>
    Task<ContainerInspection?> InspectContainerAsync(string nameOrId, CancellationToken cancellationToken = default);

    Task PullImageAsync(string image, CancellationToken cancellationToken = default);

    /// <summary>Creates the container and returns its id.</summary>
    Task<string> CreateContainerAsync(string name, ContainerSpec spec, CancellationToken cancellationToken = default);

    Task StartContainerAsync(string id, CancellationToken cancellationToken = default);

    Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default);
}

public sealed record DockerEngine(string Version, string Socket);

public sealed record ContainerMount(string Destination, string Source, string Type);

public sealed record ContainerInspection(
    string Id,
    string Name,
    string Status,
    bool Running,
    IReadOnlyList<ContainerMount> Mounts,
    IReadOnlyList<string> Networks,
    /// <summary>Published host ports by container port, e.g. "5432/tcp" → 55432.</summary>
    IReadOnlyDictionary<string, int> PublishedPorts,
    /// <summary>The container's environment, as anyone with the socket can read it.</summary>
    IReadOnlyList<string> Env);

/// <summary>What to create, in the engine's own terms.</summary>
public sealed record ContainerSpec(
    string Image,
    IReadOnlyList<string> Env,
    IReadOnlyDictionary<string, string> Labels,
    /// <summary>host path → container path.</summary>
    IReadOnlyList<(string HostPath, string ContainerPath)> Binds,
    /// <summary>Container port → (host address, host port).</summary>
    IReadOnlyList<(int ContainerPort, string HostAddress, int HostPort)> Ports,
    string? Network,
    string RestartPolicy)
{
    public JsonObject ToJson()
    {
        var exposed = new JsonObject();
        var bindings = new JsonObject();
        foreach (var (containerPort, hostAddress, hostPort) in Ports)
        {
            exposed[$"{containerPort}/tcp"] = new JsonObject();
            bindings[$"{containerPort}/tcp"] = new JsonArray(new JsonObject
            {
                ["HostIp"] = hostAddress,
                ["HostPort"] = hostPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
        }

        var labels = new JsonObject();
        foreach (var (key, value) in Labels)
        {
            labels[key] = value;
        }

        var hostConfig = new JsonObject
        {
            ["Binds"] = new JsonArray(Binds.Select(b => (JsonNode)JsonValue.Create($"{b.HostPath}:{b.ContainerPath}")).ToArray()),
            ["PortBindings"] = bindings,
            ["RestartPolicy"] = new JsonObject { ["Name"] = RestartPolicy }
        };
        if (Network is not null)
        {
            hostConfig["NetworkMode"] = Network;
        }

        return new JsonObject
        {
            ["Image"] = Image,
            ["Env"] = new JsonArray(Env.Select(e => (JsonNode)JsonValue.Create(e)).ToArray()),
            ["Labels"] = labels,
            ["ExposedPorts"] = exposed,
            ["HostConfig"] = hostConfig
        };
    }
}

/// <summary>Docker said no: the status and its message, written for whoever asked.</summary>
public sealed class DockerException(int status, string message) : Exception(message)
{
    public int Status { get; } = status;
}

/// <summary>The engine API over its Unix socket. Nothing else: no CLI in the image, no package.</summary>
public sealed class DockerApi : IDockerApi, IDisposable
{
    private readonly string? _socket;
    private readonly HttpClient? _http;

    public DockerApi(string? socket)
    {
        _socket = socket;
        if (socket is null)
        {
            return;
        }

        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (_, cancellationToken) =>
            {
                var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await connection.ConnectAsync(new UnixDomainSocketEndPoint(socket), cancellationToken);
                    return new NetworkStream(connection, ownsSocket: true);
                }
                catch
                {
                    connection.Dispose();
                    throw;
                }
            }
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri("http://docker/"), Timeout = TimeSpan.FromMinutes(10) };
    }

    public async Task<DockerEngine?> PingAsync(CancellationToken cancellationToken = default)
    {
        if (_http is null)
        {
            return null;
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            using var response = await _http.GetAsync("version", timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(timeout.Token));
            return new DockerEngine(document.RootElement.GetProperty("Version").GetString() ?? "?", _socket!);
        }
        catch (Exception ex) when (ex is HttpRequestException or SocketException or IOException or OperationCanceledException or JsonException)
        {
            return null;
        }
    }

    public async Task<string> ExplainUnavailableAsync(bool inContainer, CancellationToken cancellationToken = default)
    {
        if (_socket is null || !File.Exists(_socket))
        {
            return inContainer
                ? "StageZero can't see the Docker engine. Start the stack with docker-compose.provision.yml as well, which mounts the Docker socket into the container (and grants it the host: read the file's header first)."
                : $"No Docker socket found ({DockerEndpoint.DefaultSocket} or a desktop engine's). Is Docker running?";
        }

        if (!await CanOpenSocketAsync(cancellationToken))
        {
            var group = await SocketGroupAsync(cancellationToken);
            return PermissionAdvice(_socket, group, inContainer);
        }

        return $"The Docker socket {_socket} is there but the engine did not answer. Is Docker running?";
    }

    /// <summary>The words for a socket this process may not open.</summary>
    public static string PermissionAdvice(string socket, int? group, bool inContainer)
    {
        var who = inContainer ? "StageZero's container user" : "this process";
        var fix = group is { } gid
            ? inContainer
                ? $"It belongs to group {gid}: put DOCKER_GID={gid} in .env and run the compose command again."
                : $"It belongs to group {gid}: add your user to that group, or run StageZero as a user in it."
            : inContainer
                ? "Set DOCKER_GID in .env to the socket's group id (as seen inside the container) and run the compose command again."
                : "Add your user to the group that owns it.";
        return $"The Docker socket {socket} is mounted, but {who} may not use it. {fix}";
    }

    private async Task<bool> CanOpenSocketAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var connection = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            await connection.ConnectAsync(new UnixDomainSocketEndPoint(_socket!), cancellationToken);
            return true;
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AccessDenied)
        {
            return false;
        }
        catch (SocketException)
        {
            return true; // reachable in principle; something else is wrong
        }
    }

    /// <summary>The socket's owning group, via coreutils (the runtime image has it); null when that fails.</summary>
    private async Task<int?> SocketGroupAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var stat = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("stat", ["-c", "%g", _socket!])
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false
            });
            if (stat is null)
            {
                return null;
            }

            var output = await stat.StandardOutput.ReadToEndAsync(cancellationToken);
            await stat.WaitForExitAsync(cancellationToken);
            return stat.ExitCode == 0 && int.TryParse(output.Trim(), out var gid) ? gid : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null;
        }
    }

    public async Task<ContainerInspection?> InspectContainerAsync(string nameOrId, CancellationToken cancellationToken = default)
    {
        using var response = await Http.GetAsync($"containers/{Uri.EscapeDataString(nameOrId)}/json", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return Parse(document.RootElement);
    }

    public async Task PullImageAsync(string image, CancellationToken cancellationToken = default)
    {
        var colon = image.LastIndexOf(':');
        var (name, tag) = colon > 0 && !image[colon..].Contains('/') ? (image[..colon], image[(colon + 1)..]) : (image, "latest");
        using var response = await Http.PostAsync(
            $"images/create?fromImage={Uri.EscapeDataString(name)}&tag={Uri.EscapeDataString(tag)}", content: null, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        // The body is a progress stream; it ends when the pull does. An error mid-way is a
        // line with "error" in it.
        using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (line.Contains("\"error\"", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(line);
                throw new DockerException(500, document.RootElement.TryGetProperty("error", out var error) ? error.GetString() ?? line : line);
            }
        }
    }

    public async Task<string> CreateContainerAsync(string name, ContainerSpec spec, CancellationToken cancellationToken = default)
    {
        using var content = new StringContent(spec.ToJson().ToJsonString(), Encoding.UTF8, "application/json");
        using var response = await Http.PostAsync($"containers/create?name={Uri.EscapeDataString(name)}", content, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.GetProperty("Id").GetString()!;
    }

    public async Task StartContainerAsync(string id, CancellationToken cancellationToken = default)
    {
        using var response = await Http.PostAsync($"containers/{Uri.EscapeDataString(id)}/start", content: null, cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotModified)
        {
            return; // already running
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public async Task RemoveContainerAsync(string id, bool force, CancellationToken cancellationToken = default)
    {
        using var response = await Http.DeleteAsync($"containers/{Uri.EscapeDataString(id)}?force={(force ? "true" : "false")}", cancellationToken);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return;
        }

        await EnsureSuccessAsync(response, cancellationToken);
    }

    public void Dispose() => _http?.Dispose();

    private HttpClient Http => _http ?? throw new InvalidOperationException("No Docker socket.");

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = body;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("message", out var m))
            {
                message = m.GetString() ?? body;
            }
        }
        catch (JsonException)
        {
            // Not JSON; the body is the message.
        }

        throw new DockerException((int)response.StatusCode, message.Trim());
    }

    public static ContainerInspection Parse(JsonElement root)
    {
        var state = root.GetProperty("State");
        var mounts = new List<ContainerMount>();
        if (root.TryGetProperty("Mounts", out var mountsElement))
        {
            foreach (var mount in mountsElement.EnumerateArray())
            {
                mounts.Add(new ContainerMount(
                    mount.GetProperty("Destination").GetString() ?? "",
                    mount.GetProperty("Source").GetString() ?? "",
                    mount.TryGetProperty("Type", out var type) ? type.GetString() ?? "" : ""));
            }
        }

        var networks = new List<string>();
        var ports = new Dictionary<string, int>();
        if (root.TryGetProperty("NetworkSettings", out var settings))
        {
            if (settings.TryGetProperty("Networks", out var networksElement) && networksElement.ValueKind == JsonValueKind.Object)
            {
                networks.AddRange(networksElement.EnumerateObject().Select(n => n.Name));
            }

            if (settings.TryGetProperty("Ports", out var portsElement) && portsElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var port in portsElement.EnumerateObject())
                {
                    if (port.Value.ValueKind == JsonValueKind.Array
                        && port.Value.EnumerateArray().FirstOrDefault() is { ValueKind: JsonValueKind.Object } binding
                        && int.TryParse(binding.GetProperty("HostPort").GetString(), out var hostPort))
                    {
                        ports[port.Name] = hostPort;
                    }
                }
            }
        }

        var env = new List<string>();
        if (root.TryGetProperty("Config", out var config) && config.TryGetProperty("Env", out var envElement) && envElement.ValueKind == JsonValueKind.Array)
        {
            env.AddRange(envElement.EnumerateArray().Select(e => e.GetString() ?? ""));
        }

        return new ContainerInspection(
            root.GetProperty("Id").GetString() ?? "",
            (root.GetProperty("Name").GetString() ?? "").TrimStart('/'),
            state.GetProperty("Status").GetString() ?? "",
            state.TryGetProperty("Running", out var running) && running.GetBoolean(),
            mounts,
            networks,
            ports,
            env);
    }
}
