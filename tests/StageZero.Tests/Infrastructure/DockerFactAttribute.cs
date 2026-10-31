namespace StageZero.Tests.Infrastructure;

/// <summary>
/// A test that creates real containers: it runs when <c>STAGEZERO_TEST_DOCKER</c> is set
/// (a developer's machine, CI's Linux leg) and reports as skipped otherwise — never as passed.
/// </summary>
public sealed class DockerFactAttribute : FactAttribute
{
    public const string Variable = "STAGEZERO_TEST_DOCKER";

    public DockerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Needs a Docker engine; set {Variable}=true to run";
        }
    }
}
