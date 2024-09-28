namespace VisitorTabletAPITemplate.Features.Organizations.ListBuildingsAndFunctionsForDropdowns
{
    public sealed class ListBuildingsAndFunctionsForDropdownsResponse
    {
        public List<ListBuildingsAndFunctionsForDropdownsResponse_Building> Buildings { get; set; } = 
            new List<ListBuildingsAndFunctionsForDropdownsResponse_Building>();

        [FromHeader(headerName: "X-Request-Counter", isRequired: false)]
        public long? RequestCounter { get; set; }
    }

    public sealed class ListBuildingsAndFunctionsForDropdownsResponse_Building
    {
        public Guid BuildingId { get; set; }
        public string BuildingName { get; set; } = default!;
        public List<ListBuildingsAndFunctionsForDropdownsResponse_Function> Functions { get; set; } = 
               new List<ListBuildingsAndFunctionsForDropdownsResponse_Function>();
    }

    public sealed class ListBuildingsAndFunctionsForDropdownsResponse_Function
    {
        public Guid BuildingId { get; set; }
        public Guid FunctionId { get; set; }
        public string FunctionName { get; set; } = default!;
    }
}