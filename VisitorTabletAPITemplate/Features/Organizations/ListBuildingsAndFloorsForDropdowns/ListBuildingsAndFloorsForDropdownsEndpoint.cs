using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFloorsForDropdowns
{
    public class ListBuildingsAndFloorsForDropdownsEndpoint: Endpoint<ListBuildingsAndFloorsForDropdownsRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;
        private readonly AuthCacheService _authCacheService;

        public ListBuildingsAndFloorsForDropdownsEndpoint(OrganizationsRepository organizationsRepository,
            AuthCacheService authCacheService)
        {
            _organizationsRepository = organizationsRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/organizations/{id}/listBuildingsAndFloorsForDropdowns");
            SerializerContext(ListBuildingsAndFloorsForDropdownsContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListBuildingsAndFloorsForDropdownsRequest req, CancellationToken ct)
        {
            // Get logged in user's details
            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();

            if (!userId.HasValue)
            {
                await SendForbiddenAsync();
                return;
            }

            // Validate request
            await ValidateInputAsync(req.id!.Value, userId.Value);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            (SqlQueryResult queryResult, ListBuildingsAndFloorsForDropdownsResponse? response)
                = await _organizationsRepository.ListBuildingsAndFloorsForDropdownsAsync(req.id.Value, req.RequestCounter, ct);

            // Validate result
            ValidateOutput(queryResult);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(response!);
        }

        private async Task ValidateInputAsync(Guid? id, Guid userId)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(id, userId, UserOrganizationRole.User, _authCacheService);
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
                    AddError("The selected organization did not exist.", "error.organization.didNotExist");
                    break;
                case SqlQueryResult.SubRecordDidNotExist:
                    // Organization has no buildings.
                    HttpContext.Items.Add("FatalError", true);
                    AddError("An administrator has not fully configured your organization, so the request could not be completed.", "error.organization.organizationNotConfigured");
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
