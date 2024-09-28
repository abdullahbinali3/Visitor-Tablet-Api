namespace VisitorTabletAPITemplate.Features.Buildings.GetBuilding
{
    public sealed class GetBuildingRequest
    {
        public Guid? id { get; set; }
        public Guid? OrganizationId { get; set; }
    }
}
