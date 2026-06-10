using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RecruitAI.Infrastructure.Webhooks;

/// <summary>
/// Computes and verifies HMAC-SHA256 signatures for webhook payloads.
///
/// Signature format:
///   X-RecruitAI-Signature: sha256=&lt;hex-encoded-hmac&gt;
///
/// Signing key: WebhookConfiguration.SecretKey (per-tenant, stored encrypted)
/// Payload: raw UTF-8 JSON body bytes
/// </summary>
public static class HmacSigningUtility
{
    public const string SignatureHeader = "X-RecruitAI-Signature";
    private const string Prefix = "sha256=";

    /// <summary>Computes the HMAC-SHA256 signature of a JSON payload.</summary>
    public static string ComputeSignature(string payloadJson, string secretKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payloadJson);

        using var hmac = new HMACSHA256(keyBytes);
        var hash = hmac.ComputeHash(payloadBytes);
        return Prefix + Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Serializes a payload to canonical JSON (sorted keys, no whitespace)
    /// to ensure consistent signatures regardless of sender/receiver serializer.
    /// </summary>
    public static string SerializePayload<T>(T payload)
    {
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    /// <summary>Timing-safe signature verification.</summary>
    public static bool VerifySignature(string payloadJson, string secretKey, string receivedSignature)
    {
        var expected = ComputeSignature(payloadJson, secretKey);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(receivedSignature));
    }
}
