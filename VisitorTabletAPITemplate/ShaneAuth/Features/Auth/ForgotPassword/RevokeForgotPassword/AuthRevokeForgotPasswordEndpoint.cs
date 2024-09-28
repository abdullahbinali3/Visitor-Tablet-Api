using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.Auth.RevokeDisable2fa;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.RevokeForgotPassword
{
    public sealed class AuthRevokeForgotPasswordEndpoint : Endpoint<AuthRevokeForgotPasswordRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthRevokeForgotPasswordEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/forgotPassword/revoke");
            SerializerContext(AuthRevokeForgotPasswordContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthRevokeForgotPasswordRequest req, CancellationToken ct)
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

            // Revoke all current forgot password tokens
            UserForgotPasswordResult result = await _usersRepository.RevokeForgotPasswordAsync(req.Uid!.Value, req.Token!.Value, remoteIpAddress);

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

        public void ValidateInput(AuthRevokeForgotPasswordRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError("The forgot password link you followed was either invalid or expired.", "error.forgotPassword.forgotPasswordTokenInvalid");
            }

            // Validate Token
            if (!req.Token.HasValue)
            {
                AddError("The forgot password link you followed was either invalid or expired.", "error.forgotPassword.forgotPasswordTokenInvalid");
            }
        }

        private void ValidateOutput(UserForgotPasswordResult queryResult)
        {
            switch (queryResult)
            {
                case UserForgotPasswordResult.Ok:
                    return;
                case UserForgotPasswordResult.UserDidNotExist:
                case UserForgotPasswordResult.NoAccess:
                case UserForgotPasswordResult.ForgotPasswordTokenInvalid:
                    AddError("The forgot password link you followed was either invalid or expired.", "error.forgotPassword.forgotPasswordTokenInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
