using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Organizations.NotificationsSettings.GetOrganizationNotificationsSettings
{
    public sealed class GetOrganizationNotificationsSettingsEndpoint : Endpoint<GetOrganizationNotificationsSettingsRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetOrganizationNotificationsSettingsEndpoint(OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/organizations/notificationsSettings/get/{organizationId}");
            SerializerContext(GetOrganizationNotificationsSettingsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetOrganizationNotificationsSettingsRequest req, CancellationToken ct)
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
            OrganizationNotificationsSetting? organizationNotificationsSetting = await _organizationsRepository.GetOrganizationNotificationsSettingsAsync(req.OrganizationId!.Value, ct);

            // If no settings in database, just return empty object
            if (organizationNotificationsSetting == null)
            {
                await SendNoContentAsync();
                return;
            }

            await SendAsync(organizationNotificationsSetting!);
        }

        private async Task ValidateInputAsync(GetOrganizationNotificationsSettingsRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }
        }
    }
}
