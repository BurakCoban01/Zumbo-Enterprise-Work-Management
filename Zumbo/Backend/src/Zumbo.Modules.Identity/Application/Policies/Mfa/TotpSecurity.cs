using System.Security.Cryptography;
using System.Text;

namespace Zumbo.Modules.Identity;

public static class TotpSecurity
{
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    public static string GenerateSecret() => Base32Encode(RandomNumberGenerator.GetBytes(20));

    public static string GenerateCode(string secret, DateTimeOffset now)
    {
        var key = Base32Decode(secret);
        var counter = now.ToUnixTimeSeconds() / 30;
        Span<byte> counterBytes = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(counterBytes, counter);
        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
            | (hash[offset + 1] << 16)
            | (hash[offset + 2] << 8)
            | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    public static bool Verify(string secret, string? code, DateTimeOffset now)
    {
        var normalized = code?.Trim();
        if (normalized?.Length != 6 || normalized.Any(x => !char.IsAsciiDigit(x)))
        {
            return false;
        }

        var supplied = Encoding.ASCII.GetBytes(normalized);
        var valid = false;
        for (var offset = -1; offset <= 1; offset++)
        {
            var expected = Encoding.ASCII.GetBytes(GenerateCode(secret, now.AddSeconds(offset * 30)));
            valid |= CryptographicOperations.FixedTimeEquals(supplied, expected);
        }

        return valid;
    }

    public static string GenerateRecoveryCode()
    {
        var raw = Base32Encode(RandomNumberGenerator.GetBytes(8));
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..]}";
    }

    public static string HashRecoveryCode(string code)
    {
        var normalized = new string((code ?? string.Empty)
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        return Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(normalized)));
    }

    private static string Base32Encode(ReadOnlySpan<byte> data)
    {
        var output = new StringBuilder((data.Length * 8 + 4) / 5);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var value in data)
        {
            buffer = (buffer << 8) | value;
            bitsLeft += 8;
            while (bitsLeft >= 5)
            {
                output.Append(Base32Alphabet[(buffer >> (bitsLeft - 5)) & 31]);
                bitsLeft -= 5;
            }
        }

        if (bitsLeft > 0)
        {
            output.Append(Base32Alphabet[(buffer << (5 - bitsLeft)) & 31]);
        }

        return output.ToString();
    }

    private static byte[] Base32Decode(string value)
    {
        var normalized = new string(value
            .Where(char.IsAsciiLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());
        var output = new List<byte>(normalized.Length * 5 / 8);
        var buffer = 0;
        var bitsLeft = 0;
        foreach (var character in normalized)
        {
            var index = Base32Alphabet.IndexOf(character);
            if (index < 0)
            {
                throw new CryptographicException("TOTP secret is invalid.");
            }

            buffer = (buffer << 5) | index;
            bitsLeft += 5;
            if (bitsLeft >= 8)
            {
                output.Add((byte)(buffer >> (bitsLeft - 8)));
                bitsLeft -= 8;
            }
        }

        return output.ToArray();
    }
}
