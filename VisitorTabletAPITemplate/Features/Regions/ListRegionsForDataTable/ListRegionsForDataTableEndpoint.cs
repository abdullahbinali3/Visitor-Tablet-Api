using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Features.Regions.ListRegionsForDataTable
{
    public sealed class ListRegionsForDataTableEndpoint : Endpoint<ListRegionsForDataTableRequest>
    {
        private readonly RegionsRepository _regionsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListRegionsForDataTableEndpoint(RegionsRepository regionsRepository,
            AuthCacheService authCacheService)
        {
            _regionsRepository = regionsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/regions/{organizationId}/listForDataTable");
            SerializerContext(ListRegionsForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListRegionsForDataTableRequest req, CancellationToken ct)
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
            DataTableResponse<Region> regionDataTable = await _regionsRepository.ListRegionsForDataTableAsync(req.OrganizationId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, ct);

            await SendAsync(regionDataTable);
        }

        private async Task ValidateInputAsync(ListRegionsForDataTableRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // If no page number specified just use page 1 as default
            if (!req.PageNumber.HasValue)
            {
                req.PageNumber = 1;
            }

            // If no page size specified just use 30 as default
            if (!req.PageSize.HasValue)
            {
                req.PageSize = 30;
            }

            // If no sort specified just use Name as default
            if (!req.Sort.HasValue || req.Sort == SortType.Unsorted)
            {
                req.Sort = SortType.Name;
            }
        }
    }
}
