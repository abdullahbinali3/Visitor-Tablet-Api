using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.GetBuilding
{
    public sealed class GetBuildingEndpoint : Endpoint<GetBuildingRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public GetBuildingEndpoint(BuildingsRepository buildingsRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/get/{id}");
            SerializerContext(GetBuildingContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(GetBuildingRequest req, CancellationToken ct)
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
            Building? building = await _buildingsRepository.GetBuildingAsync(req.id!.Value, req.OrganizationId!.Value, ct);

            // Validate result
            ValidateOutput(building);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(building!);
        }

        private async Task ValidateInputAsync(GetBuildingRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user is a Super Admin, ignoring the building permission, OR is NOT a Super Admin but has access to the building
            if (!await this.ValidateUserBuildingOrSuperAdminAsync(req.OrganizationId, req.id, userId, _authCacheService, _buildingsRepository, cancellationToken))
            {
                return;
            }

            // Validate input

            // Validate id
            if (!req.id.HasValue)
            {
                AddError(m => m.id!, "Building Id is required.", "error.building.idIsRequired");
            }
        }

        private void ValidateOutput(Building? building)
        {
            if (building is null)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("The selected building did not exist.", "error.building.didNotExist");
            }
        }
    }
}
