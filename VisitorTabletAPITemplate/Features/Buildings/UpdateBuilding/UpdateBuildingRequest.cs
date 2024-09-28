namespace VisitorTabletAPITemplate.Features.Buildings.UpdateBuilding
{
    public sealed class UpdateBuildingRequest
    {
        public Guid? id { get; set; }
        public string? Name { get; set; }
        public Guid? OrganizationId { get; set; }
        public Guid? RegionId { get; set; }
        public string? Address { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Timezone { get; set; }
        public string? FacilitiesManagementEmail { get; set; }
        public string? FacilitiesManagementEmailDisplayName { get; set; }
        public bool? FeatureImageChanged { get; set; }
        public IFormFile? FeatureImage { get; set; }
        public bool? CheckInEnabled { get; set; }
        public string? CheckInQRCode { get; set; }
        public string? AccessCardCheckInWithBookingMessage { get; set; }
        public string? AccessCardCheckInWithoutBookingMessage { get; set; }
        public string? QRCodeCheckInWithBookingMessage { get; set; }
        public string? QRCodeCheckInWithoutBookingMessage { get; set; }
        public bool? CheckInReminderEnabled { get; set; }
        public TimeOnly? CheckInReminderTime { get; set; }
        public string? CheckInReminderMessage { get; set; }
        public bool? AutoUserInactivityEnabled { get; set; }
        public int? AutoUserInactivityDurationDays { get; set; }
        public int? AutoUserInactivityScheduledIntervalMonths { get; set; }
        public DateTime? AutoUserInactivityScheduleStartDateUtc { get; set; }
        public bool? MaxCapacityEnabled { get; set; }
        public int? MaxCapacityUsers { get; set; }
        public string? MaxCapacityNotificationMessage { get; set; }
        public bool? DeskBookingReminderEnabled { get; set; }
        public TimeOnly? DeskBookingReminderTime { get; set; }
        public string? DeskBookingReminderMessage { get; set; }
        public bool? DeskBookingReservationDateRangeEnabled { get; set; }
        public int? DeskBookingReservationDateRangeForUser { get; set; }
        public int? DeskBookingReservationDateRangeForAdmin { get; set; }
        public int? DeskBookingReservationDateRangeForSuperAdmin { get; set; }
        public byte[]? ConcurrencyKey { get; set; }
    }
}
