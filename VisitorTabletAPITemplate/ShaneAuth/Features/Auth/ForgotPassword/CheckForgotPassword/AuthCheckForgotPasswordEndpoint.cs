using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.CheckForgotPassword
{
    public sealed class AuthCheckForgotPasswordEndpoint : Endpoint<AuthCheckForgotPasswordRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthCheckForgotPasswordEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/forgotPassword/check");
            SerializerContext(AuthCheckForgotPasswordContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthCheckForgotPasswordRequest req, CancellationToken ct)
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
            UserForgotPasswordResult result = await _usersRepository.CheckForgotPasswordTokenAsync(req.Uid!.Value, req.Token!, remoteIpAddress);

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

        public void ValidateInput(AuthCheckForgotPasswordRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError("The forgot password link you followed was either invalid or expired.", "error.forgotPassword.forgotPasswordTokenInvalid");
            }

            // Validate Uid
            if (string.IsNullOrEmpty(req.Token))
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
