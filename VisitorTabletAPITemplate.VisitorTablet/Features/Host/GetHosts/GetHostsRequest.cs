namespace VisitorTabletAPITemplate.VisitorTablet.Features.Host.GetHosts
{
    public class GetHostsRequest
    {
        public Guid? OrganizationId { get; set; }
        public bool? IncludeDisabled { get; set; }
        public string? Search { get; set; }
        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }
}
