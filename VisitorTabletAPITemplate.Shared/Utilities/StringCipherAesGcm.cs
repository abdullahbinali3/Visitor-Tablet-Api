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
            // Trim any extra whitespace from the cipherText
            cipherText = cipherText.Trim();
            if (!IsBase64String(cipherText))
            {
                cipherText = CleanAndConvertToBase64(cipherText);
            }

            // Decode base64 string into byte array
            Span<byte> encryptedData = Convert.FromBase64String(cipherText).AsSpan();

            // Initialize pointers and safeguard sizes
            int ptr = 0;
            Span<byte> salt = new byte[saltSize];

            // Check and read the salt if the data contains enough bytes
            if (encryptedData.Length >= saltSize)
            {
                salt = encryptedData.Slice(ptr, saltSize).ToArray();
                ptr += saltSize;
            }

            // Safeguard for nonce size
            int nonceSize = 0;
            if (encryptedData.Length >= ptr + sizeof(int))
            {
                nonceSize = BinaryPrimitives.ReadInt32LittleEndian(encryptedData.Slice(ptr, sizeof(int)));
                ptr += sizeof(int);
            }
            else
            {
                // Fallback if nonce size can't be read
                nonceSize = AesGcm.NonceByteSizes.MaxSize;
            }

            // Safeguard for nonce data
            Span<byte> nonce = nonceSize > 0 && encryptedData.Length >= ptr + nonceSize
                ? encryptedData.Slice(ptr, nonceSize)
                : new byte[nonceSize]; // default to empty nonce
            ptr += nonce.Length;

            // Safeguard for tag size
            int tagSize = 0;
            if (encryptedData.Length >= ptr + sizeof(int))
            {
                tagSize = BinaryPrimitives.ReadInt32LittleEndian(encryptedData.Slice(ptr, sizeof(int)));
                ptr += sizeof(int);
            }
            else
            {
                // Fallback if tag size can't be read
                tagSize = AesGcm.TagByteSizes.MaxSize;
            }

            // Safeguard for tag data
            Span<byte> tag = tagSize > 0 && encryptedData.Length >= ptr + tagSize
                ? encryptedData.Slice(ptr, tagSize)
                : new byte[tagSize]; // default to empty tag
            ptr += tag.Length;

            // Calculate remaining cipher text size and safeguard it
            int cipherSize = encryptedData.Length > ptr
                ? encryptedData.Length - ptr
                : 0;
            Span<byte> cipherBytes = cipherSize > 0
                ? encryptedData.Slice(ptr, cipherSize)
                : Span<byte>.Empty; // default to empty cipher text if not enough data

            // Derive key
            int tagSizeInBytes = 16;
            using Rfc2898DeriveBytes deriveBytes = new Rfc2898DeriveBytes(password, salt.ToArray(), iterations, HashAlgorithmName.SHA512);
            byte[] key = deriveBytes.GetBytes(tagSizeInBytes);

            // Initialize AES implementation
            using AesGcm aes = new AesGcm(key, tagSizeInBytes);

            // Decrypt if there is cipher text to process
            if (!cipherBytes.IsEmpty)
            {
                Span<byte> plainBytes = cipherBytes.Length < 1024 ? stackalloc byte[cipherBytes.Length] : new byte[cipherBytes.Length];
                aes.Decrypt(nonce, cipherBytes, tag, plainBytes);

                // Convert plain bytes back into string
                return Encoding.UTF8.GetString(plainBytes);
            }

            // Return empty string if there's no data to decrypt
            return string.Empty;
        }

        static bool IsBase64String(string base64)
        {
            Span<byte> buffer = new Span<byte>(new byte[base64.Length]);
            return Convert.TryFromBase64String(base64, buffer, out _);
        }
        public static string CleanAndConvertToBase64(string input)
        {
            // Remove all characters that are not valid in Base64
            string cleanedInput = new string(input.Where(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=').ToArray());

            // Convert the cleaned string to a byte array
            byte[] byteArray = Encoding.UTF8.GetBytes(cleanedInput);

            // Convert byte array to Base64 string
            string base64String = Convert.ToBase64String(byteArray);

            // Ensure correct padding
            base64String = base64String.PadRight(base64String.Length + (4 - base64String.Length % 4) % 4, '=');

            return base64String;
        }

    }
}
