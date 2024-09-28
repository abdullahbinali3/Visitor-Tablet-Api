using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Organizations.AzureSettings.UpdateOrganizationAzureSettings
{
    public sealed class UpdateOrganizationAzureSettingsEndpoint : Endpoint<UpdateOrganizationAzureSettingsRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public UpdateOrganizationAzureSettingsEndpoint(OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Post("/organizations/azureSettings/update");
            SerializerContext(UpdateOrganizationAzureSettingsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(UpdateOrganizationAzureSettingsRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, OrganizationAzureSetting? organizationAzureSetting) = await _organizationsRepository.UpdateOrganizationAzureSettingsAsync(req, userId!.Value, adminUserDisplayName, remoteIpAddress);

            // If no settings in database, just return empty object
            if (organizationAzureSetting == null)
            {
                organizationAzureSetting = new OrganizationAzureSetting();
            }

            // Validate result
            ValidateOutput(queryResult, organizationAzureSetting);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(organizationAzureSetting);
        }

        private async Task ValidateInputAsync(UpdateOrganizationAzureSettingsRequest req, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Trim strings
            req.AzureADIntegrationClientSecret = req.AzureADIntegrationClientSecret?.Trim();
            req.AzureADSingleSignOnClientSecret = req.AzureADSingleSignOnClientSecret?.Trim();
            req.AzureADIntegrationNote = req.AzureADIntegrationNote?.Trim();
            req.AzureADSingleSignOnNote = req.AzureADSingleSignOnNote?.Trim();

            // Validate input

            // Validate UseCustomAzureADApplication
            if (!req.UseCustomAzureADApplication.HasValue)
            {
                AddError(m => m.UseCustomAzureADApplication!, "Use Custom Azure AD Application is required.", "error.organizationSettings.organizationAzureSettings.useCustomAzureADApplicationIsRequired");
            }

            // Validate AzureADTenantId
            if (!req.AzureADTenantId.HasValue)
            {
                AddError(m => m.AzureADTenantId!, "Azure AD Tenant ID is required.", "error.organizationSettings.organizationAzureSettings.azureADTenantIdIsRequired");
            }

            // Validate AzureADIntegrationEnabled
            if (!req.AzureADIntegrationEnabled.HasValue)
            {
                AddError(m => m.AzureADIntegrationEnabled!, "Azure AD Integration Enabled is required.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationEnabledIsRequired");
            }
            else if (req.AzureADIntegrationEnabled.Value)
            {
                // If Azure AD Integration is enabled and using Custom Azure AD application,
                // validate that required fields are present.
                if (req.UseCustomAzureADApplication.HasValue && req.UseCustomAzureADApplication.Value)
                {
                    // Validate AzureADIntegrationClientId
                    if (!req.AzureADIntegrationClientId.HasValue)
                    {
                        AddError(m => m.AzureADIntegrationClientId!, "Azure AD Integration Client ID is required.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationClientIdIsRequired");
                    }

                    // Validate AzureADIntegrationClientSecret
                    if (string.IsNullOrEmpty(req.AzureADIntegrationClientSecret))
                    {
                        AddError(m => m.AzureADIntegrationClientSecret!, "Azure AD Integration Client Secret is required.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationClientSecretIsRequired");
                    }
                    else if (req.AzureADIntegrationClientSecret.Length > 98)
                    {
                        AddError(m => m.AzureADIntegrationClientSecret!, "Azure AD Integration Client Secret must be 98 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationClientSecretLength|{\"length\":\"98\"}");
                    }

                    // Validate AzureADIntegrationNote
                    if (req.AzureADIntegrationNote is not null && req.AzureADIntegrationNote.Length > 500)
                    {
                        AddError(m => m.AzureADIntegrationNote!, "Azure AD Integration Note must be 500 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationNoteLength|{\"length\":\"500\"}");
                    }
                }
            }
            else if (!req.AzureADIntegrationEnabled.Value)
            {
                // These fields are invisible on the page in this case, but we'll keep the submitted values anyway,
                // so the values are not lost if the user only wants to disable the integration temporarily.

                // Validate AzureADIntegrationClientSecret
                if (!string.IsNullOrEmpty(req.AzureADIntegrationClientSecret) && req.AzureADIntegrationClientSecret.Length > 98)
                {
                    AddError(m => m.AzureADIntegrationClientSecret!, "Azure AD Integration Client Secret must be 98 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationClientSecretLength|{\"length\":\"98\"}");
                }

                // Validate AzureADIntegrationNote
                if (req.AzureADIntegrationNote is not null && req.AzureADIntegrationNote.Length > 500)
                {
                    AddError(m => m.AzureADIntegrationNote!, "Azure AD Integration Note must be 500 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADIntegrationNoteLength|{\"length\":\"500\"}");
                }
            }

            // Validate AzureADSingleSignOnEnabled
            if (!req.AzureADSingleSignOnEnabled.HasValue)
            {
                AddError(m => m.AzureADSingleSignOnEnabled!, "Azure AD Single Sign On Enabled is required.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnEnabledIsRequired");
            }
            else if (req.AzureADSingleSignOnEnabled.Value)
            {
                // If Azure AD Single Sign On is enabled and using Custom Azure AD application,
                // validate that required fields are present.
                if (req.UseCustomAzureADApplication.HasValue && req.UseCustomAzureADApplication.Value)
                {
                    // Validate AzureADSingleSignOnClientId
                    if (!req.AzureADSingleSignOnClientId.HasValue)
                    {
                        AddError(m => m.AzureADSingleSignOnClientId!, "Azure AD Single Sign On Client ID is required.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnClientIdIsRequired");
                    }

                    // Validate AzureADSingleSignOnClientSecret
                    if (string.IsNullOrEmpty(req.AzureADSingleSignOnClientSecret))
                    {
                        AddError(m => m.AzureADSingleSignOnClientSecret!, "Azure AD Single Sign On Client Secret is required.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnClientSecretIsRequired");
                    }
                    else if (req.AzureADSingleSignOnClientSecret.Length > 98)
                    {
                        AddError(m => m.AzureADSingleSignOnClientSecret!, "Azure AD Single Sign On Client Secret must be 98 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnClientSecretLength|{\"length\":\"98\"}");
                    }

                    // Validate AzureADSingleSignOnNote
                    if (req.AzureADSingleSignOnNote is not null && req.AzureADSingleSignOnNote.Length > 500)
                    {
                        AddError(m => m.AzureADSingleSignOnNote!, "Azure AD Single Sign On Note must be 500 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnNoteLength|{\"length\":\"500\"}");
                    }
                }
            }
            else if (!req.AzureADSingleSignOnEnabled.Value)
            {
                // These fields are invisible on the page in this case, but we'll keep the submitted values anyway,
                // so the values are not lost if the user only wants to disable single sign on temporarily.

                // Validate AzureADSingleSignOnClientSecret
                if (!string.IsNullOrEmpty(req.AzureADSingleSignOnClientSecret) && req.AzureADSingleSignOnClientSecret.Length > 98)
                {
                    AddError(m => m.AzureADSingleSignOnClientSecret!, "Azure AD Single Sign On Client Secret must be 98 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnClientSecretLength|{\"length\":\"98\"}");
                }

                // Validate AzureADSingleSignOnNote
                if (req.AzureADSingleSignOnNote is not null && req.AzureADSingleSignOnNote.Length > 500)
                {
                    AddError(m => m.AzureADSingleSignOnNote!, "Azure AD Single Sign On Note must be 500 characters or less.", "error.organizationSettings.organizationAzureSettings.azureADSingleSignOnNoteLength|{\"length\":\"500\"}");
                }
            }

            // Validate ConcurrencyKey
            // Only validating this if present, for the case where no settings are present in the database yet.
            if (req.ConcurrencyKey is not null && req.ConcurrencyKey.Length != 4)
            {
                AddError(m => m.ConcurrencyKey!, "Concurrency Key must be 4 bytes in length.", "error.concurrencyKeyLengthBytes|{\"length\":\"4\"}");
            }
        }

        private void ValidateOutput(SqlQueryResult queryResult, OrganizationAzureSetting? organizationAzureSetting)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(organizationAzureSetting!, UpdateOrganizationAzureSettingsContext.Default.OrganizationAzureSetting));
                    AddError("The organization azure settings have changed since you last accessed this page. Please review the current updated version of the settings below, then submit your changes again if you wish to overwrite.", "error.organizationSettings.organizationAzureSettings.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
