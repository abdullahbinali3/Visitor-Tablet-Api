namespace VisitorTabletAPITemplate.Features.Regions.UpdateRegion
{
    public sealed class UpdateRegionRequest
    {
        public Guid? id { get; set; }
        public string? Name { get; set; }
        public Guid? OrganizationId { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
