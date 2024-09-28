namespace VisitorTabletAPITemplate.Features.Regions.DeleteRegion
{
    public sealed class DeleteRegionRequest
    {
        public Guid? id { get; set; }
        public Guid? OrganizationId { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
