using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitDisableTwoFactorAuthentication
{
    public sealed class InitDisableTwoFactorAuthenticationEndpoint : Endpoint<InitDisableTwoFactorAuthenticationRequest>
    {
        private readonly UsersRepository _usersRepository;

        public InitDisableTwoFactorAuthenticationEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/user/twoFactorAuthentication/initDisable");
            SerializerContext(InitDisableTwoFactorAuthenticationContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(InitDisableTwoFactorAuthenticationRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

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
            UserDisableTotpResult queryResult = await _usersRepository.InitDisableTwoFactorAuthenticationAsync(userId!.Value, req, adminUserDisplayName, remoteIpAddress);

            // Get updated user data
            UserData? userData = await _usersRepository.GetUserByUidAsync(userId!.Value);

            // Validate result
            ValidateOutput(queryResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendNoContentAsync();
        }

        private static void ValidateInput(InitDisableTwoFactorAuthenticationRequest req)
        {
            // Trim strings
            req.UserAgentBrowserName = req.UserAgentBrowserName?.Trim();
            req.UserAgentOsName = req.UserAgentOsName?.Trim();
            req.UserAgentDeviceInfo = req.UserAgentDeviceInfo?.Trim();

            // Validate input

            // Validate Password
            // NOTE: We don't validate Password here because the user's account may not have a password set.
            // Instead this is validated inside InitDisableTwoFactorAuthentication() after we know whether
            // the user has a password or not.
        }

        private void ValidateOutput(UserDisableTotpResult queryResult, UserData? userData)
        {
            switch (queryResult)
            {
                case UserDisableTotpResult.Ok:
                    return;
                case UserDisableTotpResult.UserInvalid:
                    HttpContext.Items.Add("FatalError", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, InitDisableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("Your account does not have access to this system.", "error.accountHasNoAccess");
                    break;
                case UserDisableTotpResult.PasswordInvalid:
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, InitDisableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("Password is invalid.", "error.profile.twoFactorAuthentication.passwordIsInvalid");
                    break;
                case UserDisableTotpResult.TotpNotEnabled:
                    HttpContext.Items.Add("FatalError", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, InitDisableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("Two-factor authentication is not currently enabled on your account.", "error.profile.twoFactorAuthentication.totpNotEnabled");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
