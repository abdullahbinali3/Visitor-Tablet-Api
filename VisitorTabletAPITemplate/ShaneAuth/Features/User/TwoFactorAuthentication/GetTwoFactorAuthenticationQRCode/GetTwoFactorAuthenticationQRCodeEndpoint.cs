using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.TwoFactorAuthentication.GetTwoFactorAuthenticationQRCode
{
    public sealed class GetTwoFactorAuthenticationQRCodeEndpoint : EndpointWithoutRequest
    {
        private readonly UsersRepository _usersRepository;

        public GetTwoFactorAuthenticationQRCodeEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Get("/user/twoFactorAuthentication/qrCode");
            SerializerContext(GetTwoFactorAuthenticationQRCodeContext.Default);
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
            (UserEnableTotpResult totpResult, byte[]? pngImage) = await _usersRepository.GetTwoFactorAuthenticationQRCodePngAsync(userId!.Value, email!);

            // Validate result
            ValidateOutput(totpResult, pngImage);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendBytesAsync(pngImage!, $"TwoFactorAuthentifcationQRCode_{userId.Value}.png", contentType: "image/png");
        }

        private void ValidateOutput(UserEnableTotpResult totpResult, byte[]? pngImage)
        {
            // Validate queried data
            switch (totpResult)
            {
                case UserEnableTotpResult.Ok:
                    if (pngImage is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case UserEnableTotpResult.TotpAlreadyEnabled:
                    AddError("Two-factor authentication is already enabled on your account.", "error.profile.twoFactorAuthentication.totpAlreadyEnabled");
                    break;
                case UserEnableTotpResult.TotpSecretNotSet:
                    AddError("Two-factor authentication has not been correctly initialized in your account. Please set up two-factor authentication from the beginning and scan the generated QR code.", "error.profile.twoFactorAuthentication.totpSecretNotSet");
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
