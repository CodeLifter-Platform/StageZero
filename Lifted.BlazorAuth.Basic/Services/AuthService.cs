using System.Security.Cryptography;
using System.Text;
using Lifted.BlazorAuth.Basic.DataAdapters;
using Lifted.BlazorAuth.Basic.Models;
using Microsoft.Extensions.Logging;

namespace Lifted.BlazorAuth.Basic.Services;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string email, string password);
    Task LogoutAsync();
    Task<User?> GetCurrentUserAsync();
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<bool> SendEmailVerificationCodeAsync(string email);
    Task<bool> VerifyEmailCodeAsync(string code);
    Task<bool> UpdateEmailAsync(string email);
    Task<bool> SendPasswordResetCodeAsync(string email);
    Task<bool> VerifyPasswordResetCodeAsync(string email, string code);
    Task<bool> ResetPasswordAsync(string email, string code, string newPassword);
    bool IsAuthenticated { get; }
    bool RequiresPasswordChange { get; }
    bool RequiresEmailVerification { get; }
}

// ═══════════════════════════════════════════════════════════════
// CUSTOM EXCEPTION
// ═══════════════════════════════════════════════════════════════

public class AuthServiceException : Exception
{
    public AuthServiceException(string message) : base(message) { }
    public AuthServiceException(string message, Exception inner) : base(message, inner) { }
}

// ═══════════════════════════════════════════════════════════════
// IMPLEMENTATION
// ═══════════════════════════════════════════════════════════════

public class AuthService : IAuthService
{
    /// <summary>Wrong passwords in a row before the account locks.</summary>
    public const int MaxFailedLogins = 5;

    /// <summary>Wrong guesses at one code before it is void.</summary>
    public const int MaxCodeAttempts = 5;

    public static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(15);

    // Compared against when the email is unknown, so a miss costs the same BCrypt time as a
    // wrong password and response timing doesn't reveal which accounts exist.
    private static readonly Lazy<string> UnknownUserHash = new(() => BCrypt.Net.BCrypt.HashPassword(Guid.NewGuid().ToString()));

    private readonly ILogger<AuthService> _logger;
    private readonly IUserReader _userReader;
    private readonly IUserWriter _userWriter;
    private readonly IEmailService _emailService;
    private readonly IAuthThrottle _throttle;
    private readonly ClientAddress _clientAddress;
    private readonly TimeProvider _time;
    private User? _currentUser;

    public AuthService(
        ILogger<AuthService> logger,
        IUserReader userReader,
        IUserWriter userWriter,
        IEmailService emailService,
        IAuthThrottle throttle,
        ClientAddress clientAddress,
        TimeProvider time)
    {
        _logger = logger;
        _userReader = userReader;
        _userWriter = userWriter;
        _emailService = emailService;
        _throttle = throttle;
        _clientAddress = clientAddress;
        _time = time;
    }

    private DateTime UtcNow => _time.GetUtcNow().UtcDateTime;

    /// <summary>A six-digit code from a cryptographic source.</summary>
    public static string NewCode() =>
        RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

    private static bool CodesMatch(string? expected, string actual) =>
        expected is not null
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(actual));

    public bool IsAuthenticated => _currentUser != null;

    public bool RequiresPasswordChange => _currentUser?.RequiresPasswordChange ?? false;

    public bool RequiresEmailVerification => _currentUser != null && !_currentUser.EmailVerified;

    public async Task<AuthResult> LoginAsync(string email, string password)
    {
        var address = _clientAddress.Value;
        try
        {
            _logger.LogDebug("Login attempt for user {Email} from {ClientAddress}", email, address);

            if (_throttle.RetryAfter(AuthThrottleScope.Login, address) is { } wait)
            {
                _logger.LogWarning("Login refused: {ClientAddress} is throttled", address);
                return AuthResult.Failed(new AuthThrottledException(wait).Message);
            }

            // Find user by email
            var user = await _userReader.GetByEmailAsync(email);

            if (user == null)
            {
                BCrypt.Net.BCrypt.Verify(password, UnknownUserHash.Value);
                _throttle.RecordFailure(AuthThrottleScope.Login, address);
                _logger.LogWarning("Login failed: user {Email} not found ({ClientAddress})", email, address);
                return AuthResult.Failed("Invalid email or password");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: user {Email} is inactive", email);
                return AuthResult.Failed("Account is inactive");
            }

            if (user.LockoutEndsAt > UtcNow)
            {
                _logger.LogWarning("Login refused: user {Email} is locked out ({ClientAddress})", email, address);
                var minutes = Math.Max(1, (int)Math.Ceiling((user.LockoutEndsAt.Value - UtcNow).TotalMinutes));
                return AuthResult.Failed($"Too many failed sign-ins. This account is locked for {minutes} minute(s).");
            }

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                _throttle.RecordFailure(AuthThrottleScope.Login, address);
                user.FailedLoginCount++;
                if (user.FailedLoginCount >= MaxFailedLogins)
                {
                    user.LockoutEndsAt = UtcNow + LockoutDuration;
                    user.FailedLoginCount = 0;
                    _logger.LogWarning("User {Email} locked out after {Count} failed sign-ins", email, MaxFailedLogins);
                }

                await _userWriter.UpdateAsync(user);
                _logger.LogWarning("Login failed: invalid password for user {Email} ({ClientAddress})", email, address);
                return AuthResult.Failed("Invalid email or password");
            }

            // A success clears the failure count and any expired lockout
            user.FailedLoginCount = 0;
            user.LockoutEndsAt = null;
            user.LastLoginAt = UtcNow;
            await _userWriter.UpdateAsync(user);

            _currentUser = user;
            _logger.LogInformation("User {Email} logged in successfully", email);
            return AuthResult.Succeeded(user);
        }
        catch (AuthServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for user {Email}", email);
            throw new AuthServiceException("Could not complete login", ex);
        }
    }

    public Task LogoutAsync()
    {
        if (_currentUser != null)
        {
            _logger.LogInformation("User {Email} logged out", _currentUser.Email);
            _currentUser = null;
        }
        return Task.CompletedTask;
    }

    public Task<User?> GetCurrentUserAsync()
    {
        return Task.FromResult(_currentUser);
    }

    public async Task<bool> ChangePasswordAsync(string currentPassword, string newPassword)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot change password: no user is logged in");
                return false;
            }

            // Verify current password
            if (!BCrypt.Net.BCrypt.Verify(currentPassword, _currentUser.PasswordHash))
            {
                _logger.LogWarning("Password change failed: invalid current password for user {Email}", _currentUser.Email);
                return false;
            }

            // Update password
            _currentUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _currentUser.RequiresPasswordChange = false;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Password changed successfully for user {Email}", _currentUser.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change password for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not change password", ex);
        }
    }

    public async Task<bool> SendEmailVerificationCodeAsync(string email)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot send verification code: no user is logged in");
                return false;
            }

            var code = NewCode();

            // Update user with code and expiry
            _currentUser.EmailVerificationCode = code;
            _currentUser.EmailVerificationCodeExpiry = UtcNow + CodeLifetime;
            _currentUser.EmailVerificationAttempts = 0;
            await _userWriter.UpdateAsync(_currentUser);

            // Send email
            await _emailService.SendVerificationCodeAsync(email, code);

            _logger.LogInformation("Verification code sent to {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification code for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not send verification code", ex);
        }
    }

    public async Task<bool> VerifyEmailCodeAsync(string code)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot verify code: no user is logged in");
                return false;
            }

            if (string.IsNullOrEmpty(_currentUser.EmailVerificationCode))
            {
                _logger.LogWarning("No verification code found for user {Email}", _currentUser.Email);
                return false;
            }

            if (_currentUser.EmailVerificationCodeExpiry == null ||
                _currentUser.EmailVerificationCodeExpiry < UtcNow)
            {
                _logger.LogWarning("Verification code expired for user {Email}", _currentUser.Email);
                return false;
            }

            if (!CodesMatch(_currentUser.EmailVerificationCode, code))
            {
                _currentUser.EmailVerificationAttempts++;
                if (_currentUser.EmailVerificationAttempts >= MaxCodeAttempts)
                {
                    _currentUser.EmailVerificationCode = null; // void: a new code must be requested
                    _currentUser.EmailVerificationCodeExpiry = null;
                    _logger.LogWarning("Verification code for {Email} voided after {Count} wrong guesses", _currentUser.Email, MaxCodeAttempts);
                }

                await _userWriter.UpdateAsync(_currentUser);
                _logger.LogWarning("Invalid verification code for user {Email}", _currentUser.Email);
                return false;
            }

            // Mark email as verified
            _currentUser.EmailVerified = true;
            _currentUser.EmailVerificationCode = null;
            _currentUser.EmailVerificationCodeExpiry = null;
            _currentUser.EmailVerificationAttempts = 0;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Email verified successfully for user {Email}", _currentUser.Email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify email code for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not verify email code", ex);
        }
    }

    public async Task<bool> UpdateEmailAsync(string email)
    {
        try
        {
            if (_currentUser == null)
            {
                _logger.LogWarning("Cannot update email: no user is logged in");
                return false;
            }

            _currentUser.Email = email;
            _currentUser.EmailVerified = false;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Email updated to {Email}", email);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update email for user {Email}", _currentUser?.Email);
            throw new AuthServiceException("Could not update email", ex);
        }
    }

    public async Task<bool> SendPasswordResetCodeAsync(string email)
    {
        try
        {
            _logger.LogDebug("Password reset code requested for email {Email}", email);

            // Every request counts, so an address can neither guess codes nor flood the log.
            var address = _clientAddress.Value;
            if (_throttle.RetryAfter(AuthThrottleScope.PasswordReset, address) is { } wait)
            {
                _logger.LogWarning("Password reset refused: {ClientAddress} is throttled", address);
                throw new AuthThrottledException(wait);
            }

            _throttle.RecordFailure(AuthThrottleScope.PasswordReset, address);

            var user = await _userReader.GetByEmailAsync(email);
            if (user == null)
            {
                _logger.LogWarning("Password reset requested for non-existent email {Email}", email);
                // Don't reveal that the email doesn't exist for security reasons
                return true;
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Password reset requested for inactive user {Email}", user.Email);
                // Don't reveal that the account is inactive for security reasons
                return true;
            }

            var code = NewCode();

            // Update user with reset code and expiry
            user.PasswordResetCode = code;
            user.PasswordResetCodeExpiry = UtcNow + CodeLifetime;
            user.PasswordResetAttempts = 0;
            await _userWriter.UpdateAsync(user);

            // Send email
            await _emailService.SendPasswordResetCodeAsync(email, code);

            _logger.LogInformation("Password reset code sent to {Email}", email);
            return true;
        }
        catch (Exception ex) when (ex is not AuthThrottledException)
        {
            _logger.LogError(ex, "Failed to send password reset code for email {Email}", email);
            throw new AuthServiceException("Could not send password reset code", ex);
        }
    }

    public async Task<bool> VerifyPasswordResetCodeAsync(string email, string code)
    {
        try
        {
            var user = await _userReader.GetByEmailAsync(email);
            return user is not null && await CheckResetCodeAsync(user, code);
        }
        catch (Exception ex) when (ex is not AuthThrottledException)
        {
            _logger.LogError(ex, "Failed to verify password reset code for email {Email}", email);
            throw new AuthServiceException("Could not verify password reset code", ex);
        }
    }

    public async Task<bool> ResetPasswordAsync(string email, string code, string newPassword)
    {
        try
        {
            var user = await _userReader.GetByEmailAsync(email);
            if (user == null || !await CheckResetCodeAsync(user, code))
            {
                _logger.LogWarning("Password reset failed: invalid or expired code for email {Email}", email);
                return false;
            }

            // Update password and clear reset code
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            user.PasswordResetCode = null;
            user.PasswordResetCodeExpiry = null;
            user.PasswordResetAttempts = 0;
            user.FailedLoginCount = 0;
            user.LockoutEndsAt = null;
            user.RequiresPasswordChange = false;
            user.EmailVerified = false; // Require email verification after password reset
            await _userWriter.UpdateAsync(user);

            _logger.LogInformation("Password reset successfully for email {Email}. Email verification required.", email);
            return true;
        }
        catch (Exception ex) when (ex is not AuthThrottledException)
        {
            _logger.LogError(ex, "Failed to reset password for email {Email}", email);
            throw new AuthServiceException("Could not reset password", ex);
        }
    }

    /// <summary>
    /// Checks a reset code. A wrong guess counts against the address and the code; at
    /// <see cref="MaxCodeAttempts"/> the code is void, so it can't be brute-forced.
    /// </summary>
    private async Task<bool> CheckResetCodeAsync(User user, string code)
    {
        var address = _clientAddress.Value;
        if (_throttle.RetryAfter(AuthThrottleScope.PasswordReset, address) is { } wait)
        {
            throw new AuthThrottledException(wait);
        }

        if (string.IsNullOrEmpty(user.PasswordResetCode))
        {
            _logger.LogWarning("No password reset code found for email {Email}", user.Email);
            return false;
        }

        if (user.PasswordResetCodeExpiry == null || user.PasswordResetCodeExpiry < UtcNow)
        {
            _logger.LogWarning("Password reset code expired for email {Email}", user.Email);
            return false;
        }

        if (!CodesMatch(user.PasswordResetCode, code))
        {
            _throttle.RecordFailure(AuthThrottleScope.PasswordReset, address);
            user.PasswordResetAttempts++;
            if (user.PasswordResetAttempts >= MaxCodeAttempts)
            {
                user.PasswordResetCode = null; // void: a new code must be requested
                user.PasswordResetCodeExpiry = null;
                _logger.LogWarning("Password reset code for {Email} voided after {Count} wrong guesses", user.Email, MaxCodeAttempts);
            }

            await _userWriter.UpdateAsync(user);
            _logger.LogWarning("Invalid password reset code for email {Email} ({ClientAddress})", user.Email, address);
            return false;
        }

        _logger.LogInformation("Password reset code verified for email {Email}", user.Email);
        return true;
    }
}

