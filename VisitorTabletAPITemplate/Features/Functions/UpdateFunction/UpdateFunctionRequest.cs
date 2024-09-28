namespace VisitorTabletAPITemplate.Features.Functions.UpdateFunction
{
    public sealed class UpdateFunctionRequest
    {
        public Guid? id { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? BuildingId { get; set; }
        public string? Name { get; set; }
        public List<Guid>? FunctionAdjacencies { get; set; }
        public string? HtmlColor { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
