namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class MicrosoftUserData
    {
        public string? displayName { get; set; }
        public string? surname { get; set; }
        public string? givenName { get; set; }
        public string? mail { get; set; }
        public string? id { get; set; }
        public string? userPrincipalName { get; set; }
        public Guid? TenantId { get; set; }
        public Guid? ObjectId { get; set; }

        /* Microsoft also returns these, which we don't care about
        public string? odatacontext { get; set; }
        public object[]? businessPhones { get; set; }
        public object? jobTitle { get; set; }

        public object? mobilePhone { get; set; }
        public object? officeLocation { get; set; }
        public object? preferredLanguage { get; set; }
        */
    }
}
