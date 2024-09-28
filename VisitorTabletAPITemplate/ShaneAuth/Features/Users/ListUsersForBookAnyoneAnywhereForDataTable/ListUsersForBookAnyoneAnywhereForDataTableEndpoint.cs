using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListUsersForBookAnyoneAnywhereForDataTable
{
    public sealed class ListUsersForBookAnyoneAnywhereForDataTableEndpoint : Endpoint<ListUsersForBookAnyoneAnywhereForDataTableRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListUsersForBookAnyoneAnywhereForDataTableEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/{buildingId}/listUsersForBookAnyoneAnywhereForDataTable");
            SerializerContext(ListUsersForBookAnyoneAnywhereForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListUsersForBookAnyoneAnywhereForDataTableRequest req, CancellationToken ct)
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

            // Query data - list all users in building as we allow anyone to be booked anywhere
            DataTableResponse<ManageableUserDataForDataTable> manageableUserDataTable = await _usersRepository.ListUsersInBuildingForDataTableAsync(req.OrganizationId!.Value, req.BuildingId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, req.IncludeDisabled!.Value, ct);

            await SendAsync(manageableUserDataTable);
        }

        private async Task ValidateInputAsync(ListUsersForBookAnyoneAnywhereForDataTableRequest req, Guid userId, CancellationToken cancellationToken = default)
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

            // Get user's building permission to check whether they have permission for Allow Booking Anyone Anywhere for the specified building.
            UserBuildingPermission? userBuildingPermission = await _authCacheService.GetUserBuildingPermissionAsync(userId, req.OrganizationId!.Value, req.BuildingId!.Value, cancellationToken);

            if (userBuildingPermission is null || !userBuildingPermission.AllowBookingAnyoneAnywhere)
            {
                HttpContext.Items.Add("FatalError", true);
                AddError("You do not have permission to perform this action.", "error.doNotHavePermission");
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
