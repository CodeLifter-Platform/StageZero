namespace StageZero.Services;

/// <summary>
/// A failure whose message is written for the operator and is safe to show them: a rule the
/// request broke, a missing record, or Cloudflare refusing a call. The API returns the
/// message with <see cref="StatusCode"/>; anything else becomes a 500 with a reference only.
/// </summary>
public class UserFacingException : Exception
{
    public UserFacingException(string message, int statusCode = StatusCodes.Status400BadRequest)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public UserFacingException(string message, Exception inner, int statusCode = StatusCodes.Status502BadGateway)
        : base(message, inner)
    {
        StatusCode = statusCode;
    }

    public int StatusCode { get; }

    public static UserFacingException NotFound(string message) => new(message, StatusCodes.Status404NotFound);

    public static UserFacingException Conflict(string message) => new(message, StatusCodes.Status409Conflict);
}
