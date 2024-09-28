using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsForDropdown
{
    public sealed class ListBuildingsForDropdownEndpoint : Endpoint<ListBuildingsForDropdownRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListBuildingsForDropdownEndpoint(BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/listForDropdown");
            SerializerContext(ListBuildingsForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListBuildingsForDropdownRequest req, CancellationToken ct)
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
            SelectListResponse data = await _buildingsRepository.ListBuildingsForDropdownAsync(req.OrganizationId!.Value, req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListBuildingsForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken);
        }
    }
}
