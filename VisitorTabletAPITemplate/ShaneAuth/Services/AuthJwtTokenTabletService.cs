namespace VisitorTabletAPITemplate.ShaneAuth.Services
{
    /// <summary>
    /// Based on: https://fast-endpoints.com/docs/security#step-2-token-service
    /// </summary>
    public sealed class AuthJwtTokenTabletService : RefreshTokenService<TokenRequest, TokenResponse>
    {
        public AuthJwtTokenTabletService(AppSettings appSettings)
        {
            Setup(o =>
            {
                o.TokenSigningKey = appSettings.Jwt.TokenSigningKey;
                o.AccessTokenValidity = TimeSpan.FromDays(365 * 10); // 10 years
                o.RefreshTokenValidity = TimeSpan.FromDays(365 * 10); // 10 years
            });
        }

        public override Task PersistTokenAsync(TokenResponse response)
        {
            // No refresh tokens for tablet logins
            response.RefreshToken = "";

            return Task.CompletedTask;
        }

        public override Task RefreshRequestValidationAsync(TokenRequest req)
        {
            return Task.CompletedTask;
        }

        public override Task SetRenewalPrivilegesAsync(TokenRequest request, UserPrivileges privileges)
        {
            return Task.CompletedTask;
        }
    }
}
