namespace VisitorTabletAPITemplate.Models
{
    public sealed class OrganizationAutoUserInactivitySetting
    {
        public Guid OrganizationId { get; set; }
        public string OrganizationName { get; set; } = default!;
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public bool AutoUserInactivityEnabled { get; set; }
        public int AutoUserInactivityDurationDays { get; set; }
        public int AutoUserInactivityScheduledIntervalMonths { get; set; }
        public DateTime AutoUserInactivityScheduleStartDateUtc { get; set; }
        public DateTime? AutoUserInactivityScheduleLastRunDateUtc { get; set; }
        public byte[] ConcurrencyKey { get; set; } = default!;
    }
}
