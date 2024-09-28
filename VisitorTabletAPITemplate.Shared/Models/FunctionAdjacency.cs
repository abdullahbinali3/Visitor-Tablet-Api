namespace VisitorTabletAPITemplate.Models
{
    public sealed class FunctionAdjacency
    {
        public Guid FunctionId { get; set; }
        public Guid AdjacentFunctionId { get; set; }
        public string AdjacentFunctionName { get; set; } = default!;
        public string AdjacentFunctionHtmlColor { get; set; } = default!;
        public DateTime InsertDateUtc { get; set; }
    }
}
