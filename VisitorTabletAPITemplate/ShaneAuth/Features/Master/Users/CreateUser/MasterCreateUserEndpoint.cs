using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.CreateUser
{
    public sealed class MasterCreateUserEndpoint : Endpoint<MasterCreateUserRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly UsersRepository _usersRepository;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly BuildingsRepository _buildingsRepository;
        private readonly FunctionsRepository _functionsRepository;

        public MasterCreateUserEndpoint(AppSettings appSettings,
            UsersRepository usersRepository,
            OrganizationsRepository organizationsRepository,
            BuildingsRepository buildingsRepository,
            FunctionsRepository functionsRepository)
        {
            _appSettings = appSettings;
            _usersRepository = usersRepository;
            _organizationsRepository = organizationsRepository;
            _buildingsRepository = buildingsRepository;
            _functionsRepository = functionsRepository;
        }

        public override void Configure()
        {
            Post("/master/users/create");
            SerializerContext(MasterCreateUserContext.Default);
            Policies("Master");
            AllowFileUploads();
        }

        public override async Task HandleAsync(MasterCreateUserRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult = await ValidateInputAsync(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (UserManagementResult queryResult, UserData? userData) = await _usersRepository.MasterCreateUserAsync(req,
                avatarContentInspectorResult, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, userData);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(userData!);
        }

        private async Task<ContentInspectorResultWithMemoryStream?> ValidateInputAsync(MasterCreateUserRequest req)
        {
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult = null;

            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();
            req.FirstName = req.FirstName?.Trim();
            req.Surname = req.Surname?.Trim();
            req.Note = req.Note?.Trim();

            // Validate input

            // Validate Email
            if (string.IsNullOrEmpty(req.Email))
            {
                AddError("Email is required.", "error.masterSettings.users.emailIsRequired");
            }
            else if (req.Email.Length > 254)
            {
                AddError(m => m.Email!, "Email must be 254 characters or less.", "error.masterSettings.users.emailLength|{\"length\":\"254\"}");
            }
            else if (!Toolbox.IsValidEmail(req.Email))
            {
                AddError("Email is invalid.", "error.masterSettings.users.emailIsInvalid");
            }

            // Validate FirstName
            if (string.IsNullOrWhiteSpace(req.FirstName))
            {
                AddError(m => m.FirstName!, "First Name is required.", "error.masterSettings.users.firstNameIsRequired");
            }
            else if (req.FirstName.Length > 75)
            {
                AddError(m => m.FirstName!, "First Name must be 75 characters or less.", "error.masterSettings.users.firstNameLength|{\"length\":\"75\"}");
            }

            // Validate Surname
            if (string.IsNullOrWhiteSpace(req.Surname))
            {
                AddError(m => m.Surname!, "Surname is required.", "error.masterSettings.users.surnameIsRequired");
            }
            else if (req.Surname.Length > 75)
            {
                AddError(m => m.Surname!, "Surname must be 75 characters or less.", "error.masterSettings.users.surnameLength|{\"length\":\"75\"}");
            }

            // Validate Password fields if one or both is provided
            if (!string.IsNullOrEmpty(req.Password) || !string.IsNullOrEmpty(req.PasswordConfirm))
            {
                // Validate NewPassword
                if (string.IsNullOrEmpty(req.Password))
                {
                    AddError(m => m.Password!, "Local Password is required.", "error.masterSettings.users.localPasswordIsRequired");
                }
                else if (req.Password.Length < 10)
                {
                    AddError(m => m.Password!, "Local Password must be at least 10 characters long.", "error.masterSettings.users.localPasswordLength|{\"length\":\"10\"}");
                }

                // Validate NewPasswordConfirm
                if (string.IsNullOrEmpty(req.PasswordConfirm))
                {
                    AddError(m => m.PasswordConfirm!, "Confirm Local Password is required.", "error.masterSettings.users.localPasswordConfirmIsRequired");
                }
                else if (req.PasswordConfirm.Length < 10)
                {
                    AddError(m => m.PasswordConfirm!, "Confirm Local Password must be at least 10 characters long.", "error.masterSettings.users.localPasswordConfirmLength|{\"length\":\"10\"}");
                }
                else if (req.Password != req.PasswordConfirm)
                {
                    AddError(m => m.PasswordConfirm!, "Local Password and Confirm Local Password must match.", "error.masterSettings.users.localPasswordConfirmMismatch");
                }
            }

            // Validate UserSystemRole
            if (!req.UserSystemRole.HasValue || req.UserSystemRole.Value == UserSystemRole.NoAccess)
            {
                AddError(m => m.UserSystemRole!, "User System Role is required.", "error.masterSettings.users.userSystemRoleIsRequired");
            }

            // Validate Timezone
            if (string.IsNullOrEmpty(req.Timezone))
            {
                AddError(m => m.Timezone!, "Timezone is required.", "error.masterSettings.users.timezoneIsRequired");
            }
            else if (!Toolbox.IsValidTimezone(req.Timezone))
            {
                AddError(m => m.Timezone!, "The specified timezone is not a valid timezone.", "error.masterSettings.users.timezoneIsInvalid");
            }

            // Validate Disabled
            if (!req.Disabled.HasValue)
            {
                AddError(m => m.Disabled!, "Login Disabled is required.", "error.masterSettings.users.loginDisabledIsRequired");
            }

            // Validate OrganizationId
            if (!req.OrganizationId.HasValue)
            {
                AddError(m => m.OrganizationId!, "Organization is required.", "error.masterSettings.users.organizationIsRequired");
            }
            else if (!await _organizationsRepository.IsOrganizationExistsAsync(req.OrganizationId.Value))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError(m => m.OrganizationId!, "Organization is invalid.", "error.masterSettings.users.organizationIsInvalid");
            }

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building is required.", "error.masterSettings.users.buildingIsRequired");
            }
            else if (req.OrganizationId.HasValue && !await _buildingsRepository.IsBuildingExistsAsync(req.BuildingId.Value, req.OrganizationId.Value))
            {
                HttpContext.Items.Add("FatalError", true);
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
            else if (req.OrganizationId.HasValue && req.BuildingId.HasValue && !await _functionsRepository.IsFunctionExistsInBuildingAsync(req.FunctionId.Value, req.BuildingId.Value, req.OrganizationId.Value))
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

            // Validate UserAvatar
            if (req.UserAvatar is not null)
            {
                if (req.UserAvatar.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.UserAvatar!, $"Avatar maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.masterSettings.users.avatarImageMaximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    avatarContentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.UserAvatar);

                    if (avatarContentInspectorResult is null
                        || avatarContentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidImageExtension(avatarContentInspectorResult.InspectedExtension))
                    {

                        AddError(m => m.UserAvatar!, $"Avatar Image should be one of the following formats: {ImageStorageHelpers.ValidVectorImageFormats}",
                            "error.masterSettings.users.avatarImageInvalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidVectorImageFormats + "\"}");
                    }
                }
            }

            return avatarContentInspectorResult;
        }

        private void ValidateOutput(UserManagementResult queryResult, UserData? userData)
        {
            // Validate queried data
            switch (queryResult)
            {
                case UserManagementResult.Ok:
                case UserManagementResult.NewUserCreated:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    return;
                case UserManagementResult.UserAlreadyExists:
                    AddError(m => m.Email!, "Another user already exists with the specified email.", "error.masterSettings.users.emailExists");
                    break;
                case UserManagementResult.NewUserCreatedButStoreAvatarImageFailed:
                    if (userData is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }

                    // Don't produce an error for now
                    //AddError(m => m.UserAvatar!, "User Avatar image upload failed.", "error.masterSettings.users.imageUploadFailed");
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
