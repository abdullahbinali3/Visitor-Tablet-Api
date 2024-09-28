using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.CheckForgotPassword;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.CompleteForgotPassword
{
    public sealed class AuthCompleteForgotPasswordEndpoint : Endpoint<AuthCompleteForgotPasswordRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthCompleteForgotPasswordEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/forgotPassword/complete");
            SerializerContext(AuthCompleteForgotPasswordContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthCompleteForgotPasswordRequest req, CancellationToken ct)
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

            // Complete forgot password process
            UserForgotPasswordResult result = await _usersRepository.CompleteForgotPasswordAsync(req.Uid!.Value, req.Token!, req.NewPassword!, remoteIpAddress);

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

        public void ValidateInput(AuthCompleteForgotPasswordRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.forgotPassword.uidIsRequired");
            }

            // Validate Uid
            if (string.IsNullOrEmpty(req.Token))
            {
                AddError(m => m.Token!, "Token is required.", "error.forgotPassword.tokenIsRequired");
            }

            // Validate NewPassword
            if (string.IsNullOrEmpty(req.NewPassword))
            {
                AddError(m => m.NewPassword!, "New Password is required.", "error.forgotPassword.newPasswordIsRequired");
            }
            else if (req.NewPassword.Length < 10)
            {
                AddError(m => m.NewPassword!, "New Password must be at least 10 characters long.", "error.forgotPassword.newPasswordLength|{\"length\":\"10\"}");
            }

            // Validate NewPasswordConfirm
            if (string.IsNullOrEmpty(req.NewPasswordConfirm))
            {
                AddError(m => m.NewPasswordConfirm!, "New Password (Confirm) is required.", "error.forgotPassword.newPasswordConfirmIsRequired");
            }
            else if (req.NewPasswordConfirm.Length < 10)
            {
                AddError(m => m.NewPasswordConfirm!, "New Password (Confirm) must be at least 10 characters long.", "error.forgotPassword.newPasswordConfirmLength|{\"length\":\"10\"}");
            }
            else if (req.NewPassword != req.NewPasswordConfirm)
            {
                AddError(m => m.NewPasswordConfirm!, "New Password and New Password (Confirm) must match.", "error.forgotPassword.newPasswordConfirmMismatch");
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
