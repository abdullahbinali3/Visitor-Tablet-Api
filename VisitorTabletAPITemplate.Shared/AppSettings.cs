using VisitorTabletAPITemplate.StartupValidation;

namespace VisitorTabletAPITemplate
{
    public sealed class AppSettings : IValidatable
    {
        public required AppSettings_DataProtection DataProtection { get; init; }
        public required AppSettings_ConnectionStrings ConnectionStrings { get; init; }
        public required AppSettings_Cors Cors { get; init; }
        public required AppSettings_UserSession UserSession { get; init; }
        public required AppSettings_Jwt Jwt { get; init; }
        public required AppSettings_Password Password { get; init; }
        public required AppSettings_TwoFactorAuthentication TwoFactorAuthentication { get; init; }
        public required AppSettings_AzureAD AzureAD { get; init; }
        public required AppSettings_Cache Cache { get; init; }
        public required AppSettings_EmailNotifications EmailNotifications { get; init; }
        public required AppSettings_GoogleMaps GoogleMaps { get; init; }
        public required AppSettings_Organization Organization { get; init; }
        public required AppSettings_FileUpload FileUpload { get; init; }
        public required AppSettings_ImageUpload ImageUpload { get; init; }

        public void Validate()
        {
            // DataProtection
            if (string.IsNullOrEmpty(DataProtection?.CertificatePassword))
            {
                throw new AppSettingsException("AppSettings.DataProtection.CertificatePassword must be a string which is not null or empty (recommended 64 characters, symbols should be simple ones only).");
            }

            // ConnectionStrings
            if (string.IsNullOrEmpty(ConnectionStrings?.VisitorTablet))
            {
                throw new AppSettingsException("AppSettings.ConnectionStrings.VisitorTablet must be a string which is not null or empty.");
            }

            /*
            if (string.IsNullOrEmpty(ConnectionStrings?.DbIpLocation))
            {
                throw new AppSettingsException("AppSettings.ConnectionStrings.DbIpLocation must be a string which is not null or empty.");
            }
            */

            // Cors
            if (Cors is null || Cors.ProductionUrls is null || Cors.ProductionUrls.Count == 0)
            {
                throw new AppSettingsException("AppSettings.Cors.ProductionUrls must be a list of strings which are not null or empty.");
            }
            else
            {
                foreach (string? productionUrl in Cors.ProductionUrls)
                {
                    if (string.IsNullOrWhiteSpace(productionUrl))
                    {
                        throw new AppSettingsException("AppSettings.Cors.ProductionUrls must be a list of strings which are not null or empty.");
                    }
                }
            }

            if (Cors is null || Cors.DevelopmentUrls is null || Cors.DevelopmentUrls.Count == 0)
            {
                throw new AppSettingsException("AppSettings.Cors.DevelopmentUrls must be a list of strings which are not null or empty.");
            }
            else
            {
                foreach (string? productionUrl in Cors.DevelopmentUrls)
                {
                    if (string.IsNullOrWhiteSpace(productionUrl))
                    {
                        throw new AppSettingsException("AppSettings.Cors.DevelopmentUrls must be a list of strings which are not null or empty.");
                    }
                }
            }

            // UserSession
            if (UserSession is null || UserSession.UserSessionTimeoutMinutes <= 0)
            {
                throw new AppSettingsException("AppSettings.UserSession.UserSessionTimeoutMinutes must be an integer greater than 0.");
            }

            // Jwt
            if (string.IsNullOrEmpty(Jwt?.TokenSigningKey))
            {
                throw new AppSettingsException("AppSettings.Jwt.TokenSigningKey must be a string which is not null or empty (recommended 256 random characters).");
            }

            if (Jwt is null || Jwt.AccessTokenExpiryMinutes <= 0)
            {
                throw new AppSettingsException("AppSettings.Jwt.AccessTokenExpiryMinutes must be an integer greater than 0.");
            }

            // Password
            if (Password is null || Password.BcryptCost < 10 || Password.BcryptCost > 16)
            {
                throw new AppSettingsException("AppSettings.Password.BcryptCost must be an integer between 10 and 16 (recommended value is 10 or 11).");
            }

            if (string.IsNullOrEmpty(Password?.Pepper))
            {
                throw new AppSettingsException("AppSettings.Password.Pepper must be a string which is not null or empty (recommended 256 random characters).");
            }

            // EmailNotifications
            if (string.IsNullOrEmpty(EmailNotifications?.ApplicationName))
            {
                throw new AppSettingsException("AppSettings.EmailNotifications.ApplicationName must be a string which is not null or empty.");
            }

            if (string.IsNullOrEmpty(EmailNotifications?.FromAddress))
            {
                throw new AppSettingsException("AppSettings.EmailNotifications.FromAddress must be a string which is not null or empty.");
            }
        }
    }

    public sealed class AppSettings_DataProtection
    {
        public required string CertificateFilename { get; init; }
        public required string CertificatePassword { get; init; }
    }

    public sealed class AppSettings_ConnectionStrings
    {
        public required string VisitorTablet { get; init; }
        public required string DbIpLocation { get; init; }
    }

    public sealed class AppSettings_Cors
    {
        public required List<string> ProductionUrls { get; init; }
        public required List<string> DevelopmentUrls { get; init; }
        public string? AndroidCapacitorUrl { get; init; }
        public string? IosCapacitorUrl { get; init; }
    }

    public sealed class AppSettings_UserSession
    {
        public required int UserSessionTimeoutMinutes { get; init; }
    }

    public sealed class AppSettings_Jwt
    {
        public required string TokenSigningKey { get; init; }
        public required int AccessTokenExpiryMinutes { get; init; }
    }

    public sealed class AppSettings_Password
    {
        public required int BcryptCost { get; init; }
        public required string Pepper { get; init; }
        public required string ForgotPasswordTokenEncryptionKey { get; init; }
        public required string RegisterTokenEncryptionKey { get; init; }
        public required string LinkAccountTokenEncryptionKey { get; init; }
    }

    public sealed class AppSettings_TwoFactorAuthentication
    {
        public required string ApplicationName { get; init; }
        public required int SecretKeyLengthBytes { get; init; }
        public required string SecretEncryptionKey { get; init; }
        public required string DisableTokenEncryptionKey { get; init; }
    }

    public sealed class AppSettings_AzureAD
    {
        public required string SecretEncryptionKey { get; init; }
    }

    public sealed class AppSettings_Cache
    {
        public required string SspIis01 { get; init; }
        public required string SspIis02 { get; init; }
        public required string CacheApiKey { get; init; }
    }

    public sealed class AppSettings_EmailNotifications
    {
        public required string ApplicationName { get; init; }
        public required string FromAddress { get; init; }
    }

    public sealed class AppSettings_GoogleMaps
    {
        public required string GoogleMapsApiKey { get; init; }
    }

    public sealed class AppSettings_Organization
    {
        public required string EncryptionKeyEncryptionKey { get; init; }
    }

    public sealed class AppSettings_FileUpload
    {
        public required int MaxFilesizeBytes { get; init; }
    }

    public sealed class AppSettings_ImageUpload
    {
        public required int MaxFilesizeBytes { get; init; }
        public required AppSettings_ImageUpload_ObjectRestrictions ObjectRestrictions { get; init; }
    }

    public sealed class AppSettings_ImageUpload_ObjectRestrictions
    {
        public required AppSettings_ImageUpload_ObjectRestrictionThumbnailOnly SvgRaster { get; set; }
        public required AppSettings_ImageUpload_ObjectRestriction TinyMCE { get; init; }
        public required AppSettings_ImageUpload_ObjectRestriction OrganizationLogo { get; init; }
        public required AppSettings_ImageUpload_ObjectRestrictionWithThumbnail UserAvatar { get; init; }
        public required AppSettings_ImageUpload_ObjectRestrictionWithThumbnail FeatureImage { get; init; }
        public required AppSettings_ImageUpload_ObjectRestrictionWithThumbnail BuildingFeatureImage { get; init; }
        public required AppSettings_ImageUpload_ObjectRestrictionWithThumbnail BuildingMapImage { get; init; }
        public required AppSettings_ImageUpload_ObjectRestriction WorkplaceSubTileImage { get; init; }
        public required AppSettings_ImageUpload_ObjectRestriction WorkplaceTileImage { get; init; }
        public required AppSettings_ImageUpload_ObjectRestriction WorkplaceKeyDrawIcon { get; init; }
        public required AppSettings_ImageUpload_ObjectRestriction AssetTypeIcon { get; init; }
    }

    public sealed class AppSettings_ImageUpload_ObjectRestriction
    {
        public required int MaxImageWidth { get; set; }
        public required int MaxImageHeight { get; set; }
    }

    public sealed class AppSettings_ImageUpload_ObjectRestrictionWithThumbnail
    {
        public required int MaxImageWidth { get; set; }
        public required int MaxImageHeight { get; set; }
        public required int ThumbnailMaxImageWidth { get; set; }
        public required int ThumbnailMaxImageHeight { get; set; }
    }

    public sealed class AppSettings_ImageUpload_ObjectRestrictionThumbnailOnly
    {
        public required int ThumbnailMaxImageWidth { get; set; }
        public required int ThumbnailMaxImageHeight { get; set; }
    }
}
