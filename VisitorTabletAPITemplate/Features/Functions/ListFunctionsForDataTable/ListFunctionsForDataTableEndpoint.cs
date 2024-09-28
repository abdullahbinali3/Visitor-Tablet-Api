using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Features.Functions.ListFunctionsForDataTable
{
    public sealed class ListFunctionsForDataTableEndpoint : Endpoint<ListFunctionsForDataTableRequest>
    {
        private readonly FunctionsRepository _functionsRepository;
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListFunctionsForDataTableEndpoint(FunctionsRepository functionsRepository,
            BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _functionsRepository = functionsRepository;
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }
        public override void Configure()
        {
            Get("/functions/{organizationId}/{buildingId}/listForDataTable");
            SerializerContext(ListFunctionsForDataTableContext.Default);
            Policies("User");
        }
        public override async Task HandleAsync(ListFunctionsForDataTableRequest req, CancellationToken ct)
        {
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
            DataTableResponse<Function> functionDataTable = await _functionsRepository.ListFunctionsForDataTableAsync(req.OrganizationId!.Value, req.BuildingId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, ct);

            await SendAsync(functionDataTable);
        }

        private async Task ValidateInputAsync(ListFunctionsForDataTableRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate BuildingId
            if (!req.BuildingId.HasValue)
            {
                AddError(m => m.BuildingId!, "Building Id is required.", "error.buildingIdIsRequired");
            }
            else if (!await _buildingsRepository.IsBuildingExistsAsync(req.BuildingId.Value, req.OrganizationId!.Value, cancellationToken))
            {
                HttpContext.Items.Add("FatalError", true);
                AddError(m => m.BuildingId!, "Building Id is invalid.", "error.buildingIdIsInvalid");
            }

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
