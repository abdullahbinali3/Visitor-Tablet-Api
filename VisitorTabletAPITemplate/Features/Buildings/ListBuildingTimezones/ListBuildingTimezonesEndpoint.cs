using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingTimezones
{
    public sealed class ListBuildingTimezonesEndpoint : Endpoint<ListBuildingTimezonesRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListBuildingTimezonesEndpoint(BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/listTimezones");
            SerializerContext(ListBuildingTimezonesContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListBuildingTimezonesRequest req, CancellationToken ct)
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
            Dictionary<Guid, string> data = await _buildingsRepository.GetBuildingsTimeZoneStringDictionary(req.OrganizationId!.Value, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListBuildingTimezonesRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken);
        }
    }
}
