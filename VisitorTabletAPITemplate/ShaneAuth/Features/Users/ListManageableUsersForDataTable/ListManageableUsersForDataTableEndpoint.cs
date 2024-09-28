using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListManageableUsersForDataTable
{
    public sealed class ListManageableUsersForDataTableEndpoint : Endpoint<ListManageableUsersForDataTableRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListManageableUsersForDataTableEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/{buildingId}/listManageableUsersForDataTable");
            SerializerContext(ListManageableUsersForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListManageableUsersForDataTableRequest req, CancellationToken ct)
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
            UserOrganizationPermission? organizationPermission = await _authCacheService.GetUserOrganizationPermissionAsync(userId.Value, req.OrganizationId!.Value, ct);

            DataTableResponse<ManageableUserDataForDataTable> manageableUserDataTable;

            if (organizationPermission!.UserOrganizationRole == UserOrganizationRole.Admin)
            {
                // For admin, list all users who belong to the user's User Admin Functions.
                manageableUserDataTable = await _usersRepository.ListUsersInUserAdminFunctionsForDataTableAsync(userId.Value, req.OrganizationId!.Value, req.BuildingId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, req.IncludeDisabled!.Value, ct);
            }
            else if (organizationPermission!.UserOrganizationRole == UserOrganizationRole.SuperAdmin)
            {
                // For Super Admin, list all users in the building.
                manageableUserDataTable = await _usersRepository.ListUsersInBuildingForDataTableAsync(req.OrganizationId!.Value, req.BuildingId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, req.IncludeDisabled!.Value, ct);
            }
            else
            {
                throw new Exception($"Unknown User Organization Role: {organizationPermission.UserOrganizationRole}");
            }

            await SendAsync(manageableUserDataTable);
        }

        private async Task ValidateInputAsync(ListManageableUsersForDataTableRequest req, Guid userId, CancellationToken cancellationToken = default)
  {
            // Validate user has minimum required access to organization to perform this action
            if (!await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.Admin, _authCacheService, cancellationToken))
            {
                return;
            }

            // Validate user has minimum required access to the building to perform this action
            if (!await this.ValidateUserBuildingAsync(req.OrganizationId, req.BuildingId, userId, _authCacheService, cancellationToken))
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
