using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RevokeDisable2fa
{
    public sealed class AuthRevokeDisable2faEndpoint : Endpoint<AuthRevokeDisable2faRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthRevokeDisable2faEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/twoFactorAuthentication/revokeDisable");
            SerializerContext(AuthRevokeDisable2faContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthRevokeDisable2faRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Revoke all current disable two-factor authentication tokens
            UserDisableTotpResult result = await _usersRepository.RevokeDisableTwoFactorAuthenticationAsync(req.Uid!.Value, req.Token!.Value, remoteIpAddress);

            // Validate result
            ValidateOutput(result);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        public void ValidateInput(AuthRevokeDisable2faRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.profile.twoFactorAuthentication.uidIsRequired");
            }

            // Validate Token
            if (!req.Token.HasValue)
            {
                AddError(m => m.Token!, "Token is required.", "error.profile.twoFactorAuthentication.tokenIsRequired");
            }
        }

        private void ValidateOutput(UserDisableTotpResult queryResult)
        {
            switch (queryResult)
            {
                case UserDisableTotpResult.Ok:
                    return;
                case UserDisableTotpResult.TotpNotEnabled:
                case UserDisableTotpResult.UserInvalid:
                case UserDisableTotpResult.DisableTotpTokenInvalid:
                    AddError("The confirmation link you followed was either invalid or expired.", "error.profile.twoFactorAuthentication.disableTotpTokenInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
