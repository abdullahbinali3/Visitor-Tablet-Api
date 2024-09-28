namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class LoginOptions
    {
        public Guid? Uid { get; set; }
        public bool HasPassword { get; set; }
        public bool UserDisabled { get; set; }
        public Guid? OrganizationId { get; set; }
        public bool DisableLocalLoginEnabled { get; set; }
        public bool AzureADSingleSignOnEnabled { get; set; }
        public Guid? AzureADTenantId { get; set; }
        public Guid? AzureADSingleSignOnClientId { get; set; }
    }
}
