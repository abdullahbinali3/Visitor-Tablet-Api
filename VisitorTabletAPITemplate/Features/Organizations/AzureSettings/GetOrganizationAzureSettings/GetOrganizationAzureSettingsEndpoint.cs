using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Organizations.AzureSettings.GetOrganizationAzureSettings
{
    public sealed class GetOrganizationAzureSettingsEndpoint : Endpoint<GetOrganizationAzureSettingsRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetOrganizationAzureSettingsEndpoint(OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/organizations/azureSettings/get/{organizationId}");
            SerializerContext(GetOrganizationAzureSettingsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetOrganizationAzureSettingsRequest req, CancellationToken ct)
        {
            // Get logged in user's UID
            Guid? userId = User.GetId();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req, userId.Value, ct);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            OrganizationAzureSetting? organizationAzureSetting = await _organizationsRepository.GetOrganizationAzureSettingsAsync(req.OrganizationId!.Value, ct);

            // If no settings in database, just return empty object
            if (organizationAzureSetting == null)
            {
                await SendNoContentAsync();
                return;
            }

            await SendAsync(organizationAzureSetting!);
        }

        private async Task ValidateInputAsync(GetOrganizationAzureSettingsRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }
        }
    }
}
