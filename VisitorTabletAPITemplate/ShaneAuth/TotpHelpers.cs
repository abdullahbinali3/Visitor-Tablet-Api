using OtpNet;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitTwoFactorAuthentication;
using VisitorTabletAPITemplate.Utilities;
using ZiggyCreatures.Caching.Fusion;

namespace VisitorTabletAPITemplate.ShaneAuth
{
    public sealed class TotpHelpers
    {
        private readonly AppSettings _appSettings;
        private readonly IFusionCache _cache;

        private const int _period = 30;
        private const int _digits = 6;
        private const string _hashFunction = "SHA512";
        private const OtpHashMode _otpHashMode = OtpHashMode.Sha512;

        public TotpHelpers(AppSettings appSettings,
            IFusionCache cache)
        {
            _appSettings = appSettings;
            _cache = cache;
        }

        /// <summary>
        /// Generates an encrypted secret key and QR code URI for a user.
        /// </summary>
        /// <param name="userEmail">The user's email address. To be included in the QR Code Uri.</param>
        /// <param name="secretKeyLength">The length of the secret key in bytes before it is encrypted. If null or <= 0, the default from appsettings.json is used instead.</param>
        /// <returns></returns>
        public (string encryptedSecretKey, string qrCodeUri) GenerateEncryptedSecretKeyAndQrCodeUri(string userEmail, int? secretKeyLength = null)
        {
            // Use appsettings.json default if secret key length not specified
            if (!secretKeyLength.HasValue || secretKeyLength.Value <= 0)
            {
                secretKeyLength = _appSettings.TwoFactorAuthentication.SecretKeyLengthBytes;
            }

            // Generate a secret key
            byte[] key = KeyGeneration.GenerateRandomKey(secretKeyLength.Value);

            string unencryptedSecretKey = Base32Encoding.ToString(key);

            // Generate Uri string to be used as a QR code.
            string qrCodeUri = new OtpUri(OtpType.Totp, unencryptedSecretKey, userEmail, _appSettings.TwoFactorAuthentication.ApplicationName,
                _otpHashMode, _digits, _period).ToString();

            // Encrypt secret key
            string encryptedSecretKey = StringCipherAesGcm.Encrypt(unencryptedSecretKey, _appSettings.TwoFactorAuthentication.SecretEncryptionKey);

            // Return both
            return (encryptedSecretKey, qrCodeUri);
        }

        /// <summary>
        /// Generates an encrypted secret key and returns it.
        /// </summary>
        /// <param name="secretKeyLength">The length of the secret key in bytes before it is encrypted. If null or <= 0, the default from appsettings.json is used instead.</param>
        /// <returns></returns>
        public string GenerateEncryptedSecretKey(int? secretKeyLength = null)
        {
            // Use appsettings.json default if secret key length not specified
            if (!secretKeyLength.HasValue || secretKeyLength.Value <= 0)
            {
                secretKeyLength = _appSettings.TwoFactorAuthentication.SecretKeyLengthBytes;
            }

            // Generate a secret key
            byte[] key = KeyGeneration.GenerateRandomKey(secretKeyLength.Value);
            
            // Convert to base32 string
            string base32String = Base32Encoding.ToString(key);

            // Encrypt the string and return it
            return StringCipherAesGcm.Encrypt(base32String, _appSettings.TwoFactorAuthentication.SecretEncryptionKey);
        }

        /// <summary>
        /// Generates a QR Code Uri using the given email and encrypted secret key.
        /// </summary>
        /// <param name="userEmail">The user's email addres.</param>
        /// <param name="encryptedSecretKey">The user's totp encrypted secret key.</param>
        /// <returns></returns>
        public string GenerateQrCodeUri(string userEmail, string encryptedSecretKey)
        {
            // Decrypts the given encrypted secret key
            string decryptedSecretKey = StringCipherAesGcm.Decrypt(encryptedSecretKey, _appSettings.TwoFactorAuthentication.SecretEncryptionKey);

            // Return Uri String, to be converted into a QR code on the front end, to be scanned by the user
            return new OtpUri(OtpType.Totp, decryptedSecretKey, userEmail, _appSettings.TwoFactorAuthentication.ApplicationName,
                _otpHashMode, _digits, _period).ToString();
        }

        public InitTwoFactorAuthenticationResponse GenerateInitTwoFactorAuthenticationResponse(string userEmail, string encryptedSecretKey)
        {
            // Decrypts the given encrypted secret key
            string decryptedSecretKey = StringCipherAesGcm.Decrypt(encryptedSecretKey, _appSettings.TwoFactorAuthentication.SecretEncryptionKey);

            // Uri String, to be converted into a QR code on the front end, to be scanned by the user
            string? uriString = new OtpUri(OtpType.Totp, decryptedSecretKey, userEmail,
                _appSettings.TwoFactorAuthentication.ApplicationName, _otpHashMode, _digits, _period).ToString();

            // Build response
            return new InitTwoFactorAuthenticationResponse
            {
                OtpUriString = uriString,
                Secret = decryptedSecretKey,
                HashFunction = _hashFunction,
                Period = _period,
                Digits = _digits
            };
        }

        /// <summary>
        /// Verifies a totp code. If valid, caches it against the specified user <paramref name="uid"/> so that it cannot be used again within the same time step.
        /// </summary>
        /// <param name="uid">The user's uid.</param>
        /// <param name="totpCode">The 6-digit two-factor authentication code.</param>
        /// <param name="encryptedSecretKey">The user's totp encrypted secret key.</param>
        /// <returns></returns>
        public VerifyTotpCodeResult VerifyCode(Guid uid, string totpCode, string encryptedSecretKey)
        {
            // Decrypt secret
            string decryptedSecretKey = StringCipherAesGcm.Decrypt(encryptedSecretKey, _appSettings.TwoFactorAuthentication.SecretEncryptionKey);

            // Convert to bytes
            byte[] base32Bytes = Base32Encoding.ToBytes(decryptedSecretKey);

            Totp totp = new Totp(base32Bytes, mode: _otpHashMode);

            // Verify the token from the user
            if (totp.VerifyTotp(totpCode, out long timeStepMatched, VerificationWindow.RfcSpecifiedNetworkDelay))
            {
                string cacheKey = $"Totp|{uid}|{timeStepMatched}";
                bool? alreadyVerified = _cache.GetOrDefault<bool?>(cacheKey);

                // NOTE: We don't care about the value, we are only checking if it's in the cache or not.
                // Storing a boolean in the cache is just a placeholder since we need to provide a value of something,
                // as the cache does not have a "ContainsKey" method.
                if (alreadyVerified.HasValue)
                {
                    // The TOTP has been used before so reject it.
                    return VerifyTotpCodeResult.TotpCodeAlreadyUsed;
                }

                // Cache the user uid + timeStepMatched
                // As above - value doesn't matter, we are just storing a boolean in the cache as a placeholder,
                // since we need to provide a value of something.
                // When checking the cache we only care if the key is in there or not.
                // Code changes every 30 seconds, so we set the cache expiry time to triple - 91 seconds,
                // which allows for the 3 time windows, i.e. before/during/after.
                _cache.Set(cacheKey, true, new FusionCacheEntryOptions
                {
                    Duration = TimeSpan.FromSeconds(91)
                });

                return VerifyTotpCodeResult.Ok;
            }
            else
            {
                return VerifyTotpCodeResult.TotpCodeInvalid;
            }
        }
    }
}
