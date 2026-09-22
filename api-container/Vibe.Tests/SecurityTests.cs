using Vibe.Api.Security;
using Xunit;

namespace Vibe.Tests;

public class PasswordHasherTests
{
    [Fact]
    public void A_password_is_never_stored_as_it_was_typed()
    {
        var stored = PasswordHasher.Hash("Vibe2026go");

        Assert.DoesNotContain("Vibe2026go", stored);
        Assert.StartsWith("pbkdf2-sha256$", stored);
        Assert.Equal(4, stored.Split('$').Length);
    }

    [Fact]
    public void The_same_password_hashes_differently_every_time()
    {
        // A fresh salt per account: two members with the same password must not
        // share a hash, and the hash must not be guessable from a rainbow table.
        var first = PasswordHasher.Hash("Vibe2026go");
        var second = PasswordHasher.Hash("Vibe2026go");

        Assert.NotEqual(first, second);
        Assert.True(PasswordHasher.Verify("Vibe2026go", first));
        Assert.True(PasswordHasher.Verify("Vibe2026go", second));
    }

    [Fact]
    public void Verification_rejects_wrong_and_damaged_hashes()
    {
        var stored = PasswordHasher.Hash("Vibe2026go");

        Assert.False(PasswordHasher.Verify("vibe2026go", stored));
        Assert.False(PasswordHasher.Verify("", stored));
        Assert.False(PasswordHasher.Verify("Vibe2026go", "not-a-hash"));
        Assert.False(PasswordHasher.Verify("Vibe2026go", "pbkdf2-sha256$210000$notbase64$alsonot"));
    }

    [Fact]
    public void The_cost_factor_travels_with_the_hash_so_it_can_be_raised_later()
    {
        var old = PasswordHasher.Hash("Vibe2026go", iterations: 10_000);
        var current = PasswordHasher.Hash("Vibe2026go");

        Assert.Contains("$10000$", old);
        Assert.Contains($"${PasswordHasher.DefaultIterations}$", current);
        Assert.True(PasswordHasher.Verify("Vibe2026go", old));
    }
}

public class ValidatorTests
{
    [Fact]
    public void Names_and_emails_follow_the_same_rules_as_the_app()
    {
        Assert.NotNull(Validators.DisplayName("  "));
        Assert.NotNull(Validators.DisplayName(new string('L', 41)));
        Assert.Null(Validators.Email("not-an-email"));
        Assert.Null(Validators.Email("lerato@vibe.app"));
        Assert.NotNull(Validators.Email("lerato@vibe"));
    }

    [Fact]
    public void Passwords_need_eight_characters_and_a_number()
    {
        Assert.NotNull(Validators.Password("vibe1"));
        Assert.NotNull(Validators.Password("vibevibevibe"));
        Assert.Null(Validators.Password("vibe2026go"));
        Assert.Null(Validators.PasswordConfirmation("vibe2026", "vibe2026"));
        Assert.NotNull(Validators.PasswordConfirmation("vibe2026", "vibe2027"));
    }

    [Fact]
    public void A_round_needs_two_ideas_and_a_rating_stays_between_zero_and_five()
    {
        Assert.NotNull(Validators.RoundActivities(1));
        Assert.Null(Validators.RoundActivities(2));
        Assert.Null(Validators.Rating(5));
        Assert.NotNull(Validators.Rating(6));
        Assert.NotNull(Validators.Caption(new string('x', 281)));
    }

    [Fact]
    public void Invite_codes_are_normalised_before_they_are_checked()
    {
        Assert.Equal("FRYDAY", Validators.NormaliseInviteCode(" fryday "));
        Assert.True(Validators.IsInviteCode("fryday"));
        Assert.False(Validators.IsInviteCode("FRYDA"));
        Assert.False(Validators.IsInviteCode("FRYDAY1"));
    }
}
