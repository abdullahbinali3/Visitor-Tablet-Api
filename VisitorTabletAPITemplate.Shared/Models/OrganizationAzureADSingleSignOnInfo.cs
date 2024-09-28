namespace VisitorTabletAPITemplate.Models
{
    public sealed class OrganizationAzureADSingleSignOnInfo
    {
        public bool SingleSignOnEnabled { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? ClientId { get; set; }
        public string? ClientSecret { get; set; }
    }
}
