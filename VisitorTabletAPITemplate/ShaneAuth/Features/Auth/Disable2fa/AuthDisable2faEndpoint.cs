using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.Disable2fa
{
    public sealed class AuthDisable2faEndpoint : Endpoint<AuthDisable2faRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthDisable2faEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/twoFactorAuthentication/disable");
            SerializerContext(AuthDisable2faContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthDisable2faRequest req, CancellationToken ct)
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

            // Disable two-factor authentication
            UserDisableTotpResult result = await _usersRepository.DisableTwoFactorAuthenticationAsync(req.Uid!.Value, req.Token!, remoteIpAddress);

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

        public void ValidateInput(AuthDisable2faRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.profile.twoFactorAuthentication.uidIsRequired");
            }

            // Validate Uid
            if (string.IsNullOrEmpty(req.Token))
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
