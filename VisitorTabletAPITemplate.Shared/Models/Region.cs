namespace VisitorTabletAPITemplate.Models
{
    public sealed class Region
    {
        public Guid id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public string Name { get; set; } = default!;
        public Guid OrganizationId { get; set; }
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
