using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace ChessSlackBetter.Bot.Slack;

/// <summary>Verifies the X-Slack-Signature header: https://api.slack.com/authentication/verifying-requests-from-slack.</summary>
public sealed class SlackSignatureVerifier(IOptions<SlackOptions> options, TimeProvider time)
{
    private static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(5);

    public bool IsValid(string? timestamp, string? signature, string body)
    {
        var secret = options.Value.SigningSecret;
        if (string.IsNullOrEmpty(secret) || string.IsNullOrEmpty(signature) || !long.TryParse(timestamp, out var seconds))
        {
            return false;
        }

        var age = time.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (age.Duration() > MaxAge)
        {
            return false;
        }

        var expected = Sign(secret, timestamp!, body);
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature));
    }

    public static string Sign(string secret, string timestamp, string body)
    {
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"v0:{timestamp}:{body}"));
        return "v0=" + Convert.ToHexStringLower(hash);
    }
}
