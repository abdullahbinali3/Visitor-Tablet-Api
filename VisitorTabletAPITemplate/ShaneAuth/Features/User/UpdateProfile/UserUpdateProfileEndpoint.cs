using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateProfile
{
    public sealed class UserUpdateProfileEndpoint : Endpoint<UserUpdateProfileRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public UserUpdateProfileEndpoint(AppSettings appSettings,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/user/updateProfile");
            SerializerContext(UserUpdateProfileContext.Default);
            Policies("User");
            AllowFileUploads();
        }

        public override async Task HandleAsync(UserUpdateProfileRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult = await ValidateInputAsync(req, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Get requester's IP address
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (VerifyCredentialsResult updateUserResult, VerifyCredentialsResult changePasswordResult, UserData? userData) = await _usersRepository.UpdateProfileAsync(req, avatarContentInspectorResult, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(updateUserResult, changePasswordResult);

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

        private async Task<ContentInspectorResultWithMemoryStream?> ValidateInputAsync(UserUpdateProfileRequest req, Guid userId)
        {
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult = null;

            if (req.OrganizationId.HasValue)
            {
                // Validate user has minimum required access to organization to perform this action
                if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService))
                {
                    return avatarContentInspectorResult;
                }
            }

            // Trim strings
            req.FirstName = req.FirstName?.Trim();
            req.Surname = req.Surname?.Trim();
            req.Timezone = req.Timezone?.Trim();
            req.Note = req.Note?.Trim();

            // Validate input

            // Validate FirstName
            if (string.IsNullOrEmpty(req.FirstName))
            {
                AddError(m => m.FirstName!, "First Name is required.", "error.profile.firstNameIsRequired");
            }
            else if (req.FirstName.Length > 75)
            {
                AddError(m => m.FirstName!, "First Name is required.", "error.profile.firstNameLength|{\"length\":\"75\"}");
            }

            // Validate Surname
            if (string.IsNullOrEmpty(req.Surname))
            {
                AddError(m => m.Surname!, "Surname is required.", "error.profile.surnameIsRequired");
            }
            else if (req.Surname.Length > 75)
            {
                AddError(m => m.Surname!, "Surname is required.", "error.profile.surnameLength|{\"length\":\"75\"}");
            }

            // Validate AvatarImageChanged
            if (!req.AvatarImageChanged.HasValue)
            {
                AddError(m => m.AvatarImageChanged!, "Avatar Image Changed is required.", "error.profile.avatarImageChangedIsRequired");
            }
            else if (req.AvatarImageChanged.Value && req.AvatarImage is not null)
            {
                if (req.AvatarImage.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.AvatarImage!, $"Avatar Image maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.profile.avatarImageMaximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    avatarContentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.AvatarImage);

                    if (avatarContentInspectorResult is null
                        || avatarContentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidImageExtension(avatarContentInspectorResult.InspectedExtension))
                    {
                        AddError(m => m.AvatarImage!, $"Avatar Image should be one of the following formats: {ImageStorageHelpers.ValidImageFormats}",
                            "error.profile.avatarImageInvalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidImageFormats + "\"}");
                    }
                }
            }

            // Validate Timezone
            if (string.IsNullOrEmpty(req.Timezone))
            {
                AddError(m => m.Timezone!, "Timezone is required.", "error.profile.timezoneIsRequired");
            }
            else if (!Toolbox.IsValidTimezone(req.Timezone))
            {
                AddError(m => m.Timezone!, "The specified timezone is not a valid timezone.", "error.profile.timezoneIsInvalid");
            }

            // Check for password change
            if (!string.IsNullOrEmpty(req.CurrentPassword)
                || !string.IsNullOrEmpty(req.NewPassword)
                || !string.IsNullOrEmpty(req.NewPasswordConfirm))
            {
                // Validate CurrentPassword, if UserHasNoPassword is null or false
                if (string.IsNullOrEmpty(req.CurrentPassword) && (!req.UserHasNoPassword.HasValue || !req.UserHasNoPassword.Value))
                {
                    AddError(m => m.CurrentPassword!, "Current Password is required.", "error.profile.currentPasswordIsRequired");
                }

                // Validate NewPassword
                if (string.IsNullOrEmpty(req.NewPassword))
                {
                    AddError(m => m.NewPassword!, "New Password is required.", "error.profile.newPasswordIsRequired");
                }
                else if (req.NewPassword.Length < 10)
                {
                    AddError(m => m.NewPassword!, "New Password must be at least 10 characters long.", "error.profile.newPasswordLength|{\"length\":\"10\"}");
                }

                // Validate NewPasswordConfirm
                if (string.IsNullOrEmpty(req.NewPasswordConfirm))
                {
                    AddError(m => m.NewPasswordConfirm!, "New Password (Confirm) is required.", "error.profile.newPasswordConfirmIsRequired");
                }
                else if (req.NewPasswordConfirm.Length < 10)
                {
                    AddError(m => m.NewPasswordConfirm!, "New Password (Confirm) must be at least 10 characters long.", "error.profile.newPasswordConfirmLength|{\"length\":\"10\"}");
                }
                else if (req.NewPassword != req.NewPasswordConfirm)
                {
                    AddError(m => m.NewPasswordConfirm!, "New Password and New Password (Confirm) must match.", "error.profile.newPasswordConfirmMismatch");
                }
            }

            if (req.OrganizationId.HasValue)
            {
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

            return avatarContentInspectorResult;
        }

        private void ValidateOutput(VerifyCredentialsResult updateUserResult, VerifyCredentialsResult changePasswordResult)
        {
            // Validate queried data
            switch (updateUserResult)
            {
                case VerifyCredentialsResult.Ok:
                    // Validate update password result
                    switch (changePasswordResult)
                    {
                        case VerifyCredentialsResult.Ok:
                            return;
                        case VerifyCredentialsResult.NoAccess:
                        case VerifyCredentialsResult.UserDidNotExist:
                            AddError("Your account does not have access to this system.", "error.accountHasNoAccess");
                            break;
                        case VerifyCredentialsResult.PasswordInvalid:
                            AddError(m => m.CurrentPassword!, "Your current password is invalid.", "error.profile.currentPasswordIsInvalid");
                            break;
                        default:
                            AddError("An unknown error occurred.", "error.unknown");
                            break;
                    }
                    return;
                case VerifyCredentialsResult.NoAccess:
                    AddError("Your account does not have access to this system.", "error.accountHasNoAccess");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
