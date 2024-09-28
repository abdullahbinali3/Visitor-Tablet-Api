using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateNote
{
    public sealed class MasterUserUpdateNoteEndpoint : Endpoint<MasterUserUpdateNoteRequest>
    {
        private readonly UserOrganizationsRepository _userOrganizationsRepository;
        private readonly UsersRepository _usersRepository;

        public MasterUserUpdateNoteEndpoint(UserOrganizationsRepository userOrganizationsRepository,
            UsersRepository usersRepository)
        {
            _userOrganizationsRepository = userOrganizationsRepository;
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/master/users/updateNote");
            SerializerContext(MasterUserUpdateNoteContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterUserUpdateNoteRequest req, CancellationToken ct)
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
            SqlQueryResult queryResult = await _userOrganizationsRepository.UpdateUserOrganizationNoteAsync(req.Uid!.Value, req.OrganizationId!.Value, req.Note, userId, adminUserDisplayName, remoteIpAddress);

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

        private void ValidateInput(MasterUserUpdateNoteRequest req)
        {
            // Trim strings
            req.Note = req.Note?.Trim();

            // Set note to null if empty
            if (string.IsNullOrEmpty(req.Note))
            {
                req.Note = null;
            }

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }

            // Validate OrganizationId
            if (!req.OrganizationId.HasValue)
            {
                AddError(m => m.OrganizationId!, "Organization Id is required.", "error.organizationIdIsRequired");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.InsufficientPermissions:
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, MasterUserUpdateNoteContext.Default.UserData));
                    AddError("The note could not be updated because the user has not been assigned to the selected organization.", "error.user.noteInvalidOrg");
                    return;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
