using Microsoft.Extensions.Configuration;
using StageZero.Api;

namespace StageZero.Tests.Api;

public class HttpsListenerTests
{
    [Theory]
    [InlineData(null, null, false)]                                          // the test host, and a container
    [InlineData("http://+:8080", null, false)]                               // the debug container
    [InlineData("https://localhost:5001;http://localhost:5000", null, true)] // dotnet run, VS Code
    [InlineData("http://localhost:5000;HTTPS://localhost:5001", null, true)]
    [InlineData("http://+:8080", "8443", true)]                              // ASPNETCORE_HTTPS_PORTS
    [InlineData("http://+:8080", " ", false)]
    public void It_knows_whether_an_https_listener_was_asked_for(string? urls, string? httpsPorts, bool expected)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["urls"] = urls, ["HTTPS_PORTS"] = httpsPorts })
            .Build();

        Assert.Equal(expected, HttpsListener.IsConfigured(configuration));
    }
}
