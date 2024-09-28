using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.DisableTwoFactorAuthentication
{
    public sealed class MasterDisableTwoFactorAuthenticationEndpoint : Endpoint<MasterDisableTwoFactorAuthenticationRequest>
    {
        private readonly UsersRepository _usersRepository;

        public MasterDisableTwoFactorAuthenticationEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/master/users/twoFactorAuthentication/disable");
            SerializerContext(MasterDisableTwoFactorAuthenticationContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterDisableTwoFactorAuthenticationRequest req, CancellationToken ct)
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
            UserDisableTotpResult queryResult = await _usersRepository.MasterDisableTwoFactorAuthenticationAsync(req.Uid!.Value, userId!.Value, adminUserDisplayName, remoteIpAddress);

            // Get updated user data
            UserData? userData = await _usersRepository.GetUserByUidAsync(req.Uid!.Value);

            // Validate result
            ValidateOutput(queryResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // If user is the logged in user and is a master user, also populate MasterInfo
            if (req.Uid == userId && userData!.UserSystemRole == UserSystemRole.Master)
            {
                userData.ExtendedData.MasterInfo = await _usersRepository.GetMasterInfoAsync(ct);
            }

            await SendOkAsync(userData!);
        }

        private void ValidateInput(MasterDisableTwoFactorAuthenticationRequest req)
        {
            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }
        }

        private void ValidateOutput(UserDisableTotpResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case UserDisableTotpResult.Ok:
                    return;
                case UserDisableTotpResult.UserInvalid:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The selected user did not exist.", "error.user.didNotExist");
                    return;
                case UserDisableTotpResult.TotpNotEnabled:
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, MasterDisableTwoFactorAuthenticationContext.Default.UserData));
                    AddError("The selected user does not have two-factor authentication enabled.", "error.user.twoFactorAuthenticationIsNotEnabled");
                    return;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
