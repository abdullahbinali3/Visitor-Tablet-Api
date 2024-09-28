namespace VisitorTabletAPITemplate.ShaneAuth.ShaneAuth.Models
{
    public sealed class RegisterAzureTokenDbRow
    {
        public Guid AzureTenantId { get; set; }
        public Guid AzureObjectId { get; set; }
        public string RegisterToken { get; set; } = default!;
        public DateTime InsertDateUtc { get; set; }
        public string Email { get; set; } = default!;
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public Guid OrganizationId { get; set; }
        public DateTime ExpiryDateUtc { get; set; }
        public string? Location { get; set; }
        public string? BrowserName { get; set; }
        public string? OSName { get; set; }
        public string? DeviceInfo { get; set; }
        public string? AvatarUrl { get; set; }
        public Guid? AvatarImageStorageId { get; set; }
    }
}
