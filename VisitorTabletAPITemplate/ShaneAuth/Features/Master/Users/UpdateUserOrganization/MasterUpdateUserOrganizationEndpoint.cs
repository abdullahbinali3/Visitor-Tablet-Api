using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUserOrganization
{
    public sealed class MasterUpdateUserOrganizationEndpoint : Endpoint<MasterUpdateUserOrganizationRequest>
    {
        private readonly UserOrganizationsRepository _userOrganizationsRepository;
        private readonly UsersRepository _usersRepository;

        public MasterUpdateUserOrganizationEndpoint(UserOrganizationsRepository userOrganizationsRepository,
            UsersRepository usersRepository)
        {
            _userOrganizationsRepository = userOrganizationsRepository;
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/master/users/updateOrganization");
            SerializerContext(MasterUpdateUserOrganizationContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterUpdateUserOrganizationRequest req, CancellationToken ct)
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
            (UserManagementResult queryResult, UserData? userData) = await _userOrganizationsRepository.MasterUpdateUserOrganizationAsync(req, userId, adminUserDisplayName, remoteIpAddress);

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

        private void ValidateInput(MasterUpdateUserOrganizationRequest req)
        {
            // Trim strings
            req.Note = req.Note?.Trim();

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }

            // Validate UserOrganizationRole
            if (!req.UserOrganizationRole.HasValue)
            {
                AddError(m => m.UserOrganizationRole!, "User Organization Role is required.", "error.masterSettings.users.userOrganizationRoleIsRequired");
            }
            else if (!EnumParser.IsValidEnum(req.UserOrganizationRole.Value))
            {
                AddError(m => m.UserOrganizationRole!, "User Organization Role is invalid.", "error.masterSettings.users.userOrganizationRoleIsInvalid");
            }

            // Set note to null if empty
            if (string.IsNullOrEmpty(req.Note))
            {
                req.Note = null;
            }

            // Validate Contractor
            if (!req.Contractor.HasValue)
            {
                AddError(m => m.Contractor!, "Contractor is required.", "error.masterSettings.users.contractorIsRequired");
            }

            // Validate Visitor
            if (!req.Visitor.HasValue)
            {
                AddError(m => m.Visitor!, "Visitor is required.", "error.masterSettings.users.visitorIsRequired");
            }

            // Validate UserOrganizationDisabled
            if (!req.UserOrganizationDisabled.HasValue)
            {
                AddError(m => m.UserOrganizationDisabled!, "User Organization Disabled is required.", "error.masterSettings.users.userOrganizationDisabledIsRequired");
            }
        }

        private void ValidateOutput(UserManagementResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case UserManagementResult.Ok:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case UserManagementResult.UserDidNotExist:
                    HttpContext.Items.TryAdd("FatalError", true);
                    AddError(m => m.Uid!, "The user was deleted since you last accessed this page.", "error.masterSettings.users.deletedSinceAccessedPage");
                    return;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
