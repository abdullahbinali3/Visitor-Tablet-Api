using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.EnableTwoFactorAuthentication
{
    public sealed class EnableTwoFactorAuthenticationEndpoint : Endpoint<EnableTwoFactorAuthenticationRequest>
    {
        private readonly UsersRepository _usersRepository;

        public EnableTwoFactorAuthenticationEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/user/twoFactorAuthentication/enable");
            SerializerContext(EnableTwoFactorAuthenticationContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(EnableTwoFactorAuthenticationRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            Guid? userId = User.GetId();

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
            UserEnableTotpResult queryResult = await _usersRepository.EnableTwoFactorAuthenticationAsync(userId!.Value, req.TotpCode!, remoteIpAddress);

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

            await SendOkAsync(userData!);
        }

        private void ValidateInput(EnableTwoFactorAuthenticationRequest req)
        {
            // Trim strings
            if (req.TotpCode is not null)
            {
                req.TotpCode = GeneratedRegexes.ContainsNonDigits().Replace(req.TotpCode, "");
            }

            // Validate input

            // Validate TotpCode
            if (string.IsNullOrEmpty(req.TotpCode))
            {
                AddError(m => m.TotpCode!, "6-Digit Code is required.", "error.profile.twoFactorAuthentication.totpCodeIsRequired|{\"length\":\"6\"}");
            }
            else if (req.TotpCode.Length != 6 || GeneratedRegexes.ContainsNonDigits().IsMatch(req.TotpCode)) // Check for non-digits
            {
                AddError(m => m.TotpCode!, "6-Digit Code should be a 6-digit number containing only numbers without spaces, dashes, etc.",
                    "error.profile.twoFactorAuthentication.totpLength|{\"length\":\"6\"}");
            }
        }

        private void ValidateOutput(UserEnableTotpResult queryResult, UserData? userData)
        {
            switch (queryResult)
            {
                case UserEnableTotpResult.Ok:
                    return;
                case UserEnableTotpResult.UserInvalid:
                    HttpContext.Items.Add("FatalError", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, EnableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("Your account does not have access to this system.", "error.accountHasNoAccess");
                    break;
                case UserEnableTotpResult.TotpSecretNotSet:
                    HttpContext.Items.Add("FatalError", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, EnableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("Two-factor authentication has not been correctly initialized in your account. Please set up two-factor authentication from the beginning and scan the generated QR code.", "error.profile.twoFactorAuthentication.totpSecretNotSet");
                    break;
                case UserEnableTotpResult.TotpCodeInvalid:
                    AddError("The two-factor authentication code was invalid.", "error.profile.twoFactorAuthentication.totpCodeIsInvalid");
                    break;
                case UserEnableTotpResult.TotpAlreadyEnabled:
                    HttpContext.Items.Add("FatalError", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, EnableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("Two-factor authentication is already enabled on your account.", "error.profile.twoFactorAuthentication.totpAlreadyEnabled");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
