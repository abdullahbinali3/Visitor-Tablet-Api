using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace VisitorTabletAPITemplate.Utilities
{
    /* Based on answer by janw of StackOverflow, with some adjustments by Shane
     * https://stackoverflow.com/questions/60889345/using-the-aesgcm-class
     */
    public static class StringCipherAesGcm
    {
        private const int defaultIterations = 100_000;
        private const int saltSize = 16;

        public static string Encrypt(string plainText, string password)
        {
            return Encrypt(plainText, password, defaultIterations);
        }

        public static string Encrypt(string plainText, string password, int iterations)
        {
            // Salt is randomly generated each time, but is preprended to encrypted cipher text
            // so that the same Salt value can be used when decrypting.  
            byte[] salt = RandomNumberGenerator.GetBytes(saltSize);

            // Derive key
            // AES key size is 16 bytes
            int tagSizeInBytes = 16;

            using Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA512);
            byte[] key = deriveBytes.GetBytes(tagSizeInBytes);

            // Initialize AES implementation
            using AesGcm aes = new AesGcm(key, tagSizeInBytes);

            // Get bytes of plaintext string
            byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);

            // Get parameter sizes
            int nonceSize = AesGcm.NonceByteSizes.MaxSize;
            int tagSize = AesGcm.TagByteSizes.MaxSize;
            int cipherSize = plainBytes.Length;

            // We write everything into one big array for easier encoding
            int encryptedDataLength = saltSize + sizeof(int) + nonceSize + sizeof(int) + tagSize + cipherSize;
            Span<byte> encryptedData = encryptedDataLength < 1024 ? stackalloc byte[encryptedDataLength] : new byte[encryptedDataLength].AsSpan();

            // Copy parameters
            int ptr = 0;
            salt.CopyTo(encryptedData.Slice(ptr, saltSize));
            ptr += saltSize;
            BinaryPrimitives.WriteInt32LittleEndian(encryptedData.Slice(ptr, sizeof(int)), nonceSize);
            ptr += sizeof(int);
            Span<byte> nonce = encryptedData.Slice(ptr, nonceSize);
            ptr += nonceSize;
            BinaryPrimitives.WriteInt32LittleEndian(encryptedData.Slice(ptr, sizeof(int)), tagSize);
            ptr += sizeof(int);
            Span<byte> tag = encryptedData.Slice(ptr, tagSize);
            ptr += tagSize;
            Span<byte> cipherBytes = encryptedData.Slice(ptr, cipherSize);

            // Generate secure nonce
            RandomNumberGenerator.Fill(nonce);

            // Encrypt
            aes.Encrypt(nonce, plainBytes.AsSpan(), cipherBytes, tag);

            // Encode for transmission
            return Convert.ToBase64String(encryptedData);
        }

        public static string Decrypt(string cipherText, string password)
        {
            return Decrypt(cipherText, password, defaultIterations);
        }

        public static string Decrypt(string cipherText, string password, int iterations)
        {
            // Decode
            Span<byte> encryptedData = Convert.FromBase64String(cipherText).AsSpan();

            // Extract parameter sizes + data
            int ptr = 0;
            byte[] salt = encryptedData.Slice(ptr, saltSize).ToArray();
            ptr += saltSize;
            int nonceSize = BinaryPrimitives.ReadInt32LittleEndian(encryptedData.Slice(ptr, sizeof(int)));
            ptr += sizeof(int);
            Span<byte> nonce = encryptedData.Slice(ptr, nonceSize);
            ptr += nonceSize;
            int tagSize = BinaryPrimitives.ReadInt32LittleEndian(encryptedData.Slice(ptr, sizeof(int)));
            ptr += sizeof(int);
            Span<byte> tag = encryptedData.Slice(ptr, tagSize);
            ptr += tagSize;
            int cipherSize = encryptedData.Length - ptr;
            Span<byte> cipherBytes = encryptedData.Slice(ptr, cipherSize);

            // Derive key
            // AES key size is 16 bytes
            int tagSizeInBytes = 16;

            using Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA512);
            byte[] key = deriveBytes.GetBytes(tagSizeInBytes);

            // Initialize AES implementation
            using AesGcm aes = new AesGcm(key, tagSizeInBytes);

            // Decrypt
            Span<byte> plainBytes = cipherSize < 1024 ? stackalloc byte[cipherSize] : new byte[cipherSize];
            aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

            // Convert plain bytes back into string
            return Encoding.UTF8.GetString(plainBytes);
        }
    }
}
