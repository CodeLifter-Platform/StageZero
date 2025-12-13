namespace StageZero.ReverseProxy.Services
{
    /// <summary>
    /// Interface for proxy configuration service.
    /// This will be implemented when YARP reverse proxy is fully integrated.
    /// </summary>
    public interface IProxyConfigurationService
    {
        Task ReloadConfigurationAsync();
    }
}

