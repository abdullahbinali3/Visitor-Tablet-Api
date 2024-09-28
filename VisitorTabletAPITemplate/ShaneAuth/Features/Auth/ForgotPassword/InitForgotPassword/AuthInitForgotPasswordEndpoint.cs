using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.ForgotPassword.InitForgotPassword
{
    public sealed class AuthInitForgotPasswordEndpoint : Endpoint<AuthInitForgotPasswordRequest>
    {
        private readonly UsersRepository _usersRepository;

        public AuthInitForgotPasswordEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/forgotPassword/init");
            SerializerContext(AuthInitForgotPasswordContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthInitForgotPasswordRequest req, CancellationToken ct)
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

            // Query data
            UserForgotPasswordResult forgotPasswordResult = await _usersRepository.InitForgotPasswordAsync(req.Email!, req, remoteIpAddress);

            // Validate result
            ValidateOutput(forgotPasswordResult);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        public void ValidateInput(AuthInitForgotPasswordRequest req)
        {
            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();

            // Validate input

            // Validate Email
            if (string.IsNullOrEmpty(req.Email))
            {
                AddError(m => m.Email!, "Email is required.", "error.login.emailIsRequired");
            }
            else if (req.Email.Length > 254)
            {
                AddError(m => m.Email!, "Email must be 254 characters or less.", "error.login.emailLength|{\"length\":\"254\"}");
            }
            else if (!Toolbox.IsValidEmail(req.Email))
            {
                AddError(m => m.Email!, "Email format is invalid.", "error.login.emailIsInvalidFormat");
            }
        }

        private void ValidateOutput(UserForgotPasswordResult forgotPasswordResult)
        {
            // Validate queried data
            switch (forgotPasswordResult)
            {
                case UserForgotPasswordResult.Ok:
                    return;
                case UserForgotPasswordResult.UserDidNotExist:
                    AddError("A user account does not exist with the specified email address.", "error.forgotPassword.userDidNotExist");
                    break;
                case UserForgotPasswordResult.NoAccess:
                    AddError("Your account does not have access to this system.", "error.login.accountHasNoAccess");
                    break;
                case UserForgotPasswordResult.LocalLoginDisabled:
                    AddError("Local login has been disabled for your organization. Please log in using Single Sign On instead.", "error.forgotPassword.localLoginDisabled");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
