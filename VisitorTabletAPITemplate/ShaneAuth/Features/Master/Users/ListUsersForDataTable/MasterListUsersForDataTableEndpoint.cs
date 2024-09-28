using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Repositories;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.ListUsersForDataTable
{
    public sealed class MasterListUsersForDataTableEndpoint : Endpoint<MasterListUsersForDataTableRequest>
    {
        private readonly UsersRepository _usersRepository;

        public MasterListUsersForDataTableEndpoint(UsersRepository usersRepository)
        {
            _usersRepository = usersRepository;
        }

        public override void Configure()
        {
            Get("/master/users/listForDataTable");
            SerializerContext(MasterListUsersForDataTableContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(MasterListUsersForDataTableRequest req, CancellationToken ct)
        {
            // Validate request
            ValidateInput(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            // Query data
            DataTableResponse<MasterUserDataForDataTable> userDataTable = await _usersRepository.MasterListUsersForDataTableAsync(req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, req.Filter, ct);

            // Check if PageNumber value exceeds total number of records.
            // If so, query data again using page 1 instead.
            if (1 + ((userDataTable.PageNumber - 1) * userDataTable.PageSize) > userDataTable.TotalCount)
            {
                userDataTable = await _usersRepository.MasterListUsersForDataTableAsync(1, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, req.Filter, ct);
            }

            await SendAsync(userDataTable);
        }

        private void ValidateInput(MasterListUsersForDataTableRequest req)
        {
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

            // Validate filters
            if (req.Filter is not null)
            {
                // Validate Filter.OrganizationIds
                if (req.Filter.OrganizationIds is not null && req.Filter.OrganizationIds.Count > 0)
                {
                    req.Filter.OrganizationIds = Toolbox.DedupeGuidList(req.Filter.OrganizationIds);
                }

                // Validate Filter.UserSystemRole
                if (req.Filter.UserSystemRole is not null && !ShaneAuthHelpers.IsValidUserSystemRole(req.Filter.UserSystemRole))
                {
                    AddError(m => m.Filter!.UserSystemRole!, "Filter User System Role is invalid.", "error.masterSettings.users.filter.userSystemRoleIsInvalid");
                }
            }
        }
    }
}
