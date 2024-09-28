namespace VisitorTabletAPITemplate.Features.Organizations.UpdateOrganization
{
    public sealed class UpdateOrganizationRequest
    {
        public Guid? id { get; set; }
        public string? Name { get; set; }
        public List<string>? Domains { get; set; }
        public IFormFile? LogoImage { get; set; }
        public bool? LogoImageChanged { get; set; }
        public bool? AutomaticUserInactivityEnabled { get; set; }
        public bool? CheckInEnabled { get; set; }
        public bool? MaxCapacityEnabled { get; set; }
        public bool? WorkplacePortalEnabled { get; set; }
        public bool? WorkplaceAccessRequestsEnabled { get; set; }
        public bool? WorkplaceInductionsEnabled { get; set; }
        public bool? Enforce2faEnabled { get; set; }
        public bool? DisableLocalLoginEnabled { get; set; }
        public bool? Disabled { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
