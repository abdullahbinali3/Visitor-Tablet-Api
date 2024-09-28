using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Users.ListUsersForDataTable
{
    public sealed class ListUsersForDataTableEndpoint : Endpoint<ListUsersForDataTableRequest>
    {
        private readonly UsersRepository _usersRepository;
        private readonly AuthCacheService _authCacheService;

        public ListUsersForDataTableEndpoint(UsersRepository usersRepository,
            AuthCacheService authCacheService)
        {
            _usersRepository = usersRepository;
            _authCacheService = authCacheService;
        }

        public override void Configure()
        {
            Get("/users/{organizationId}/listForDataTable");
            SerializerContext(ListUsersForDataTableContext.Default);
            Policies("User");
        }

        public override async Task HandleAsync(ListUsersForDataTableRequest req, CancellationToken ct)
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
            DataTableResponse<ManageableUserDataForDataTable> data = await _usersRepository.ListUsersForDataTableAsync(req.OrganizationId!.Value, req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, req.IncludeDisabled!.Value, ct);

            await SendAsync(data);
        }

        private async Task ValidateInputAsync(ListUsersForDataTableRequest req, Guid userId, CancellationToken cancellationToken)
        {
            // Validate user has minimum required access to organization to perform this action
            await this.ValidateUserOrganizationRoleAsync(req.OrganizationId, userId, UserOrganizationRole.User, _authCacheService, cancellationToken);

            // Validate input

            // Validate IncludeDisabled
            if (!req.IncludeDisabled.HasValue)
            {
                req.IncludeDisabled = false;
            }

            // If no page number specified just use page 1 as default
            if (!req.PageNumber.HasValue)
            {
                req.PageNumber = 1;
            }

            // If no page size specified just use 30 as default
            if (!req.PageSize.HasValue || req.PageSize.Value <= 0)
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
