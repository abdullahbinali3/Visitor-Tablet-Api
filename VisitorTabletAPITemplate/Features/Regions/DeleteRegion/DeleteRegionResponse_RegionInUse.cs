namespace VisitorTabletAPITemplate.Features.Regions.DeleteRegion
{
    public sealed class DeleteRegionResponse_RegionInUse
    {
        public List<DeleteRegionResponse_RegionInUse_Building>? Buildings { get; set; }
    }

    public sealed class DeleteRegionResponse_RegionInUse_Building
    {
        public Guid BuildingId { get; set; }
        public string BuildingName { get; set; } = default!;
    }
}
