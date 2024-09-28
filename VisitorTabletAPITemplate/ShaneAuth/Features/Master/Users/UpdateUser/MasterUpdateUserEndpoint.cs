using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;
using System.Text.Json;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUser
{
    public sealed class MasterUpdateUserEndpoint : Endpoint<MasterUpdateUserRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly UsersRepository _usersRepository;

        public MasterUpdateUserEndpoint(AppSettings appSettings,
            UsersRepository usersRepository)
        {
            _appSettings = appSettings;
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Post("/master/users/update");
            SerializerContext(MasterUpdateUserContext.Default);
            Policies("Master");
            AllowFileUploads();
        }

        public override async Task HandleAsync(MasterUpdateUserRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, UserData? userData) = await _usersRepository.MasterUpdateUserAsync(req, avatarContentInspectorResult, userId, adminUserDisplayName, remoteIpAddress);

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

            await SendAsync(userData);
        }

        private async Task<ContentInspectorResultWithMemoryStream?> ValidateInputAsync(MasterUpdateUserRequest req)
        {
            ContentInspectorResultWithMemoryStream? avatarContentInspectorResult = null;

            // Trim strings
            req.Email = req.Email?.Trim().ToLowerInvariant();
            req.FirstName = req.FirstName?.Trim();
            req.Surname = req.Surname?.Trim();

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.masterSettings.users.uidIsRequired");
            }

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

            // Validate NewPassword
            if (!string.IsNullOrEmpty(req.NewPassword) && req.NewPassword.Length < 10)
            {
                AddError(m => m.NewPassword!, "New Local Password must be at least 10 characters long.", "error.masterSettings.users.newLocalPasswordLength|{\"length\":\"10\"}");
            }

            // Validate NewPasswordConfirm
            if (!string.IsNullOrEmpty(req.NewPasswordConfirm))
            {
                if (req.NewPasswordConfirm.Length < 10)
                {
                    AddError(m => m.NewPasswordConfirm!, "Confirm New Local Password must be at least 10 characters long.", "error.masterSettings.users.newLocalPasswordConfirmLength|{\"length\":\"10\"}");
                }
                else if (req.NewPassword != req.NewPasswordConfirm)
                {
                    AddError(m => m.NewPasswordConfirm!, "New Local Password and Confirm New Local Password must match.", "error.masterSettings.users.newLocalPasswordConfirmMismatch");
                }
            }

            // Validate UserSystemRole
            if (!req.UserSystemRole.HasValue)
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

            // Validate AvatarImageChanged
            if (!req.AvatarImageChanged.HasValue)
            {
                AddError(m => m.AvatarImageChanged!, "Avatar Image Changed is required.", "error.masterSettings.users.avatarImageChangedIsRequired");
            }
            else if (req.AvatarImageChanged.Value && req.AvatarImage is not null)
            {
                if (req.AvatarImage.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.AvatarImage!, $"Avatar Image maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.masterSettings.users.avatarImageMaximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    avatarContentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.AvatarImage);

                    if (avatarContentInspectorResult is null
                        || avatarContentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidImageExtension(avatarContentInspectorResult.InspectedExtension))
                    {
                        AddError(m => m.AvatarImage!, $"Avatar Image should be one of the following formats: {ImageStorageHelpers.ValidImageFormats}",
                            "error.masterSettings.users.avatarImageInvalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidImageFormats + "\"}");
                    }
                }
            }

            return avatarContentInspectorResult;
        }

        private void ValidateOutput(SqlQueryResult queryResult, UserData? data)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    if (data is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                    }
                    break;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The selected user did not exist.", "error.masterSettings.users.didNotExist");
                    break;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Email!, "Another user already exists with the specified email.", "error.masterSettings.users.emailExists");
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    if (data is null)
                    {
                        AddError("An unknown error occurred.", "error.unknown");
                        break;
                    }

                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(data, MasterUpdateUserContext.Default.UserData));
                    AddError("The user's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.masterSettings.users.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
