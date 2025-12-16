using StageZero.DataAdapters.Users;
using StageZero.Models;
using StageZero.Services.Email;

namespace StageZero.Services.Auth;

// ═══════════════════════════════════════════════════════════════
// INTERFACE
// ═══════════════════════════════════════════════════════════════

public interface IAuthService
{
    Task<AuthResult> LoginAsync(string username, string password);
    Task LogoutAsync();
    Task<User?> GetCurrentUserAsync();
    Task<bool> ChangePasswordAsync(string currentPassword, string newPassword);
    Task<bool> SendEmailVerificationCodeAsync(string email);
    Task<bool> VerifyEmailCodeAsync(string code);
    Task<bool> UpdateEmailAsync(string email);
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
    private readonly ILogger<AuthService> _logger;
    private readonly IUserReader _userReader;
    private readonly IUserWriter _userWriter;
    private readonly IEmailService _emailService;
    private User? _currentUser;

    public AuthService(
        ILogger<AuthService> logger,
        IUserReader userReader,
        IUserWriter userWriter,
        IEmailService emailService)
    {
        _logger = logger;
        _userReader = userReader;
        _userWriter = userWriter;
        _emailService = emailService;
    }

    public bool IsAuthenticated => _currentUser != null;

    public bool RequiresPasswordChange => _currentUser?.RequiresPasswordChange ?? false;

    public bool RequiresEmailVerification => _currentUser != null && !_currentUser.EmailVerified;

    public async Task<AuthResult> LoginAsync(string username, string password)
    {
        try
        {
            _logger.LogDebug("Login attempt for user {Username}", username);

            // Try to find user by username first, then by email
            var user = await _userReader.GetByUsernameAsync(username);
            if (user == null)
            {
                // Try to find by email
                var allUsers = await _userReader.GetAllAsync();
                user = allUsers.FirstOrDefault(u => u.Email != null && u.Email.Equals(username, StringComparison.OrdinalIgnoreCase));
            }

            if (user == null)
            {
                _logger.LogWarning("Login failed: user {Username} not found", username);
                return AuthResult.Failed("Invalid username or password");
            }

            if (!user.IsActive)
            {
                _logger.LogWarning("Login failed: user {Username} is inactive", username);
                return AuthResult.Failed("Account is inactive");
            }

            if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
            {
                _logger.LogWarning("Login failed: invalid password for user {Username}", username);
                return AuthResult.Failed("Invalid username or password");
            }

            // Update last login time
            user.LastLoginAt = DateTime.UtcNow;
            await _userWriter.UpdateAsync(user);

            _currentUser = user;
            _logger.LogInformation("User {Username} logged in successfully", username);
            return AuthResult.Succeeded(user);
        }
        catch (AuthServiceException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login failed for user {Username}", username);
            throw new AuthServiceException("Could not complete login", ex);
        }
    }

    public Task LogoutAsync()
    {
        if (_currentUser != null)
        {
            _logger.LogInformation("User {Username} logged out", _currentUser.Username);
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
                _logger.LogWarning("Password change failed: invalid current password for user {Username}", _currentUser.Username);
                return false;
            }

            // Update password
            _currentUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
            _currentUser.RequiresPasswordChange = false;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Password changed successfully for user {Username}", _currentUser.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change password for user {Username}", _currentUser?.Username);
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

            // Generate 6-digit code
            var code = new Random().Next(100000, 999999).ToString();
            var expiry = DateTime.UtcNow.AddMinutes(15);

            // Update user with code and expiry
            _currentUser.EmailVerificationCode = code;
            _currentUser.EmailVerificationCodeExpiry = expiry;
            await _userWriter.UpdateAsync(_currentUser);

            // Send email
            await _emailService.SendVerificationCodeAsync(email, code);

            _logger.LogInformation("Verification code sent to {Email} for user {Username}", email, _currentUser.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification code for user {Username}", _currentUser?.Username);
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
                _logger.LogWarning("No verification code found for user {Username}", _currentUser.Username);
                return false;
            }

            if (_currentUser.EmailVerificationCodeExpiry == null ||
                _currentUser.EmailVerificationCodeExpiry < DateTime.UtcNow)
            {
                _logger.LogWarning("Verification code expired for user {Username}", _currentUser.Username);
                return false;
            }

            if (_currentUser.EmailVerificationCode != code)
            {
                _logger.LogWarning("Invalid verification code for user {Username}", _currentUser.Username);
                return false;
            }

            // Mark email as verified
            _currentUser.EmailVerified = true;
            _currentUser.EmailVerificationCode = null;
            _currentUser.EmailVerificationCodeExpiry = null;
            await _userWriter.UpdateAsync(_currentUser);

            _logger.LogInformation("Email verified successfully for user {Username}", _currentUser.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify email code for user {Username}", _currentUser?.Username);
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

            _logger.LogInformation("Email updated to {Email} for user {Username}", email, _currentUser.Username);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update email for user {Username}", _currentUser?.Username);
            throw new AuthServiceException("Could not update email", ex);
        }
    }
}

