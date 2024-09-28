using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromBuilding
{
    public sealed class MasterRemoveUserFromBuildingEndpoint : Endpoint<MasterRemoveUserFromBuildingRequest>
    {
        private readonly UserBuildingsRepository _userBuildingsRepository;
        private readonly UsersRepository _usersRepository;

        public MasterRemoveUserFromBuildingEndpoint(UserBuildingsRepository userBuildingsRepository,
            UsersRepository usersRepository)
        {
            _userBuildingsRepository = userBuildingsRepository;
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/master/users/removeUserFromBuilding");
            SerializerContext(MasterRemoveUserFromBuildingContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterRemoveUserFromBuildingRequest req, CancellationToken ct)
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
            (UserManagementResult queryResult, UserData? userData) = await _userBuildingsRepository.MasterRemoveUserFromBuildingAsync(req, userId.Value, adminUserDisplayName, remoteIpAddress);

            // Load user data if building did not exist
            if (queryResult == UserManagementResult.UserDidNotExistInBuilding && userData is null)
            {
                userData = await _usersRepository.GetUserByUidAsync(req.Uid!.Value);

                if (userData is null)
                {
                    queryResult = UserManagementResult.UserDidNotExist;
                }
            }

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

        private void ValidateInput(MasterRemoveUserFromBuildingRequest req)
        {
            // Validate input

            // Validate OrganizationId
            if (!req.OrganizationId.HasValue)
            {
                AddError(m => m.OrganizationId!, "Organization Id is required.", "error.organizationIdIsRequired");
            }

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.masterSettings.users.uidIsRequired");
            }

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building Id is required.", "error.buildingIdIsRequired");
            }

            // Validate ConcurrencyKey
            if (req.ConcurrencyKey is null || req.ConcurrencyKey.Length == 0)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key is required.", "error.concurrencyKeyIsRequired");
            }
            else if (req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
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
                        break;
                    }
                    return;
                case UserManagementResult.UserDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError(m => m.Uid!, "The user was deleted since you last accessed this page.", "error.masterSettings.users.deletedSinceAccessedPage");
                    break;
                case UserManagementResult.UserDidNotExistInBuilding:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData, MasterRemoveUserFromBuildingContext.Default.UserData));
                    AddError("The user was removed from the building since you last accessed this page.", "error.masterSettings.users.userRemovedFromBuildingSinceAccessedPage");
                    break;
                case UserManagementResult.ConcurrencyKeyInvalid:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(userData, MasterRemoveUserFromBuildingContext.Default.UserData));
                    AddError("The user's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit again if you still wish to remove the user from the building.", "error.masterSettings.users.deleteConcurrencyKeyInvalid.building");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
