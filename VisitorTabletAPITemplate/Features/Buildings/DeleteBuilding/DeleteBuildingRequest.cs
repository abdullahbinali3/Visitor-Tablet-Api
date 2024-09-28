namespace VisitorTabletAPITemplate.Features.Buildings.DeleteBuilding
{
    public sealed class DeleteBuildingRequest
    {
        public Guid? id { get; set; }
        public Guid? OrganizationId { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
