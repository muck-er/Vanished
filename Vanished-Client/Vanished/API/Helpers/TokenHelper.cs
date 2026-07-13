using System.IdentityModel.Tokens.Jwt;
using System.Linq;

namespace Vanished.API.Helpers;

public static class TokenHelper
{
    private static string? _currentToken;
    private static string? _currentRefreshToken;

    public static string? CurrentToken => _currentToken;
    public static string? CurrentRefreshToken => _currentRefreshToken;

    public static void SaveToken(string token) => _currentToken = token;
    public static void SaveRefreshToken(string token) => _currentRefreshToken = token;

    public static void ClearToken()
    {
        _currentToken = null;
        _currentRefreshToken = null;
    }

    public static string GetEmailFromToken()
    {
        if (string.IsNullOrWhiteSpace(_currentToken))
            return string.Empty;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(_currentToken);
        return jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value ?? string.Empty;
    }
}
