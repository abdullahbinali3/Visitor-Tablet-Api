using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsForRegionForDropdown
{
    public sealed class ListBuildingsForRegionForDropdownEndpoint : Endpoint<ListBuildingsForRegionForDropdownRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListBuildingsForRegionForDropdownEndpoint(BuildingsRepository buildingsRepository,
            RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/listForRegionForDropdown/{regionId}");
            SerializerContext(ListBuildingsForRegionForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListBuildingsForRegionForDropdownRequest req, CancellationToken ct)
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
            SelectListResponse data = await _buildingsRepository.ListBuildingsForRegionForDropdownAsync(req.OrganizationId!.Value, req.RegionId!.Value, req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListBuildingsForRegionForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate RegionId
            if (!req.RegionId.HasValue)
            {
                AddError(m => m.RegionId!, "Region Id is required.", "error.building.regionIdIsRequired");
            }
            else if (!await _regionsRepository.IsRegionExistsAsync(req.RegionId.Value, req.OrganizationId!.Value))
            {
                AddError(m => m.RegionId!, "Region Id is invalid.", "error.building.regionIdIsInvalid");
            }
        }
    }
}
