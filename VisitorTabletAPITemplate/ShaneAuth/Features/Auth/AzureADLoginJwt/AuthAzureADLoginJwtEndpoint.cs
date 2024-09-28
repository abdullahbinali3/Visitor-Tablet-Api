using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage.Enums;
using VisitorTabletAPITemplate.ImageStorage.Models;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.ShaneAuth.ShaneAuth.Models;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Auth.AzureADLoginJwt
{
    public sealed class AuthAzureADLoginJwtEndpoint : Endpoint<AuthAzureADLoginJwtRequest, TokenResponse>
    {
        private readonly AppSettings _appSettings;
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly ImageStorageRepository _imageStorageRepository;
        private readonly MicrosoftAccountService _microsoftAccountService;

        public AuthAzureADLoginJwtEndpoint(AppSettings appSettings,
            OrganizationsRepository organizationsRepository,
            UsersRepository usersRepository,
            ImageStorageRepository imageStorageRepository,
            MicrosoftAccountService microsoftAccountService)
        {
            _appSettings = appSettings;
            _organizationsRepository = organizationsRepository;
            _usersRepository = usersRepository;
            _imageStorageRepository = imageStorageRepository;
            _microsoftAccountService = microsoftAccountService;
        }

        public override void Configure()
        {
            Post("/auth/azureADLoginJwt");
            SerializerContext(AuthAzureADLoginJwtContext.Default);
            AllowAnonymous();
            Tags("IgnoreAntiforgeryToken");
        }

        public override async Task HandleAsync(AuthAzureADLoginJwtRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            OrganizationAzureADSingleSignOnInfo singleSignOnInfo = await _organizationsRepository.GetAzureADSingleSignOnInfo(req.OrganizationId!.Value, ct);

            if (!singleSignOnInfo.SingleSignOnEnabled)
            {
                AddError(m => m.OrganizationId!, "The login request was invalid.", "error.login.azureADLoginRequestInvalid");
                await SendErrorsAsync();
                return;
            }

            // string redirectUri = Globals.FrontEndBaseUrl + "/sso-complete/mobile"; // Web Url, old url
            string redirectUri = $"{Globals.BackEndBaseUrl}/auth/ssoComplete/mobile"; // Api Url, new url 

            MicrosoftAccountsResult<MicrosoftTokenResponse> getTokenResult = await _microsoftAccountService.GetTokenFromAuthorizationCodeAsync(singleSignOnInfo.ClientId!.Value, singleSignOnInfo.ClientSecret!,
                singleSignOnInfo.TenantId!.Value, "user.read", redirectUri, req.AuthorizationCode!);

            if (!getTokenResult.Success || getTokenResult.Result is null || getTokenResult.Result.access_token is null)
            {
                if (!string.IsNullOrEmpty(getTokenResult.Error))
                {
                    AddError(getTokenResult.Error);
                }
                else
                {
                    AddError(m => m.AuthorizationCode!, "The login request was invalid.", "error.login.azureADLoginRequestInvalid");
                }

                await SendErrorsAsync();
                return;
            }

            MicrosoftAccountsResult<MicrosoftUserData> getUserResult = await _microsoftAccountService.GetUserData(getTokenResult.Result.access_token);

            if (!getUserResult.Success || getUserResult.Result is null)
            {
                if (!string.IsNullOrEmpty(getTokenResult.Error))
                {
                    AddError(getTokenResult.Error);
                }
                else
                {
                    AddError(m => m.AuthorizationCode!, "The login request was invalid.", "error.login.azureADLoginRequestInvalid");
                }

                await SendErrorsAsync();
                return;
            }

            // Check if TID and OID were parsed from the access token
            if (getUserResult.Result.TenantId is null || getUserResult.Result.ObjectId is null)
            {
                AddError("Could not retrieve Azure AD Tenant ID and User ID from access token.", "error.login.azureADLoginNoTenantIdObjectId");
                await SendErrorsAsync();
                return;
            }

            // Check the TID from the access token matches the TenantId stored in SSP
            if (singleSignOnInfo.TenantId.Value != getUserResult.Result.TenantId.Value)
            {
                AddError(m => m.AuthorizationCode!, "The login request was invalid.", "error.login.azureADLoginRequestInvalid");
                await SendErrorsAsync();
                return;
            }

            // Get the user's email address, either using the Mail field if available, otherwise using the User Principal Name
            if (string.IsNullOrWhiteSpace(getUserResult.Result.mail) && !string.IsNullOrWhiteSpace(getUserResult.Result.userPrincipalName))
            {
                getUserResult.Result.mail = getUserResult.Result.userPrincipalName;
            }

            if (string.IsNullOrEmpty(getUserResult.Result.mail))
            {
                if (!string.IsNullOrEmpty(getTokenResult.Error))
                {
                    AddError(getTokenResult.Error);
                }
                else
                {
                    AddError("Could not retrieve your Azure AD account username.", "error.login.azureADLoginNoUsername");
                }

                await SendErrorsAsync();
                return;
            }

            (bool azureTenantIdObjectIdValid, bool azureTenantIdObjectIdUnset, bool azureTenantIdObjectIdLinkedToOtherEmail, UserData? userData) = await _usersRepository.GetUserAndValidateAzureObjectIdAsync(getUserResult.Result.TenantId.Value, getUserResult.Result.ObjectId.Value, getUserResult.Result.mail, true, "Web", ct);

            // Check if the Azure TenantId and ObjectId are already linked to a different user
            if (azureTenantIdObjectIdLinkedToOtherEmail)
            {
                AddError("The email address of your Azure AD account and the linked account in Smart Space Pro do not match. If your email address was changed within Azure AD, please contact Facilities Management to have your Smart Space Pro account email updated to match the one on your Azure AD account.", "error.login.azureTenantIdObjectIdLinkedToOtherUser");
                await SendErrorsAsync();
                return;
            }

            if (userData is null)
            {
                // If user doesn't exist, check if email domain belongs to a valid Organization, if so, allow user to register

                // Below is based on InitRegister and CheckRegister combined to be used for Azure AD.

                // Get requester's IP address
                string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                // Retrieve the user's profile photo
                MicrosoftAccountsResult<MicrosoftUserProfilePhotoData> getUserProfilePhotoResult = await _microsoftAccountService.GetUserProfilePhoto(getTokenResult.Result.access_token);

                string? azureAvatarUrl = null;
                Guid? azureAvatarImageStorageId = null;

                // Store the photo, only if retrieving it was successful.
                if (getUserProfilePhotoResult.Success && getUserProfilePhotoResult.Result is not null)
                {
                    Guid azureProfilePhotoImageStorageLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                    (SqlQueryResult storeImageResult, StoredImageFile? storedImageFile) =
                        await _imageStorageRepository.WriteImageAsync(
                            _appSettings.ImageUpload.ObjectRestrictions.UserAvatar.MaxImageWidth,
                            _appSettings.ImageUpload.ObjectRestrictions.UserAvatar.MaxImageHeight,
                            true,
                            false,
                            getUserProfilePhotoResult.Result.ProfilePhoto,
                            ImageStorageRelatedObjectType.AzureProfilePhoto,
                            getUserResult.Result.ObjectId,
                            req.OrganizationId.Value,
                            "tblRegisterAzureTokens",
                            azureProfilePhotoImageStorageLogId,
                            null,
                            null,
                            remoteIpAddress);

                    if (storeImageResult == SqlQueryResult.Ok && storedImageFile is not null)
                    {
                        azureAvatarUrl = storedImageFile.FileUrl;
                        azureAvatarImageStorageId = storedImageFile.Id;
                    }
                }

                UserSelfRegistrationResult userSelfRegistrationResult = await _usersRepository.InitRegisterAzureADAsync(getUserResult.Result.TenantId.Value,
                    getUserResult.Result.ObjectId.Value, req.OrganizationId!.Value, getUserResult.Result.mail, getUserResult.Result.userPrincipalName, getUserResult.Result.givenName, getUserResult.Result.surname,
                    getUserResult.Result.displayName, req.UserAgentBrowserName, req.UserAgentOsName, req.UserAgentDeviceInfo, azureAvatarUrl, azureAvatarImageStorageId, remoteIpAddress);

                switch (userSelfRegistrationResult)
                {
                    case UserSelfRegistrationResult.RecordAlreadyExists:
                        // This one shouldn't ever happen, because userData wouldn't be null if the user exists
                        AddError("A user with the specified email address already exists.", "error.register.userEmailExists");
                        break;
                    case UserSelfRegistrationResult.SingleSignOnNotEnabled:
                        HttpContext.Items.Add("FatalError", true);
                        AddError("Single Sign On using Azure Active Directory has not been enabled for your organization. Please contact your Facilities Management team for assistance, or create a local account using the 'Create an account' link from the Smart Space Pro login page instead.", "error.register.azureADSingleSignOnNotEnabled");
                        break;
                    case UserSelfRegistrationResult.Ok:
                        AddError("An email has been sent to you with instructions on how to set up your account. Please refer to the email to continue.", "error.login.azureADRegistrationRequired");
                        break;
                }

                await SendErrorsAsync();
                return;
            }
            else if (userData.UserSystemRole == UserSystemRole.NoAccess || userData.Disabled)
            {
                AddError("Your account does not have access to this system.", "error.login.accountHasNoAccess");
                await SendErrorsAsync();
                return;
            }
            else if (azureTenantIdObjectIdValid && !azureTenantIdObjectIdUnset)
            {
                // Create JWT Token to sign in the user
                Response = await CreateTokenWith<AuthJwtTokenService>(userData!.Uid.ToString(), userPrivileges =>
                {
                    ShaneAuthHelpers.PopulateUserPrivileges(userPrivileges, userData!);
                });
                return;
            }
            else if (!azureTenantIdObjectIdValid && azureTenantIdObjectIdUnset)
            {
                // If user has an existing account but hasn't signed in using Azure AD SSO before,
                // send them an email notification to confirm linking their account.

                // Get requester's IP address
                string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

                // Send link account confirmation email
                SqlQueryResult linkAccountConfirmEmailResult = await _usersRepository.InitLinkAccountAzureADSingleSignOnConfirmation(userData.Uid, getUserResult.Result.TenantId.Value,
                    getUserResult.Result.ObjectId.Value, userData.Email, userData.FirstName, userData.DisplayName, getUserResult.Result.userPrincipalName, getUserResult.Result.displayName, req.UserAgentBrowserName, req.UserAgentOsName, req.UserAgentDeviceInfo, remoteIpAddress);

                if (linkAccountConfirmEmailResult == SqlQueryResult.Ok)
                {
                    AddError("As this is your first time logging in using Single Sign On, a confirmation email has been sent to the email address associated with your account. Please refer to the confirmation email to continue.", "error.login.linkAccountAzureADConfirmRequired");
                }
                else
                {
                    // Should never happen as we would have found the linked account above if so
                    AddError("An unknown error occurred.", "error.unknown");
                }


                await SendErrorsAsync();
                return;
            }
            else if (!azureTenantIdObjectIdValid && !azureTenantIdObjectIdUnset)
            {
                AddError(m => m.AuthorizationCode!, "The login request was invalid.", "error.login.azureADLoginRequestInvalid");
                await SendErrorsAsync();
                return;
            }
            else
            {
                AddError("An unknown error occurred.", "error.unknown");
                await SendErrorsAsync();
                return;
            }
        }

        public void ValidateInput(AuthAzureADLoginJwtRequest req)
        {
            // Validate input

            // Validate OrganizationId
            if (!req.OrganizationId.HasValue)
            {
                AddError("Organization Id is required.", "error.organizationIdIsRequired");
            }

            // Validate AuthorizationCode
            if (string.IsNullOrEmpty(req.AuthorizationCode))
            {
                AddError("Authorization Code is required.", "error.login.authorizationCodeIsRequired");
            }
        }
    }
}
