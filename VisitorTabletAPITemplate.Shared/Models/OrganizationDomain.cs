namespace VisitorTabletAPITemplate.Models
{
    public sealed class OrganizationDomain
    {
        public Guid OrganizationId { get; set; }
        public string DomainName { get; set; } = default!;
        public DateTime InsertDateUtc { get; set; }
    }
}
