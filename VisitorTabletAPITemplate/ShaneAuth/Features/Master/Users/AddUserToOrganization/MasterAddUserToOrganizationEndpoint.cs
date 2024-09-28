using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.AddUserToOrganization
{
    public sealed class MasterAddUserToOrganizationEndpoint : Endpoint<MasterAddUserToOrganizationRequest>
    {
        private readonly UserOrganizationsRepository _userOrganizationsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly BuildingsRepository _buildingsRepository;
        private readonly FunctionsRepository _functionsRepository;

        public MasterAddUserToOrganizationEndpoint(UserOrganizationsRepository userOrganizationsRepository,
            UsersRepository usersRepository,
            OrganizationsRepository organizationsRepository,
            BuildingsRepository buildingsRepository,
            FunctionsRepository functionsRepository)
        {
            _userOrganizationsRepository = userOrganizationsRepository;
            _usersRepository = usersRepository;
            _organizationsRepository = organizationsRepository;
            _buildingsRepository = buildingsRepository;
            _functionsRepository = functionsRepository;
        }

        public override void Configure()
        {
            Post("/master/users/addUserToOrganization");
            SerializerContext(MasterAddUserToOrganizationContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterAddUserToOrganizationRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (UserManagementResult queryResult, UserData? userData) = await _userOrganizationsRepository.MasterAddUserToOrganizationAsync(req, userId, adminUserDisplayName, remoteIpAddress);

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

        private async Task ValidateInputAsync(MasterAddUserToOrganizationRequest req)
        {
            // Trim strings
            req.Note = req.Note?.Trim();

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.masterSettings.users.uidIsRequired");
            }
            else if (!await _usersRepository.IsUserExistsAsync(req.Uid.Value))
            {
                HttpContext.Items.TryAdd("FatalError", true);
                AddError(m => m.Uid!, "The user was deleted since you last accessed this page.", "error.masterSettings.users.deletedSinceAccessedPage");
                return;
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
                return;
            }

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building is required.", "error.masterSettings.users.buildingIsRequired");
            }
            else if (req.OrganizationId.HasValue && !await _buildingsRepository.IsBuildingExistsAsync(req.BuildingId.Value, req.OrganizationId!.Value))
            {
                HttpContext.Items.TryAdd("FatalError", true);
                AddError(m => m.BuildingId!, "Building is invalid.", "error.masterSettings.users.buildingIsInvalid");
            }

            // Validate UserOrganizationRole
            if (!req.UserOrganizationRole.HasValue || req.UserOrganizationRole.Value == UserOrganizationRole.NoAccess)
            {
                AddError(m => m.UserOrganizationRole!, "User Organization Role is required.", "error.masterSettings.users.userOrganizationRoleIsRequired");
            }
            else if (!EnumParser.IsValidEnum(req.UserOrganizationRole.Value))
            {
                AddError(m => m.UserOrganizationRole!, "User Organization Role is invalid.", "error.masterSettings.users.userOrganizationRoleIsInvalid");
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

            // Validate Note
            if (!string.IsNullOrEmpty(req.Note) && req.Note.Length > 500)
            {
                AddError(m => m.Note!, "Note must be 500 characters or less.", "error.masterSettings.users.noteLength|{\"length\":\"500\"}");
            }

            // Validate UserOrganizationDisabled
            if (!req.UserOrganizationDisabled.HasValue)
            {
                AddError(m => m.UserOrganizationDisabled!, "Access Disabled is required.", "error.masterSettings.users.userOrganizationDisabledIsRequired");
            }

            // Validate FunctionId
            if (!req.FunctionId.HasValue)
            {
                AddError(m => m.FunctionId!, "Function is required.", "error.masterSettings.users.functionIsRequired");
            }
            else if (!await _functionsRepository.IsFunctionExistsInBuildingAsync(req.FunctionId!.Value, req.BuildingId!.Value, req.OrganizationId!.Value))
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

            // Validate Admin and Super Admin-specific fields
            if (req.UserOrganizationRole.HasValue && req.UserOrganizationRole.Value != UserOrganizationRole.NoAccess)
            {
                switch (req.UserOrganizationRole.Value)
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
                        throw new Exception($"Unknown UserOrganizationRole: {req.UserOrganizationRole}");
                }
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
                case UserManagementResult.UserAlreadyExistsInOrganization:
                    AddError(m => m.OrganizationId!, "The user already belongs to the specified organization.", "error.masterSettings.users.organizationExists");
                    break;
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
