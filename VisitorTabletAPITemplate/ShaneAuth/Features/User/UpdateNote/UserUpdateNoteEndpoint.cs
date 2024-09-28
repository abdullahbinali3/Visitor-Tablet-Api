using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateNote
{
    public sealed class UserUpdateNoteEndpoint : Endpoint<UserUpdateNoteRequest>
    {
        private readonly UserOrganizationsRepository _userOrganizationsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public UserUpdateNoteEndpoint(AppSettings appSettings,
            UserOrganizationsRepository userOrganizationsRepository,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _userOrganizationsRepository = userOrganizationsRepository;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/user/updateNote");
            SerializerContext(UserUpdateNoteContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UserUpdateNoteRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            SqlQueryResult queryResult = await _userOrganizationsRepository.UpdateUserOrganizationNoteAsync(userId.Value, req.OrganizationId!.Value, req.Note, userId, adminUserDisplayName, remoteIpAddress);
            UserData? userData = await _usersRepository.GetUserByUidAsync(userId.Value);

            // Validate result
            ValidateOutput(queryResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // For master users, also populate MasterInfo
            if (userData!.UserSystemRole == UserSystemRole.Master)
            {
                userData.ExtendedData.MasterInfo = await _usersRepository.GetMasterInfoAsync(ct);
            }

            await SendOkAsync(userData!);
        }

        private async Task ValidateInputAsync(UserUpdateNoteRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService))
            {
                return;
            }

            // Trim strings
            req.Note = req.Note?.Trim();

            // Validate Note
            if (req.Note == "")
            {
                // Set note to null if empty
                req.Note = null;
            }
            else if (!string.IsNullOrEmpty(req.Note) && req.Note.Length > 500)
            {
                AddError(m => m.Note!, "Note must be 500 characters or less.", "error.profile.noteLength|{\"length\":\"500\"}");
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
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, UserUpdateNoteContext.Default.UserData));
                    AddError("Your note could not be updated as you do not have permission to access the selected organization.", "error.profile.noteInvalidOrg");
                    return;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
