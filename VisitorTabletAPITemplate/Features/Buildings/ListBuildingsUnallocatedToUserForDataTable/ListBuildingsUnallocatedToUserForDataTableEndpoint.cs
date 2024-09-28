using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.Features.Buildings.ListBuildingsUnallocatedToUserForDataTable
{
    public sealed class ListBuildingsUnallocatedToUserForDataTableEndpoint : Endpoint<ListBuildingsUnallocatedToUserForDataTableRequest>
    {
        private readonly BuildingsRepository _buildingsRepository;
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListBuildingsUnallocatedToUserForDataTableEndpoint(BuildingsRepository buildingsRepository,
            UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _buildingsRepository = buildingsRepository;
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/buildings/{organizationId}/listUnallocatedToUserForDataTable/{uid}");
            SerializerContext(ListBuildingsUnallocatedToUserForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListBuildingsUnallocatedToUserForDataTableRequest req, CancellationToken ct)
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
            DataTableResponse<Building> buildingDataTable = await _buildingsRepository.ListBuildingsUnallocatedForUserForDataTableAsync(req.OrganizationId!.Value, req.Uid!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, ct);

            await SendAsync(buildingDataTable);
        }

        private async Task ValidateInputAsync(ListBuildingsUnallocatedToUserForDataTableRequest req, Guid userId, CancellationToken cancellationToken = default)
        {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.SuperAdmin, _authCacheService))
            {
                return;
            }

            // Validate input

            // Validate Uid
            if (!req.Uid.HasValue)
            {
                AddError(m => m.Uid!, "Uid is required.", "error.user.uidIsRequired");
            }
            else if (!await _usersRepository.IsUserExistsAsync(req.Uid.Value, cancellationToken))
            {
                AddError(m => m.Uid!, "The selected user did not exist.", "error.user.didNotExist");
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
