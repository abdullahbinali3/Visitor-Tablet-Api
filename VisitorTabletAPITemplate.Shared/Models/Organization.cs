namespace VisitorTabletAPITemplate.Models
{
    public sealed class Organization
    {
        public Guid id { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public string Name { get; set; } = default!;
        public List<string> Domains { get; set; } = new List<string>();
        public string? LogoImageUrl { get; set; }
        public Guid? LogoImageStorageId { get; set; }
        public bool AutomaticUserInactivityEnabled { get; set; }
        public bool CheckInEnabled { get; set; }
        public bool MaxCapacityEnabled { get; set; }
        public bool WorkplacePortalEnabled { get; set; }
        public bool WorkplaceAccessRequestsEnabled { get; set; }
        public bool WorkplaceInductionsEnabled { get; set; }
        public bool Enforce2faEnabled { get; set; }
        public bool DisableLocalLoginEnabled { get; set; }
        public bool Disabled { get; set; }
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
