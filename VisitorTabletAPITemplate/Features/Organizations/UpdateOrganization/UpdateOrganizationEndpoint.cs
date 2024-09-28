using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.Utilities;
using System.Text.Json;

namespace VisitorTabletAPITemplate.Features.Organizations.UpdateOrganization
{
    public sealed class UpdateOrganizationEndpoint : Endpoint<UpdateOrganizationRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly OrganizationsRepository _organizationsRepository;

        public UpdateOrganizationEndpoint(AppSettings appSettings,
            OrganizationsRepository organizationsRepository)
        {
            _appSettings = appSettings;
            _organizationsRepository = organizationsRepository;
        }

        public override void Configure()
        {
            Post("/organizations/update");
            SerializerContext(UpdateOrganizationContext.Default);
            Policies("Master");
            AllowFileUploads();
        }

        public override async Task HandleAsync(UpdateOrganizationRequest req, CancellationToken ct)
        {
            // Validate request
            ContentInspectorResultWithMemoryStream? logoImageContentInspectorResult = await ValidateInputAsync(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Organization? organization, List<OrganizationDomainCollision>? organizationDomainCollisions) = await _organizationsRepository.UpdateOrganizationAsync(req, logoImageContentInspectorResult, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, organization, organizationDomainCollisions);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(organization!);
        }

        private async Task<ContentInspectorResultWithMemoryStream?> ValidateInputAsync(UpdateOrganizationRequest req)
        {
            ContentInspectorResultWithMemoryStream? contentInspectorResult = null;
            HashSet<string> uniqueDomains = new HashSet<string>();

            // Trim strings
            req.Name = req.Name?.Trim();

            // Remove duplicates
            if (req.Domains is not null)
            {
                req.Domains = Toolbox.DedupeList(req.Domains);
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Organization Id is required.", "error.organization.idIsRequired");
            }

            // Validate Name
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                AddError(m => m.Name!, "Organization Name is required.", "error.organization.nameIsRequired");
            }
            else if (req.Name.Length > 100)
            {
                AddError(m => m.Name!, "Organization Name must be 100 characters or less.", "error.organization.nameLength|{\"length\":\"100\"}");
            }

            // Validate Domains
            if (req.Domains is null || req.Domains.Count == 0)
            {
                AddError(m => m.Domains!, "Email Domains is required.", "error.organization.emailDomainsIsRequired");
            }
            else if (req.Domains.Count > 25)
            {
                AddError(m => m.Domains!, "The maximum number of Email Domains is 25.", "error.organization.emailDomainsMaximum|{\"maximum\":\"25\"}");
            }
            else
            {
                for (int i = 0; i < req.Domains.Count; ++i)
                {
                    string domain = req.Domains[i];

                    // Check domain length
                    if (domain.Length > 252)
                    {
                        var errorParams = new
                        {
                            domain = $"{domain[..25]}...",
                            length = 252
                        };
                        AddError(m => m.Domains!, $"The email domain \"{domain[..25]}...\" must be 252 characters or less.", $"error.organization.emailDomainLength|{JsonSerializer.Serialize(errorParams)}");
                        continue;
                    }

                    // Validate domain is a valid DNS hostname, and that it contains a dot, but not at the start or end of the string.
                    int indexOfDot = domain.IndexOf('.');
                    if (indexOfDot == -1 || indexOfDot == 0 || indexOfDot == domain.Length - 1 || Uri.CheckHostName(domain) != UriHostNameType.Dns)
                    {
                        var errorParams = new
                        {
                            domain
                        };
                        AddError(m => m.Domains!, $"The email domain \"{domain}\" is invalid.", $"error.organization.emailDomainIsInvalid|{JsonSerializer.Serialize(errorParams)}");
                        continue;
                    }

                    // Change domain to lowercase and add to HashSet
                    uniqueDomains.Add(domain.ToLowerInvariant());
                }
            }

            // Validate LogoImage
            if (!req.LogoImageChanged.HasValue)
            {
                AddError(m => m.LogoImageChanged!, "Logo Image Changed is required.", "error.organization.logoImageChangedIsRequired");
            }
            else if (req.LogoImageChanged.Value && req.LogoImage is not null)
            {
                if (req.LogoImage.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.LogoImage!, $"Organization Logo maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.organization.maximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    contentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.LogoImage);

                    if (contentInspectorResult is null
                        || contentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidAnyVectorOrImageExtension(contentInspectorResult.InspectedExtension))
                    {
                        AddError(m => m.LogoImage!, $"Organization Logo image should be one of the following formats: {ImageStorageHelpers.ValidAnyVectorOrImageFormats}",
                            "error.organization.invalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidAnyVectorOrImageFormats + "\"}");
                    }
                }
            }

            // Validate AutomaticUserInactivityEnabled
            if (!req.AutomaticUserInactivityEnabled.HasValue)
            {
                AddError(m => m.AutomaticUserInactivityEnabled!, "Automatic User Inactivity Enabled is required.", "error.organization.automaticUserInactivityEnabledIsRequired");
            }

            // Validate CheckInEnabled
            if (!req.CheckInEnabled.HasValue)
            {
                AddError(m => m.CheckInEnabled!, "Check In Enabled is required.", "error.organization.checkInEnabledIsRequired");
            }

            // Validate MaxCapacityEnabled
            if (!req.MaxCapacityEnabled.HasValue)
            {
                AddError(m => m.MaxCapacityEnabled!, "Max Capacity Enabled is required.", "error.organization.maxCapacityEnabledIsRequired");
            }

            // Validate WorkplacePortalEnabled
            if (!req.WorkplacePortalEnabled.HasValue)
            {
                AddError(m => m.WorkplacePortalEnabled!, "Workplace Portal Enabled is required.", "error.organization.workplacePortalEnabledIsRequired");
            }

            // Validate AccessRequestsEnabled
            if (!req.WorkplaceAccessRequestsEnabled.HasValue)
            {
                AddError(m => m.WorkplaceAccessRequestsEnabled!, "Access Requests is required.", "error.organization.accessRequestsEnabledIsRequired");
            }

            // Validate WorkplaceInductionsEnabled
            if (!req.WorkplaceInductionsEnabled.HasValue)
            {
                AddError(m => m.WorkplaceInductionsEnabled!, "Workplace Inductions is required.", "error.organization.workplaceInductionsEnabledIsRequired");
            }

            // Validate Enforce2faEnabled
            if (!req.Enforce2faEnabled.HasValue)
            {
                AddError(m => m.Enforce2faEnabled!, "Enforce Two-Factor Authentication is required.", "error.organization.enforce2faEnabledIsRequired");
            }

            // Validate DisableLocalLoginEnabled
            if (!req.DisableLocalLoginEnabled.HasValue)
            {
                AddError(m => m.DisableLocalLoginEnabled!, "Disable Local Login is required.", "error.organization.disableLocalLoginIsRequired");
            }

            // Validate Disabled
            if (!req.Disabled.HasValue)
            {
                AddError(m => m.Disabled!, "Disabled is required.", "error.organization.disabledIsRequired");
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

            // If validation passed, keep only unique domains
            if (!ValidationFailed)
            {
                req.Domains = uniqueDomains.ToList();
            }

            return contentInspectorResult;
        }

        private void ValidateOutput(SqlQueryResult queryResult, Organization? organization, List<OrganizationDomainCollision>? organizationDomainCollisions)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The organization was deleted since you last accessed this page.", "error.organization.deletedSinceAccessedPage");
                    break;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another organization already exists with the specified name.", "error.organization.nameExists");
                    break;
                case SqlQueryResult.SubRecordAlreadyExists:
                    if (organizationDomainCollisions is not null)
                    {
                        foreach (OrganizationDomainCollision collision in organizationDomainCollisions)
                        {
                            var errorParams = new
                            {
                                domain = collision.DomainName,
                                organization = collision.OrganizationName ?? collision.OrganizationId.ToString()
                            };
                            AddError(m => m.Domains!, $"The email domain \"{collision.DomainName}\" already belongs to the organization \"{collision.OrganizationName ?? collision.OrganizationId.ToString()}\".",
                                $"error.organization.emailDomainBelongsToOrganization|{JsonSerializer.Serialize(errorParams)}");
                        }
                    }
                    else
                    {
                        AddError(m => m.Domains!, "Unknown existing email domain error.", "error.organization.emailDomainBelongsToOrganization.unknown");
                    }
                    break;
                case SqlQueryResult.ConcurrencyKeyInvalid:
                    HttpContext.Items.Add("ConcurrencyKeyInvalid", true);
                    HttpContext.Items.Add("ErrorAdditionalData", JsonSerializer.Serialize(organization!, UpdateOrganizationContext.Default.Organization));
                    AddError("The organization's data has changed since you last accessed this page. Please review the current updated version of the data below, then submit your changes again if you wish to overwrite.", "error.organization.concurrencyKeyInvalid");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
