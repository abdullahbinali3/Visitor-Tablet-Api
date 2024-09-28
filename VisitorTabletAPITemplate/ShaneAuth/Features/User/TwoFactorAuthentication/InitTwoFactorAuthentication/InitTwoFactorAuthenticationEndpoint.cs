using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.InitTwoFactorAuthentication
{
    public sealed class InitTwoFactorAuthenticationEndpoint : EndpointWithoutRequest
    {
        private readonly UsersRepository _usersRepository;

        public InitTwoFactorAuthenticationEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/user/twoFactorAuthentication/init");
            SerializerContext(InitTwoFactorAuthenticationContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(CancellationToken ct)
        {
            // Get logged in user's details
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            string? email = User.GetEmail();

            // Query data
            (UserEnableTotpResult init2faResult, InitTwoFactorAuthenticationResponse? response) = await _usersRepository.InitTwoFactorAuthenticationAsync(userId!.Value, email!);

            // Validate result
            ValidateOutput(init2faResult, response);

            // If 2fa already enabled, get updated user data.
            if (init2faResult == UserEnableTotpResult.TotpAlreadyEnabled)
            {
                UserData? userData = await _usersRepository.GetUserByUidAsync(userId!.Value);
                HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, InitTwoFactorAuthenticationContext.Default.UserData));
            }

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(response!);
        }

        private void ValidateOutput(UserEnableTotpResult init2faResult, InitTwoFactorAuthenticationResponse? response)
        {
            // Validate queried data
            switch (init2faResult)
            {
                case UserEnableTotpResult.Ok:
                    if (response is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case UserEnableTotpResult.TotpAlreadyEnabled:
                    AddError("Two-factor authentication is already enabled on your account.", "error.profile.twoFactorAuthentication.totpAlreadyEnabled");
                    break;
                case UserEnableTotpResult.UserInvalid:
                    AddError("Your account does not have access to this system.", "error.accountHasNoAccess");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
