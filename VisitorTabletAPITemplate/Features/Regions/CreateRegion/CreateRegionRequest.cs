namespace VisitorTabletAPITemplate.Features.Regions.CreateRegion
{
    public sealed class CreateRegionRequest
    {
        public string? Name { get; set; }
        public Guid? OrganizationId { get; set; }
    }
}
