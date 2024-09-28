using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUserBuilding
{
    public sealed class MasterUpdateUserBuildingEndpoint : Endpoint<MasterUpdateUserBuildingRequest>
    {
        private readonly UserBuildingsRepository _userBuildingsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly FunctionsRepository _functionsRepository;

        public MasterUpdateUserBuildingEndpoint(UserBuildingsRepository userBuildingsRepository,
            UsersRepository usersRepository,
            OrganizationsRepository organizationsRepository,
            FunctionsRepository functionsRepository)
        {
            _userBuildingsRepository = userBuildingsRepository;
            _usersRepository = usersRepository;
            _organizationsRepository = organizationsRepository;
            _functionsRepository = functionsRepository;
        }

        public override void Configure()
        {
            Post("/master/users/updateBuilding");
            SerializerContext(MasterUpdateUserBuildingContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterUpdateUserBuildingRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            UserOrganizationRole? userOrganizationRole = await ValidateInputAsync(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (UserManagementResult queryResult, UserData? userData) = await _userBuildingsRepository.MasterUpdateUserBuildingAsync(req, userOrganizationRole!.Value, userId, adminUserDisplayName, remoteIpAddress);

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

        private async Task<UserOrganizationRole?> ValidateInputAsync(MasterUpdateUserBuildingRequest req)
        {
            UserOrganizationRole? userOrganizationRole = null;
            UserData? userData = null;
            bool validBuilding = false;

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.masterSettings.users.uidIsRequired");
                return null;
            }
            else
            {
                userData = await _usersRepository.GetUserByUidAsync(req.Uid.Value, false, true);

                if (userData is null)
                {
                    HttpContext.Items.TryAdd("FatalError", true);
                    AddError(m => m.Uid!, "The user was deleted since you last accessed this page.", "error.masterSettings.users.deletedSinceAccessedPage");
                    return null;
                }

                // If user does not belong to any organizations, stop here.
                if (userData.ExtendedData.Organizations is null || userData.ExtendedData.Organizations.Count == 0)
                {
                    HttpContext.Items.TryAdd("FatalError", true);
                    AddError(m => m.OrganizationId!, "The user does not belong to the specified organization.", "error.masterSettings.users.userDoesNotBelongToOrganization");
                    return null;
                }

                // Validate OrganizationId
                if (!req.OrganizationId.HasValue)
                {
                    AddError(m => m.OrganizationId!, "Organization is required.", "error.masterSettings.users.organizationIsRequired");
                }
                else if (!await _organizationsRepository.IsOrganizationExistsAsync(req.OrganizationId!.Value))
                {
                    HttpContext.Items.TryAdd("FatalError", true);
                    AddError(m => m.OrganizationId!, "Organization is invalid.", "error.masterSettings.users.organizationIsInvalid");
                    return null;
                }
                else
                {
                    // Find the user's UserOrganizationRole in the organization so we can use it later
                    foreach (UserData_UserOrganizations userOrganization in userData!.ExtendedData.Organizations!)
                    {
                        if (userOrganization.Id == req.OrganizationId.Value)
                        {
                            userOrganizationRole = (UserOrganizationRole)userOrganization.UserOrganizationRole;

                            // Check if user belongs to building while we are here
                            if (req.BuildingId.HasValue)
                            {
                                foreach (UserData_Building building in userOrganization.Buildings)
                                {
                                    if (building.Id == req.BuildingId.Value)
                                    {
                                        validBuilding = true;
                                        break;
                                    }
                                }

                                if (!validBuilding)
                                {
                                    HttpContext.Items.TryAdd("FatalError", true);
                                    AddError(m => m.BuildingId!, "The user does not belong to the specified building.", "error.masterSettings.users.userDoesNotBelongToBuilding");
                                    return null;
                                }
                            }
                        }
                    }

                    // If user does not belong to the organization, stop here
                    if (userOrganizationRole is null)
                    {
                        HttpContext.Items.TryAdd("FatalError", true);
                        AddError(m => m.OrganizationId!, "The user does not belong to the specified organization.", "error.masterSettings.users.userDoesNotBelongToOrganization");
                        return null;
                    }
                }
            }

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building Id is required.", "error.buildingIdIsRequired");
            }

            // Validate FunctionId
            if (!req.FunctionId.HasValue)
            {
                AddError(m => m.FunctionId!, "Function is required.", "error.masterSettings.users.functionIsRequired");
            }
            else if (validBuilding && !await _functionsRepository.IsFunctionExistsInBuildingAsync(req.FunctionId!.Value, req.BuildingId!.Value, req.OrganizationId!.Value))
            {
                AddError(m => m.FunctionId!, "Function is invalid.", "error.masterSettings.users.functionIsInvalid");
            }

            // Validate UserAssetTypes
            if (req.UserAssetTypes is not null && req.UserAssetTypes.Count > 0)
            {
                // Remove duplicates and Guid.Empty
                req.UserAssetTypes = Toolbox.DedupeGuidList(req.UserAssetTypes);
            }

            // Validate AllowBookingDeskForVisitor
            if (!req.AllowBookingDeskForVisitor.HasValue)
            {
                AddError(m => m.AllowBookingDeskForVisitor!, "Allow Booking Desk For Visitor is required.", "error.masterSettings.users.allowBookingDeskForVisitorIsRequired");
            }

            // Validate AllowBookingRestrictedRooms
            if (!req.AllowBookingRestrictedRooms.HasValue)
            {
                AddError(m => m.AllowBookingRestrictedRooms!, "Allow Booking Restricted Rooms is required.", "error.masterSettings.users.allowBookingRestrictedRoomsIsRequired");
            }

            // Validate FirstAidOfficer
            if (!req.FirstAidOfficer.HasValue)
            {
                AddError(m => m.FirstAidOfficer!, "First Aid Officer is required.", "error.masterSettings.users.firstAidOfficerIsRequired");
            }

            // Validate FireWarden
            if (!req.FireWarden.HasValue)
            {
                AddError(m => m.FireWarden!, "Fire Warden is required.", "error.masterSettings.users.fireWardenIsRequired");
            }

            // Validate PeerSupportOfficer
            if (!req.PeerSupportOfficer.HasValue)
            {
                AddError(m => m.PeerSupportOfficer!, "Peer Support Officer is required.", "error.masterSettings.users.peerSupportOfficerIsRequired");
            }

            if (userOrganizationRole is not null)
            {
                // Validate Admin and Super Admin-specific fields
                switch (userOrganizationRole)
                {
                    case UserOrganizationRole.NoAccess:
                    case UserOrganizationRole.User:
                    case UserOrganizationRole.Tablet:
                        // Clear Admin and Super Admin-specific fields for users + tablet
                        req.AllowBookingAnyoneAnywhere = false;
                        req.UserAdminFunctions = null;
                        req.UserAdminAssetTypes = null;
                        break;
                    case UserOrganizationRole.Admin:
                        // Validate AllowBookingAnyoneAnywhere
                        if (!req.AllowBookingAnyoneAnywhere.HasValue)
                        {
                            AddError(m => m.AllowBookingAnyoneAnywhere!, "Allow Booking Anyone Anywhere is required.", "error.masterSettings.users.allowBookingAnyoneAnywhereIsRequired");
                        }

                        // Validate UserAdminFunctions
                        if (req.UserAdminFunctions is not null && req.UserAdminFunctions.Count > 0)
                        {
                            // Remove duplicates and Guid.Empty
                            req.UserAdminFunctions = Toolbox.DedupeGuidList(req.UserAdminFunctions);
                        }

                        // Validate UserAdminAssetTypes
                        if (req.UserAdminAssetTypes is not null && req.UserAdminAssetTypes.Count > 0)
                        {
                            // Remove duplicates and Guid.Empty
                            req.UserAdminAssetTypes = Toolbox.DedupeGuidList(req.UserAdminAssetTypes);
                        }
                        break;
                    case UserOrganizationRole.SuperAdmin:
                        // Validate AllowBookingAnyoneAnywhere
                        if (!req.AllowBookingAnyoneAnywhere.HasValue)
                        {
                            AddError(m => m.AllowBookingAnyoneAnywhere!, "Allow Booking Anyone Anywhere is required.", "error.masterSettings.users.allowBookingAnyoneAnywhereIsRequired");
                        }

                        req.UserAdminFunctions = null;
                        req.UserAdminAssetTypes = null;
                        break;
                    default:
                        throw new Exception($"Unknown UserOrganizationRole: {userOrganizationRole}");
                }
            }

            return userOrganizationRole;
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
                case UserManagementResult.UserAssetTypesInvalid:
                    AddError(m => m.UserAssetTypes!, "At least one of the specified user asset types was invalid.", "error.masterSettings.users.userAssetTypesInvalid");
                    break;
                case UserManagementResult.UserAdminFunctionsInvalid:
                    AddError(m => m.UserAdminFunctions!, "At least one of the specified admin functions was invalid.", "error.masterSettings.users.userAdminFunctionsInvalid");
                    break;
                case UserManagementResult.UserAdminAssetTypesInvalid:
                    AddError(m => m.UserAdminAssetTypes!, "At least one of the specified admin asset types was invalid.", "error.masterSettings.users.userAdminAssetTypesInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
