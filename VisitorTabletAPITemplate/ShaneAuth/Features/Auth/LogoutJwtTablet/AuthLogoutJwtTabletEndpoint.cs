using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.LogoutJwtTablet
{
    public sealed class AuthLogoutJwtTabletEndpoint : Endpoint<LogoutJwtTabletRequest>
    {
        private readonly RefreshTokensRepository _refreshTokensRepository;
        private readonly UsersRepository _usersRepository;

        public AuthLogoutJwtTabletEndpoint(RefreshTokensRepository refreshTokensRepository,
            UsersRepository usersRepository)
        {
            _refreshTokensRepository = refreshTokensRepository;
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/auth/logoutJwtTablet");
            SerializerContext(LogoutJwtTabletContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(LogoutJwtTabletRequest req, CancellationToken ct)
        {
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            string? userEmail = User.GetEmail();

            // Validate request
            ValidateInput(req, userEmail);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Check user credentials
            (VerifyCredentialsResult result, _) = await _usersRepository.VerifyCredentialsAndGetUserAsync(req.Email!, req.Password!, req.TotpCode, "Tablet", ct);

            // Validate result
            ValidateOutput(result);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Clear all refresh tokens for user
            await _refreshTokensRepository.ClearRefreshTokens(userId.Value);

            // Delete XSRF-TOKEN cookie
            HttpContext.Response.Cookies.Delete("XSRF-TOKEN");

            await SendNoContentAsync();
        }

        public void ValidateInput(LogoutJwtTabletRequest req, string? userEmail)
        {
            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();

            // Validate input

            // Validate Email
            if (string.IsNullOrEmpty(req.Email))
            {
                AddError(m => m.Email!, "Email is required.", "error.logout.emailIsRequired");
            }
            else if (!req.Email.Equals(userEmail, StringComparison.OrdinalIgnoreCase))
            {
                AddError(m => m.Email!, "Email belongs to a different user.", "error.logout.emailBelongsToDifferentUser");
            }

            // Validate Password
            if (string.IsNullOrEmpty(req.Password))
            {
                AddError(m => m.Password!, "Password is required.", "error.logout.passwordIsRequired");
            }
        }

        public void ValidateOutput(VerifyCredentialsResult result)
        {
            switch (result)
            {
                case VerifyCredentialsResult.Ok:
                    return;
                case VerifyCredentialsResult.UserDidNotExist:
                case VerifyCredentialsResult.PasswordInvalid:
                    AddError("Invalid email or password.", "error.login.invalidEmailOrPassword");
                    break;
                case VerifyCredentialsResult.NoAccess:
                    AddError("Your account does not have access to this system.", "error.login.accountHasNoAccess");
                    break;
                case VerifyCredentialsResult.PasswordNotSet:
                    AddError("Your account does not have a password set. Please login using Single Sign On instead.", "error.login.accountNoPasswordSet");
                    break;
                case VerifyCredentialsResult.PasswordLoginLockedOut:
                    AddError("Your account has been locked out after too many failed login attempts. Please wait a few minutes before trying again.", "error.login.passwordLoginLockedOut");
                    break;
                case VerifyCredentialsResult.TotpCodeRequired:
                    AddError(m => m.TotpCode!, "Two-factor authentication code is required.", "error.login.totpCodeRequired");
                    break;
                case VerifyCredentialsResult.TotpLockedOut:
                    AddError("Your account has been locked out after too many failed two-factor authentication code attempts. Please wait a few minutes before trying again.", "error.login.totpLockedOut");
                    break;
                case VerifyCredentialsResult.TotpCodeInvalid:
                    AddError(m => m.TotpCode!, "The two-factor authentication code was invalid.", "error.login.totpCodeInvalid");
                    break;
                case VerifyCredentialsResult.TotpCodeAlreadyUsed:
                    AddError(m => m.TotpCode!, "Successful login has already been made with the specified two-factor authentication code. Please wait until a new code has been generated in your authenticator app before logging in again.", "error.login.totpCodeAlreadyUsed");
                    break;
                case VerifyCredentialsResult.UnknownError:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
