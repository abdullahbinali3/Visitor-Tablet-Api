using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;

namespace VisitorTabletAPITemplate.Features.Organizations.ListOrganizationsForDropdown
{
    public sealed class ListOrganizationsForDropdownEndpoint : Endpoint<ListOrganizationsForDropdownRequest>
    {
        private readonly OrganizationsRepository _organizationsRepository;

        public ListOrganizationsForDropdownEndpoint(OrganizationsRepository organizationsRepository)
        {
            _organizationsRepository = organizationsRepository;
        }

        public override void Configure()
        {
            Get("/organizations/listForDropdown");
            SerializerContext(ListOrganizationsForDropdownContext.Default);
            Policies("Master");
        }

        public override async Task HandleAsync(ListOrganizationsForDropdownRequest req, CancellationToken ct)
        {
            // Query data
            SelectListResponse data = await _organizationsRepository.ListOrganizationsForDropdownAsync(req.Search, req.RequestCounter, ct);

            await SendAsync(data);
        }
    }
}
