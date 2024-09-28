namespace VisitorTabletAPITemplate.Models
{
    public sealed class Function
    {
        public Guid id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
        public string BuildingName { get; set; } = default!;
        public string HtmlColor { get; set; } = default!;
        public List<FunctionAdjacency> FunctionAdjacencies { get; set; } = new List<FunctionAdjacency>();
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
