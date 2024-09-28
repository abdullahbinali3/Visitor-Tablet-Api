using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Security.Cryptography;

namespace VisitorTabletAPITemplate.ShaneAuth.Services
{
    /// <summary>
    /// Based on: https://fast-endpoints.com/docs/security#step-2-token-service
    /// </summary>
    public sealed class AuthJwtTokenService : RefreshTokenService<TokenRequest, TokenResponse>
    {
        private readonly UsersRepository _usersRepository;
        private readonly RefreshTokensRepository _refreshTokensRepository;

        public AuthJwtTokenService(AppSettings appSettings,
            UsersRepository usersRepository,
            RefreshTokensRepository refreshTokensRepository)
        {
            _usersRepository = usersRepository;
            _refreshTokensRepository = refreshTokensRepository;

            Setup(o =>
            {
                o.TokenSigningKey = appSettings.Jwt.TokenSigningKey;
                o.AccessTokenValidity = TimeSpan.FromMinutes(appSettings.Jwt.AccessTokenExpiryMinutes);
                o.RefreshTokenValidity = TimeSpan.FromMinutes(appSettings.UserSession.UserSessionTimeoutMinutes);

                o.Endpoint("/auth/refreshToken", ep =>
                {
                    ep.Tags("IgnoreAntiforgeryToken");
                    ep.Summary(s => s.Summary = "This is the refresh token endpoint.");
                });
            });
        }

        public override async Task PersistTokenAsync(TokenResponse response)
        {
            // this method will be called whenever a new access/refresh token pair is being generated.
            // store the tokens and expiry dates however you wish for the purpose of verifying
            // future refresh requests.

            // Override refresh token with a more secure one, as FastEndpoints
            // internally just uses Guid.NewGuid().ToString("N") as a RefreshToken.
            byte[] refreshTokenOverride = RandomNumberGenerator.GetBytes(64);
            response.RefreshToken = Convert.ToHexString(refreshTokenOverride);

            // Store the token in the database so it can be used for verifying later.
            await _refreshTokensRepository.StoreRefreshToken(Guid.Parse(response.UserId), refreshTokenOverride, response.RefreshExpiry);
        }

        public override async Task RefreshRequestValidationAsync(TokenRequest req)
        {
            // validate the incoming refresh request by checking the token and expiry against the
            // previously stored data. if the token is not valid and a new token pair should
            // not be created, simply add validation errors using the AddError() method.
            // the failures you add will be sent to the requesting client. if no failures are added,
            // validation passes and a new token pair will be created and sent to the client.

            Guid uid = Guid.Parse(req.UserId);
            byte[] refreshToken = Convert.FromHexString(req.RefreshToken);

            if (!await _refreshTokensRepository.ValidateAndConsumeRefreshToken(uid, refreshToken))
            {
                AddError(r => r.RefreshToken, "Refresh token is invalid.", "error.auth.refreshTokenIsInvalid");
            }
        }

        public override async Task SetRenewalPrivilegesAsync(TokenRequest request, UserPrivileges privileges)
        {
            // specify the user privileges to be embedded in the jwt when a refresh request is
            // received and validation has passed. this only applies to renewal/refresh requests
            // received to the refresh endpoint and not the initial jwt creation.

            UserData? userData = await _usersRepository.GetUserByUidAsync(Guid.Parse(request.UserId));

            if (userData is not null)
            {
                ShaneAuthHelpers.PopulateUserPrivileges(privileges, userData);
            }
        }
    }
}
