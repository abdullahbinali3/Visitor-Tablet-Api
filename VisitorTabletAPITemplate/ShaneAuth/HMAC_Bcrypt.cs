/*
 * hmac-bcrypt by Jeremi M Gosney - 31st July 2022
 * https://github.com/epixoip/hmac-bcrypt
 * 
 * Comments below by Shane 1st October 2022
 * 
 * Explanation:
 * 
 * - "Password" is the password to be hashed.
 * 
 * - "Cost" is how expensive the hashing function will be. Recommended minimum: 9, maximum: 14.
 *   Cost is exponential. Default cost is 13. A value of 9 is recommended for real-time server-side applications,
 *   i.e. web or mobile backend APIs, or a value of 10 can be used for lower traffic and/or more powerful servers.
 *   
 * - "Salt" is used when generating the hash. Should be different for every hash generated (automatic default behaviour).
 *   You can specify a custom salt, but this is not recommended.
 *   
 * - "Pepper" is used to add strength to the hash to make it harder to crack, giving extra security
 *   even if the original password is weak.
 *     - Pepper can be the same throughout the application (same value used for all hashed passwords), but must be
 *       kept a secret otherwise the added strength is compromised.
 *     - Pepper is optional but recommended.
 * 
 * Usage:
 * 
 * // Uses default cost of 13 and default pepper of "hmac_bcrypt". Automatically generates a random salt.
 * 1) string hash = HMAC_Bcrypt.hmac_bcrypt_hash(password);
 * 
 * // Manually set cost, uses default pepper of "hmac_bcrypt". Automatically generates a random salt.
 * 2) string hash = HMAC_Bcrypt.hmac_bcrypt_hash(password, "$2a$10"); 
 * 
 * // Uses default cost of 13 with a custom pepper. Automatically generates a random salt.
 * 3) string hash = HMAC_Bcrypt.hmac_bcrypt_hash(password, pepper: pepper);
 * 
 * // Manually set cost of 10 with a custom pepper. Automatically generates a random salt.
 * 4) string hash = HMAC_Bcrypt.hmac_bcrypt_hash(password, "$2a$10", pepper);
 * 
 * // Manually set cost of 10 and fixed salt, uses default pepper of "hmac_bcrypt". **NOT RECOMMENDED TO SPECIFY SALT**
 * 5) string hash = HMAC_Bcrypt.hmac_bcrypt_hash(password, "$2a$10$v.vnO5oVlX/5zJM9TTXSz.");
 *
 * // Manually set cost of 10 with fixed salt and custom pepper. **NOT RECOMMENDED TO SPECIFY SALT**
 * 6) string hash = HMAC_Bcrypt.hmac_bcrypt_hash(password, "$2a$10$v.vnO5oVlX/5zJM9TTXSz.", pepper);
 */
using System.Security.Cryptography;
using System.Text;

namespace hmac_bcrypt
{
    static class HMAC_Bcrypt
    {
        private const int BCRYPT_COST = 13;
        private const string BCRYPT_PEPPER = "hmac_bcrypt";

        public static string hmac_bcrypt_hash(string password, string? settings = null, string? pepper = null)
        {
            int cost = BCRYPT_COST;
            string? salt = null;

            if (!string.IsNullOrEmpty(settings))
            {
                string[] sets = settings.Split('$');

                cost = short.Parse(sets[2]);

                if (sets.Length > 3 && !string.IsNullOrEmpty(sets[3]))
                {
                    salt = sets[3];
                }
            }

            if (string.IsNullOrEmpty(salt))
            {
                settings = BCrypt.Net.BCrypt.GenerateSalt(cost);
            }
            else if (settings != null)
            {
                settings = settings[..29];
            }

            if (string.IsNullOrEmpty(pepper))
            {
                pepper = BCRYPT_PEPPER;
            }

            HMACSHA512 hmac = new HMACSHA512(
                Encoding.UTF8.GetBytes(pepper)
            );

            string pre_hash = Convert.ToBase64String(
                hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(password)
                )
            );

            string mid_hash = BCrypt.Net.BCrypt.HashPassword(pre_hash, settings);

            string post_hash = Convert.ToBase64String(
                hmac.ComputeHash(
                    Encoding.UTF8.GetBytes(mid_hash)
                )
            ).Replace("=", string.Empty);

            return settings + post_hash;
        }

        public static bool hmac_bcrypt_verify(string password, string valid, string? pepper = null)
        {
            if (string.IsNullOrEmpty(pepper))
            {
                pepper = BCRYPT_PEPPER;
            }

            byte[] a = Encoding.UTF8.GetBytes(
                hmac_bcrypt_hash(password, valid, pepper)
            );

            byte[] b = Encoding.UTF8.GetBytes(valid);

            uint diff = (uint)a.Length ^ (uint)b.Length;

            for (int i = 0; i < a.Length && i < b.Length; i++)
            {
                diff |= (uint)(a[i] ^ b[i]);
            }

            return diff == 0;
        }
    }
}