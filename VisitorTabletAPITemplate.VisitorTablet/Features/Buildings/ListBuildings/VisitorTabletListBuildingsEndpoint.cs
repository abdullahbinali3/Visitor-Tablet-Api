using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.VisitorTablet.Repositories;

namespace VisitorTabletAPITemplate.VisitorTablet.Features.Buildings.ListBuildings
{
    public sealed class VisitorTabletListBuildingsEndpoint : Endpoint<VisitorTabletListBuildingsRequest>
    {
        private readonly VisitorTabletBuildingsRepository _visitorTabletBuildingsRepository;
        private readonly AuthCacheService _authCacheService;

        public VisitorTabletListBuildingsEndpoint(VisitorTabletBuildingsRepository visitorTabletBuildingsRepository,
            AuthCacheService authCacheService)
        {
            _visitorTabletBuildingsRepository = visitorTabletBuildingsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/visitorTablet/buildings/{organizationId}/list");
            SerializerContext(VisitorTabletListBuildingsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(VisitorTabletListBuildingsRequest req, CancellationToken ct)
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
            (SqlQueryResult queryResult, List<SelectListItemGuid>? data) = await _visitorTabletBuildingsRepository.ListBuildingsAsync(req.OrganizationId!.Value, userId!.Value, ct);

            // Validate result
            ValidateOutput(queryResult);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(data!);
        }

        private async Task ValidateInputAsync(VisitorTabletListBuildingsRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.Tablet, _authCacheService, cancellationToken);
        }

        private void ValidateOutput(SqlQueryResult queryResult)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordDidNotExist:
                    HttpContext.Items.Add("FatalError", true);
                    AddError("The selected organization did not exist.", "error.visitorTablet.organizationDidNotExist");
                    break;
                case SqlQueryResult.SubRecordDidNotExist:
                    // Organization has no buildings.
                    HttpContext.Items.Add("FatalError", true);
                    AddError("An administrator has not fully configured your organization, so the request could not be completed.", "error.visitorTablet.organizationNotConfigured");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
