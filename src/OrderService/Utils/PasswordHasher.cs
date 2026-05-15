using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace OrderService.Utils
{
    public static class PasswordHasher
    {
        /// <summary>
        /// Hash Password
        /// </summary>
        /// <param name="password"></param>
        /// <param name="iterations"></param>
        /// <returns></returns>
        public static string HashPassword(string password, int iterations = 100_000)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password must not be null or empty.", nameof(password));

            var salt = new byte[16];
            RandomNumberGenerator.Fill(salt);

            // Use the static PBKDF2 helper to derive the key (HMACSHA256)
            var subkey = KeyDerivation.Pbkdf2(password, salt, KeyDerivationPrf.HMACSHA256, iterations, 32);

            return $"PBKDF2${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(subkey)}";
        }

        /// <summary>
        /// Verify Password
        /// </summary>
        /// <param name="password"></param>
        /// <param name="storedHash"></param>
        /// <returns></returns>
        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(storedHash))
                return false;
            if (!storedHash.StartsWith("PBKDF2$", StringComparison.Ordinal))
                return false;

            var parts = storedHash.Split('$');
            if (parts.Length != 4)
                return false;

            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
                return false;

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

            try
            {
                var actual = KeyDerivation.Pbkdf2(password ?? string.Empty, salt, KeyDerivationPrf.HMACSHA256, iterations, expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                // Any failure during derivation should be treated as verification failure
                return false;
            }
        }
    }
}
