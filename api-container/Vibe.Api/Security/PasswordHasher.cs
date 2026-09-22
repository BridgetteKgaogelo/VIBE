using System.Security.Cryptography;

namespace Vibe.Api.Security;

/// <summary>
/// Salted, one-way password hashing (PoE security requirement, OWASP aligned).
/// </summary>
/// <remarks>
/// PBKDF2-HMAC-SHA256 with 210 000 iterations, a 128-bit random salt and a 256-bit
/// hash - the OWASP Password Storage Cheat Sheet recommendation for PBKDF2. The
/// stored format is self-describing so the cost can be raised later without
/// invalidating existing accounts:
///
/// <code>pbkdf2-sha256$210000$&lt;base64 salt&gt;$&lt;base64 hash&gt;</code>
///
/// Passwords are never returned, logged or stored anywhere else. Verification uses
/// <see cref="CryptographicOperations.FixedTimeEquals"/> so a wrong password cannot
/// be narrowed down by timing.
/// </remarks>
public static class PasswordHasher
{
    private const string Scheme = "pbkdf2-sha256";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    /// <summary>Cost factor; bump it and old hashes still verify with their own value.</summary>
    public const int DefaultIterations = 210_000;

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(password);
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            HashBytes);

        return string.Join('$', Scheme, iterations, Convert.ToBase64String(salt), Convert.ToBase64String(hash));
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(stored))
        {
            return false;
        }

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Scheme || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        byte[] salt;
        byte[] expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Rfc2898DeriveBytes.Pbkdf2(
            password,
            salt,
            iterations,
            HashAlgorithmName.SHA256,
            expected.Length);

        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
