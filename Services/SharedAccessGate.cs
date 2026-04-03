using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace FFVIIEverCrisisAnalyzer.Services;

public sealed class SharedAccessGate
{
    private readonly SharedAccessOptions _options;
    private readonly HashSet<string> _protectedPages;

    public SharedAccessGate(IOptions<SharedAccessOptions> options)
    {
        _options = options.Value;
        _protectedPages = new HashSet<string>(
            _options.ProtectedPages
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(NormalizePath),
            StringComparer.OrdinalIgnoreCase);
    }

    public string CookieName => string.IsNullOrWhiteSpace(_options.CookieName)
        ? "LeadershipUnlock"
        : _options.CookieName;

    public bool RequiresUnlock(PathString path)
    {
        var value = NormalizePath(path.Value);
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (value.StartsWith("/unlock", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/error", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("/privacy", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return _protectedPages.Contains(value);
    }

    public bool ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(_options.PasswordHash))
        {
            return false;
        }

        var providedHash = ComputeBase64Sha256(password.Trim());
        return FixedTimeEquals(providedHash, _options.PasswordHash.Trim());
    }

    public string GenerateToken()
    {
        var utcNow = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var payload = $"{_options.PasswordVersion}|{utcNow}";
        var signature = ComputeBase64Sha256(payload + "|" + _options.PasswordHash);
        return $"{payload}|{signature}";
    }

    public bool IsValidToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var parts = token.Split('|');
        if (parts.Length != 3)
        {
            return false;
        }

        var version = parts[0];
        var issuedAtRaw = parts[1];
        var signature = parts[2];

        if (!string.Equals(version, _options.PasswordVersion, StringComparison.Ordinal))
        {
            return false;
        }

        if (!long.TryParse(issuedAtRaw, out var issuedAtSeconds))
        {
            return false;
        }

        var payload = $"{version}|{issuedAtRaw}";
        var expectedSignature = ComputeBase64Sha256(payload + "|" + _options.PasswordHash);
        if (!FixedTimeEquals(signature, expectedSignature))
        {
            return false;
        }

        var issuedAt = DateTimeOffset.FromUnixTimeSeconds(issuedAtSeconds);
        var expiry = issuedAt.AddHours(Math.Max(1, _options.UnlockDurationHours));
        return expiry >= DateTimeOffset.UtcNow;
    }

    public CookieOptions BuildCookieOptions()
    {
        return new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Lax,
            Expires = DateTimeOffset.UtcNow.AddHours(Math.Max(1, _options.UnlockDurationHours))
        };
    }

    public string SanitizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        if (!Uri.TryCreate(returnUrl, UriKind.Relative, out _))
        {
            return "/";
        }

        if (!returnUrl.StartsWith('/'))
        {
            return "/";
        }

        return returnUrl;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var trimmed = path.Trim();
        if (!trimmed.StartsWith('/'))
        {
            trimmed = "/" + trimmed;
        }

        return trimmed.TrimEnd('/');
    }

    private static string ComputeBase64Sha256(string value)
    {
        using var sha = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(value);
        var hash = sha.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
