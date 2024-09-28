using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Functions.ListFunctionsForDropdown
{
    public sealed class ListFunctionsForDropdownEndpoint : Endpoint<ListFunctionsForDropdownRequest>
    {
        private readonly FunctionsRepository _functionsRepository;
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListFunctionsForDropdownEndpoint(FunctionsRepository functionsRepository,
            BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _functionsRepository = functionsRepository;
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/functions/{organizationId}/{buildingId}/listForDropdown");
            SerializerContext(ListFunctionsForDropdownContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListFunctionsForDropdownRequest req, CancellationToken ct)
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
            SelectListResponse data = await _functionsRepository.ListFunctionsForDropdownAsync(req.OrganizationId!.Value, req.BuildingId!.Value, req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListFunctionsForDropdownRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken))
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
        }
    }
}
