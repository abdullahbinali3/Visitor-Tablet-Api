namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFunctionsForDropdowns
{
    public sealed class ListBuildingsAndFunctionsForDropdownsRequest
    {
        public Guid? id { get; set; }   // OrganizationId

        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
