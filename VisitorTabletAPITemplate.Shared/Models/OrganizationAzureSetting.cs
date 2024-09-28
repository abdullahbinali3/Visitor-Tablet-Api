namespace VisitorTabletAPITemplate.Models
{
    public sealed class OrganizationAzureSetting
    {
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = default!;
        public DateTime UpdatedDateUtc { get; set; }
        public bool UseCustomAzureADApplication { get; set; }
        public Guid AzureADTenantId { get; set; }
        public bool AzureADIntegrationEnabled { get; set; }
        public Guid? AzureADIntegrationClientId { get; set; }
        public string? AzureADIntegrationClientSecret { get; set; }
        public string? AzureADIntegrationNote { get; set; }
        public Guid SystemAzureADIntegrationClientId { get; set; }
        public bool AzureADSingleSignOnEnabled { get; set; }
        public Guid? AzureADSingleSignOnClientId { get; set; }
        public string? AzureADSingleSignOnClientSecret { get; set; }
        public string? AzureADSingleSignOnNote { get; set; }
        public Guid SystemAzureADSingleSignOnClientId { get; set; }
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
