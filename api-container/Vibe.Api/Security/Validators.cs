using System.Text.RegularExpressions;

namespace Vibe.Api.Security;

/// <summary>
/// Server-side validation, mirroring <c>domain/Validation.kt</c> on the client so a
/// value accepted on the device is never rejected here, and a rule can never be
/// bypassed by calling the API directly.
/// </summary>
public static partial class Validators
{
    public const int MinPasswordLength = 8;
    public const int MaxNameLength = 40;
    public const int MaxCaptionLength = 280;
    public const int MinRoundActivities = 2;
    public const string InviteAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    public const int InviteLength = 6;

    [GeneratedRegex(@"^[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}$")]
    private static partial Regex EmailPattern();

    public static string? DisplayName(string? value)
    {
        var name = value?.Trim() ?? string.Empty;
        if (name.Length == 0) return "Enter your name.";
        if (name.Length > MaxNameLength) return $"Keep your name under {MaxNameLength} characters.";
        return null;
    }

    public static string? Email(string? value) =>
        EmailPattern().IsMatch(value?.Trim() ?? string.Empty)
            ? null
            : "Enter a valid email address, for example lerato@vibe.app.";

    public static string? Password(string? value)
    {
        var password = value ?? string.Empty;
        if (password.Length < MinPasswordLength)
        {
            return $"Use at least {MinPasswordLength} characters.";
        }

        return password.Any(char.IsDigit) ? null : "Add at least one number.";
    }

    public static string? PasswordConfirmation(string? password, string? confirmation) =>
        string.Equals(password, confirmation, StringComparison.Ordinal)
            ? null
            : "The two passwords do not match.";

    public static string? GroupName(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Give your group a name." : null;

    public static string? ActivityTitle(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Give the idea a title." : null;

    public static string? Caption(string? value) =>
        (value?.Length ?? 0) <= MaxCaptionLength
            ? null
            : $"Keep the caption under {MaxCaptionLength} characters.";

    public static string? Rating(int rating) =>
        rating is >= 0 and <= 5 ? null : "Ratings are between 0 and 5 stars.";

    public static string? RoundActivities(int count) =>
        count >= MinRoundActivities ? null : "Add at least two activities before starting a round.";

    public static bool IsInviteCode(string? value)
    {
        var code = NormaliseInviteCode(value);
        return code.Length == InviteLength && code.All(c => InviteAlphabet.Contains(c));
    }

    public static string NormaliseInviteCode(string? value) =>
        new((value ?? string.Empty)
            .Trim()
            .ToUpperInvariant()
            .Where(char.IsLetterOrDigit)
            .ToArray());
}
