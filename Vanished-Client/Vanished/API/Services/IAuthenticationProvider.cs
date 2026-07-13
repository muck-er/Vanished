using System.Threading.Tasks;

namespace Vanished.API.Services;

public sealed class AuthContext
{
    public string Email { get; init; } = string.Empty;
    public string Purpose { get; init; } = string.Empty;
}

public sealed class AuthResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public static AuthResult Ok(string message = "Autenticado.") => new() { Success = true, Message = message };
    public static AuthResult Fail(string message) => new() { Success = false, Message = message };
}

public interface IAuthenticationProvider
{
    string Name { get; }
    bool IsAvailable { get; }
    Task<AuthResult> AuthenticateAsync(AuthContext context);
}

public sealed class PasswordTotpAuthProvider : IAuthenticationProvider
{
    public string Name => "Password + TOTP";
    public bool IsAvailable => true;

    public Task<AuthResult> AuthenticateAsync(AuthContext context)
        => Task.FromResult(AuthResult.Fail("Usa o modal de password local + TOTP para esta ação."));
}

