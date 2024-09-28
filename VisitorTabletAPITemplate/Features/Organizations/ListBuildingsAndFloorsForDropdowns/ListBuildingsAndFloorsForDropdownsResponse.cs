namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFloorsForDropdowns
{
    public sealed class ListBuildingsAndFloorsForDropdownsResponse
    {
        public List<ListBuildingsAndFloorsForDropdownsResponse_Building> Buildings { get; set; } =
           new List<ListBuildingsAndFloorsForDropdownsResponse_Building>();

        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }

    public sealed class ListBuildingsAndFloorsForDropdownsResponse_Building
    {
        public Guid BuildingId { get; set; }
        public string BuildingName { get; set; } = default!;
        public List<ListBuildingsAndFloorsForDropdownsResponse_Floor> Floors { get; set; } =
               new List<ListBuildingsAndFloorsForDropdownsResponse_Floor>();
    }

    public sealed class ListBuildingsAndFloorsForDropdownsResponse_Floor
    {
        public Guid BuildingId { get; set; }
        public Guid FloorId { get; set; }
        public string FloorName { get; set; } = default!;
    }
}
