namespace VisitorTabletAPITemplate.Features.Organizations.CreateOrganization
{
    public sealed class CreateOrganizationRequest
    {
        // Organization Details
        public string? Name { get; set; }
        public List<string>? Domains { get; set; }
        public IFormFile? LogoImage { get; set; }
        public bool? AutomaticUserInactivityEnabled { get; set; }
        public bool? CheckInEnabled { get; set; }
        public bool? MaxCapacityEnabled { get; set; }
        public bool? WorkplacePortalEnabled { get; set; }
        public bool? WorkplaceAccessRequestsEnabled { get; set; }
        public bool? WorkplaceInductionsEnabled { get; set; }
        public bool? Enforce2faEnabled { get; set; }
        public bool? DisableLocalLoginEnabled { get; set; }
        public bool? Disabled { get; set; }

        // Region Details
        public string? RegionName { get; set; }

        // Building Details
        public string? BuildingName { get; set; }
        public string? BuildingAddress { get; set; }
        public decimal? BuildingLatitude { get; set; }
        public decimal? BuildingLongitude { get; set; }
        public string? BuildingTimezone { get; set; }
        public string? BuildingFacilitiesManagementEmail { get; set; }
        public string? BuildingFacilitiesManagementEmailDisplayName { get; set; }
        public IFormFile? BuildingFeatureImage { get; set; }
        public bool? BuildingCheckInEnabled { get; set; }
        public string? BuildingCheckInQRCode { get; set; }
        public string? BuildingAccessCardCheckInWithBookingMessage { get; set; }
        public string? BuildingAccessCardCheckInWithoutBookingMessage { get; set; }
        public string? BuildingQRCodeCheckInWithBookingMessage { get; set; }
        public string? BuildingQRCodeCheckInWithoutBookingMessage { get; set; }
        public bool? BuildingCheckInReminderEnabled { get; set; }
        public TimeOnly? BuildingCheckInReminderTime { get; set; }
        public string? BuildingCheckInReminderMessage { get; set; }
        public bool? BuildingAutoUserInactivityEnabled { get; set; }
        public int? BuildingAutoUserInactivityDurationDays { get; set; }
        public int? BuildingAutoUserInactivityScheduledIntervalMonths { get; set; }
        public DateTime? BuildingAutoUserInactivityScheduleStartDateUtc { get; set; }
        public bool? BuildingMaxCapacityEnabled { get; set; }
        public int? BuildingMaxCapacityUsers { get; set; }
        public string? BuildingMaxCapacityNotificationMessage { get; set; }
        public bool? BuildingDeskBookingReminderEnabled { get; set; }
        public TimeOnly? BuildingDeskBookingReminderTime { get; set; }
        public string? BuildingDeskBookingReminderMessage { get; set; }
        public bool? BuildingDeskBookingReservationDateRangeEnabled { get; set; }
        public int? BuildingDeskBookingReservationDateRangeForUser { get; set; }
        public int? BuildingDeskBookingReservationDateRangeForAdmin { get; set; }
        public int? BuildingDeskBookingReservationDateRangeForSuperAdmin { get; set; }

        // Function Details
        public string? FunctionName { get; set; }
        public string? FunctionHtmlColor { get; set; }
    }
}
