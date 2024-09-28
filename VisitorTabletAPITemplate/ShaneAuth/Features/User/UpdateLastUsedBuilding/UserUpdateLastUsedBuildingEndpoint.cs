using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;
using System.Text.Json;


namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateLastUsedBuilding
{
    public sealed class UserUpdateLastUsedBuildingEndpoint : Endpoint<UserUpdateLastUsedBuildingRequest>
    {
        private readonly UserLastUsedBuildingRepository _userOrganizationsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public UserUpdateLastUsedBuildingEndpoint(AppSettings appSettings,
            UserLastUsedBuildingRepository userOrganizationsRepository,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _userOrganizationsRepository = userOrganizationsRepository;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/user/updateLastUsedBuilding");
            SerializerContext(UserUpdateLastUsedBuildingContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UserUpdateLastUsedBuildingRequest req, CancellationToken ct)
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
            SqlQueryResult queryResult;

            if (req.ClientPortalType!.Value == (int)ClientPortalType.Web)
            {
                queryResult = await _userOrganizationsRepository.UpdateUserWebLastUsedBuildingAsync(userId.Value, req.OrganizationId!.Value, req.BuildingId!.Value, userId, adminUserDisplayName, remoteIpAddress);
            }
            else if (req.ClientPortalType!.Value == (int)ClientPortalType.Mobile)
            {
                queryResult = await _userOrganizationsRepository.UpdateUserMobileLastUsedBuildingAsync(userId.Value, req.OrganizationId!.Value, req.BuildingId!.Value, userId, adminUserDisplayName, remoteIpAddress);
            }
            else
            {
                queryResult = SqlQueryResult.UnknownError;
            }

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

        private async Task ValidateInputAsync(UserUpdateLastUsedBuildingRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService))
            {
                return;
            }

            // Validate user has minimum required access to building
            if (!await this.ValidateUserBuildingAsync(req.OrganizationId, req.BuildingId, userId, _authCacheService))
            {
                return;
            }

            // Validate ClientPortalType
            if (!req.ClientPortalType.HasValue)
            {
                AddError(m => m.ClientPortalType!, "Client Portal Type is required.", "error.profile.clientPortalTypeIsRequired");
            }
            else if (!EnumParser.TryParseEnum(req.ClientPortalType.Value, out ClientPortalType? parsedClientPortalType))
            {
                AddError(m => m.ClientPortalType!, "Client Portal Type is invalid.", "error.profile.clientPortalTypeIsInvalid");
            }
            else if (parsedClientPortalType != ClientPortalType.Mobile && parsedClientPortalType != ClientPortalType.Web)
            {
                AddError(m => m.ClientPortalType!, "Client Portal Type is invalid.", "error.profile.clientPortalTypeIsInvalid");

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
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData!, UserUpdateLastUsedBuildingContext.Default.UserData));
                    AddError("Your note could not be updated as you do not have permission to access the selected organization.", "error.profile.noteInvalidOrg");
                    return;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
