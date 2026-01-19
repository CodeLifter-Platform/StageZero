# YARP and Blazor Server Compatibility

## Question: Will YARP work with Blazor Server applications that run on SignalR?

**Short Answer**: **YES**, but you need to enable WebSocket support in your YARP configuration.

## How Blazor Server Works

Blazor Server applications use **SignalR** for real-time communication between the browser and server:

1. **Initial HTTP Request** - Browser loads the page via standard HTTP/HTTPS
2. **SignalR Connection** - After page load, SignalR establishes a persistent connection
3. **WebSocket Upgrade** - SignalR prefers WebSockets (falls back to Server-Sent Events or Long Polling)
4. **Persistent Connection** - All UI updates and events flow through this connection

### SignalR Connection Flow

```
Browser → HTTP GET /page → Server (loads Blazor app)
Browser → WebSocket Upgrade → Server (establishes SignalR connection)
Browser ←→ WebSocket ←→ Server (bidirectional communication)
```

## YARP and WebSocket Support

YARP (Yet Another Reverse Proxy) **fully supports WebSocket proxying**, including SignalR connections.

### Key Points

1. **WebSocket Proxying is Built-In** - YARP can proxy WebSocket connections
2. **No Special Configuration Required** - WebSockets work by default in most cases
3. **HTTP/1.1 Upgrade Protocol** - YARP handles the WebSocket upgrade handshake
4. **Bidirectional Communication** - Full duplex communication is preserved

### Your Current Implementation

Looking at your `StageZero.ReverseProxy` implementation, you have:

```csharp
public class ProxyHost
{
    // ...
    public bool WebSocketsSupport { get; set; }  // ✅ You have this flag
    // ...
}
```

And in your disabled `DatabaseProxyConfigProvider.cs`:

```csharp
if (host.WebSocketsSupport)
{
    metadata["WebSocketsSupport"] = "true";
}
```

**However**, this metadata approach is **not the standard YARP way** to enable WebSockets.

## How to Properly Configure YARP for Blazor Server

### Option 1: Default Behavior (Recommended)

YARP proxies WebSockets **by default** without any special configuration. You don't need to do anything special!

```csharp
var cluster = new ClusterConfig
{
    ClusterId = clusterId,
    Destinations = new Dictionary<string, DestinationConfig>
    {
        {
            $"destination_{host.Id}",
            new DestinationConfig
            {
                Address = $"{host.ForwardScheme}://{host.ForwardHost}:{host.ForwardPort}"
            }
        }
    }
};
```

That's it! YARP will automatically handle WebSocket upgrade requests.

### Option 2: Explicit Configuration (If Needed)

If you need to explicitly configure WebSocket behavior:

```csharp
var cluster = new ClusterConfig
{
    ClusterId = clusterId,
    HttpRequest = new ForwarderRequestConfig
    {
        AllowResponseBuffering = false,  // Important for WebSockets
        ActivityTimeout = TimeSpan.FromMinutes(100)  // Keep connection alive
    },
    Destinations = new Dictionary<string, DestinationConfig>
    {
        {
            $"destination_{host.Id}",
            new DestinationConfig
            {
                Address = $"{host.ForwardScheme}://{host.ForwardHost}:{host.ForwardPort}"
            }
        }
    }
};
```

### Option 3: Using appsettings.json

```json
{
  "ReverseProxy": {
    "Routes": {
      "blazor-app": {
        "ClusterId": "blazor-cluster",
        "Match": {
          "Hosts": ["blazorapp.example.com"]
        }
      }
    },
    "Clusters": {
      "blazor-cluster": {
        "Destinations": {
          "destination1": {
            "Address": "http://localhost:5000"
          }
        }
      }
    }
  }
}
```

## Authentication Cookies and YARP

### Important Consideration

When using YARP with Blazor Server + ASP.NET Core Identity:

1. **Authentication happens on the backend Blazor Server app** (not on YARP)
2. **Cookies are set by the backend app**
3. **YARP forwards cookies transparently**

### Cookie Flow

```
Browser → YARP → Blazor Server (login, sets cookie)
Browser ← YARP ← Blazor Server (cookie in response)
Browser → YARP → Blazor Server (subsequent requests include cookie)
```

YARP will:
- ✅ Forward the `Set-Cookie` header from backend to browser
- ✅ Forward the `Cookie` header from browser to backend
- ✅ Preserve authentication state

### Potential Issues

1. **Cookie Domain** - Make sure cookies are set for the correct domain
2. **Secure Cookies** - If YARP terminates SSL, backend needs to trust forwarded headers
3. **SameSite** - May need to configure SameSite cookie attribute

## Recommendations for Your Implementation

### 1. Remove the WebSocketsSupport Metadata

The current approach with metadata won't actually enable WebSocket support. Remove this:

```csharp
// ❌ This doesn't do anything in YARP
if (host.WebSocketsSupport)
{
    metadata["WebSocketsSupport"] = "true";
}
```

### 2. Keep the UI Flag (Optional)

You can keep the `WebSocketsSupport` boolean in your `ProxyHost` model for documentation purposes, but it's not required for functionality.

### 3. Configure Timeouts

For Blazor Server apps, configure appropriate timeouts:

```csharp
builder.Services.AddReverseProxy()
    .LoadFromConfig(configuration.GetSection("ReverseProxy"))
    .ConfigureHttpClient((context, handler) =>
    {
        handler.ActivityTimeout = TimeSpan.FromMinutes(100);  // Long-lived connections
    });
```

## Testing Your Setup

### 1. Test Without YARP

First, ensure your Blazor Server app works directly:
```
http://localhost:5000
```

### 2. Test Through YARP

Then test through YARP:
```
http://your-yarp-proxy.com
```

### 3. Check Browser DevTools

Open browser DevTools → Network tab:
- Look for a request to `_blazor`
- Status should be `101 Switching Protocols`
- Type should be `websocket`

### 4. Monitor SignalR Connection

In your Blazor app, add logging:

```csharp
builder.Services.AddServerSideBlazor()
    .AddHubOptions(options =>
    {
        options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
        options.HandshakeTimeout = TimeSpan.FromSeconds(30);
    });
```

## Summary

✅ **YARP works perfectly with Blazor Server**  
✅ **WebSocket support is built-in and enabled by default**  
✅ **No special configuration needed in most cases**  
✅ **Authentication cookies are forwarded transparently**  
⚠️ **Your current `WebSocketsSupport` metadata doesn't actually do anything**  
⚠️ **You may need to configure timeouts for long-lived connections**

## References

- [YARP Documentation](https://microsoft.github.io/reverse-proxy/)
- [YARP WebSocket Support Issue #1618](https://github.com/dotnet/yarp/issues/1618)
- [Blazor Server SignalR Configuration](https://docs.microsoft.com/en-us/aspnet/core/blazor/fundamentals/signalr)

