using StageZero.Services.Database;

namespace StageZero.Tests.Fakes;

/// <summary>Records the restart a switch asks for, without stopping the test host.</summary>
public sealed class FakeAppRestart : IAppRestart
{
    public List<string> Reasons { get; } = [];

    public bool Pending => Reasons.Count > 0;

    public void Request(string reason) => Reasons.Add(reason);
}
