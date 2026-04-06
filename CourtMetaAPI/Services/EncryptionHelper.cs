using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CourtMetaAPI.Services;

/// <summary>
/// Ports the encrypt/decrypt logic from the eCourts mobile app (main.js).
///
/// encryptData(data):
///   key  = 4D6251655468576D5A7134743677397A  (hex, 16 bytes → AES-128)
///   IV   = globaliv[random 0-5]  +  randomHex(16 chars)   → 32 hex chars = 16 bytes
///   result = randomiv(16 hex) + globalIndex(1 digit) + Base64(AES-CBC ciphertext)
///
/// decodeResponse(result):
///   key  = 3273357638782F413F4428472B4B6250  (hex, 16 bytes)
///   IV   = first 32 chars of result   → 16 bytes
///   data = chars from position 32 onwards
/// </summary>
public static class EncryptionHelper
{
    // Encryption key (for outgoing request data and the JWT in the Auth header)
    private static readonly byte[] EncryptKey =
        Convert.FromHexString("4D6251655468576D5A7134743677397A");

    // Decryption key (for incoming encrypted responses)
    private static readonly byte[] DecryptKey =
        Convert.FromHexString("3273357638782F413F4428472B4B6250");

    // Six possible globaliv values picked randomly each call
    private static readonly string[] GlobalIvOptions =
    [
        "556A586E32723575",
        "34743777217A2543",
        "413F4428472B4B62",
        "48404D635166546A",
        "614E645267556B58",
        "655368566D597133"
    ];

    /// <summary>
    /// Encrypts <paramref name="data"/> (any object – serialised to JSON first)
    /// exactly as the mobile app's encryptData() does.
    /// </summary>
    public static string EncryptData(object data)
    {
        var json = JsonSerializer.Serialize(data);

        // Pick a random globaliv entry
        var globalIndex = Random.Shared.Next(0, GlobalIvOptions.Length);
        var globaliv = GlobalIvOptions[globalIndex];

        // Generate 8 random bytes → 16 lowercase hex chars
        var randomIvBytes = new byte[8];
        RandomNumberGenerator.Fill(randomIvBytes);
        var randomiv = Convert.ToHexString(randomIvBytes).ToLower();

        // IV = globaliv(16 hex) + randomiv(16 hex) → 32 hex chars → 16 bytes
        var iv = Convert.FromHexString(globaliv + randomiv);

        var plainBytes = Encoding.UTF8.GetBytes(json);

        using var aes = Aes.Create();
        aes.Key = EncryptKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        using var encryptor = aes.CreateEncryptor();
        var cipherBytes = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);
        var base64Cipher = Convert.ToBase64String(cipherBytes);

        // Result layout: randomiv(16) + globalIndex(1 digit) + base64(cipher)
        return randomiv + globalIndex.ToString() + base64Cipher;
    }

    /// <summary>
    /// Decrypts an encrypted response body as the mobile app's decodeResponse() does.
    /// Returns the plain JSON string, or null if decryption fails.
    /// </summary>
    public static string? DecryptResponse(string result)
    {
        try
        {
            result = result.Trim();
            if (result.Length < 32) return null;

            var ivHex = result[..32];
            var cipherPart = result[32..].Trim();

            var iv = Convert.FromHexString(ivHex);
            var cipherBytes = Convert.FromBase64String(cipherPart);

            using var aes = Aes.Create();
            aes.Key = DecryptKey;
            aes.IV = iv;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;

            using var decryptor = aes.CreateDecryptor();
            var plainBytes = decryptor.TransformFinalBlock(cipherBytes, 0, cipherBytes.Length);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }
}
