using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;

namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsForDataTable
{
    public sealed class ListOrganizationsForDataTableEndpoint : Endpoint<ListOrganizationsForDataTableRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;

        public ListOrganizationsForDataTableEndpoint(OrganizationsRepository organizationsRepository)
        {
            _organizationsRepository = organizationsRepository;
        }

        public override void Configure()
        {
            Get("/organizations/listForDataTable");
            SerializerContext(ListOrganizationsForDataTableContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(ListOrganizationsForDataTableRequest req, CancellationToken ct)
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
            DataTableResponse<Organization> organizationDataTable = await _organizationsRepository.ListOrganizationsForDataTableAsync(req.PageNumber!.Value, req.PageSize!.Value, req.Sort!.Value, req.RequestCounter, req.Search, ct);

            await SendAsync(organizationDataTable);
        }

        private static void ValidateInput(ListOrganizationsForDataTableRequest req)
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
        }
    }
}
