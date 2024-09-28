using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Regions.ListRegionsForDropdown
{
    public sealed class ListRegionsForDropdownEndpoint : Endpoint<ListRegionsForDropdownRequest>
    {
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListRegionsForDropdownEndpoint(RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/regions/{organizationId}/listForDropdown");
            SerializerContext(ListRegionsForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListRegionsForDropdownRequest req, CancellationToken ct)
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
            SelectListResponse data = await _regionsRepository.ListRegionsForDropdownAsync(req.OrganizationId!.Value, req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListRegionsForDropdownRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken);
        }
    }
}
