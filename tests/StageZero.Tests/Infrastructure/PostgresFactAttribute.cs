namespace StageZero.Tests.Infrastructure;

/// <summary>
/// A test that needs a PostgreSQL server: it runs when <c>STAGEZERO_TEST_POSTGRES</c> names
/// one (CI's PostgreSQL leg) and reports as skipped otherwise — never as passed.
/// </summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(StageZeroApp.PostgresServerVariable)))
        {
            Skip = $"Needs a PostgreSQL server in {StageZeroApp.PostgresServerVariable}";
        }
    }
}
