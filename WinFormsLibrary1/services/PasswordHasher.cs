using System;
using System.Security.Cryptography;
using System.Text;

namespace AlphaElite.infrastructure.services
{
    /// <summary>
    /// Password hashing / verification.
    ///
    /// The existing tenant databases store unsalted SHA-256 hex hashes (64 characters)
    /// because that is what the original demo accounts were created with. Those hashes
    /// are still verifiable here so existing accounts keep working, but new passwords
    /// are stored with salted PBKDF2-SHA256.
    ///
    /// Verification is read-only: <see cref="Verify"/> never writes to the database.
    /// Re-hashing a legacy hash requires an explicit, approved write, so callers decide
    /// what to do with the reported <c>needsRehash</c> flag.
    /// </summary>
    public static class PasswordHasher
    {
        private const string Pbkdf2Prefix = "PBKDF2-SHA256";
        private const int Pbkdf2Iterations = 210_000;
        private const int SaltSize = 16;
        private const int KeySize = 32;

        /// <summary>Creates a new salted PBKDF2 hash for <paramref name="password"/>.</summary>
        public static string Hash(string password)
        {
            if (password == null) throw new ArgumentNullException(nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                Pbkdf2Iterations,
                HashAlgorithmName.SHA256,
                KeySize);

            return $"{Pbkdf2Prefix}${Pbkdf2Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
        }

        /// <summary>
        /// Verifies <paramref name="password"/> against a stored hash.
        /// <paramref name="needsRehash"/> is true when the stored hash uses the legacy
        /// unsalted SHA-256 format and should be replaced (with approval) on next login.
        /// </summary>
        public static bool Verify(string? storedHash, string password, out bool needsRehash)
        {
            needsRehash = false;

            if (string.IsNullOrWhiteSpace(storedHash) || password == null)
            {
                return false;
            }

            if (storedHash.StartsWith(Pbkdf2Prefix + "$", StringComparison.Ordinal))
            {
                return VerifyPbkdf2(storedHash, password);
            }

            if (IsLegacySha256(storedHash))
            {
                var ok = string.Equals(storedHash, ComputeLegacySha256(password), StringComparison.OrdinalIgnoreCase);
                needsRehash = ok; // correct password, but stored with an unsalted algorithm
                return ok;
            }

            return false;
        }

        private static bool VerifyPbkdf2(string storedHash, string password)
        {
            var parts = storedHash.Split('$');
            if (parts.Length != 4)
            {
                return false;
            }

            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
            {
                return false;
            }

            byte[] salt;
            byte[] expectedKey;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expectedKey = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actualKey = Rfc2898DeriveBytes.Pbkdf2(
                Encoding.UTF8.GetBytes(password),
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedKey.Length);

            return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
        }

        private static bool IsLegacySha256(string storedHash)
        {
            if (storedHash.Length != 64)
            {
                return false;
            }

            foreach (var c in storedHash)
            {
                var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!isHex)
                {
                    return false;
                }
            }

            return true;
        }

        private static string ComputeLegacySha256(string raw)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(raw));

            var sb = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
            {
                sb.Append(b.ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
