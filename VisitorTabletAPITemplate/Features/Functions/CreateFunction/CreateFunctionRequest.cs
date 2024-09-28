namespace VisitorTabletAPITemplate.Features.Functions.CreateFunction
{
    public sealed class CreateFunctionRequest
    {
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public string? Name { get; set; }
        public List<Guid>? FunctionAdjacencies { get; set; }
        public string? HtmlColor { get; set; }
    }
}
