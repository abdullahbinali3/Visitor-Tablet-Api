namespace VisitorTabletAPITemplate.Models
{
    public sealed class OrganizationDomainCollision
    {
        public Guid OrganizationId { get; set; }
        public string DomainName { get; set; } = default!;
        public string? OrganizationName { get; set; }
    }
}
