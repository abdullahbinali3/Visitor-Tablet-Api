namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFloorsForDropdowns
{
    public class ListBuildingsAndFloorsForDropdownsRequest
    {
        public Guid? id { get; set; } // OrganizationId

        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
