using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage;
using VisitorTabletAPITemplate.ImageStorage.Repositories;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth;
using VisitorTabletAPITemplate.Utilities;

namespace VisitorTabletAPITemplate.Features.Organizations.CreateOrganization
{
    public sealed class CreateOrganizationEndpoint : Endpoint<CreateOrganizationRequest>
    {
        private readonly AppSettings _appSettings;
        private readonly OrganizationsRepository _organizationsRepository;

        public CreateOrganizationEndpoint(AppSettings appSettings,
            OrganizationsRepository organizationsRepository)
        {
            _appSettings = appSettings;
            _organizationsRepository = organizationsRepository;
        }

        public override void Configure()
        {
            Post("/organizations/create");
            SerializerContext(CreateOrganizationContext.Default);
            Policies("Master");
            AllowFileUploads();
        }

        public override async Task HandleAsync(CreateOrganizationRequest req, CancellationToken ct)
        {
            // Validate request
            (ContentInspectorResultWithMemoryStream? organizationLogoContentInspectorResult, ContentInspectorResultWithMemoryStream? buildingFeatureImageContentInspectorResult) = await ValidateInputAsync(req);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            (Guid? userId, string? adminUserDisplayName) = User.GetIdAndName();
            string? remoteIpAddress = HttpContext.Connection.RemoteIpAddress?.ToString();

            // Query data
            (SqlQueryResult queryResult, Organization? organization, List<OrganizationDomainCollision>? organizationDomainCollisions) = await _organizationsRepository.CreateOrganizationAsync(req, organizationLogoContentInspectorResult, buildingFeatureImageContentInspectorResult, userId, adminUserDisplayName, remoteIpAddress);

            // Validate result
            ValidateOutput(queryResult, organizationDomainCollisions);

            // Stop if validation failed
            if (ValidationFailed)
            {
                await SendErrorsAsync();
                return;
            }

            await SendAsync(organization!);
        }

        private async Task<(ContentInspectorResultWithMemoryStream? organizationLogoContentInspectorResult, ContentInspectorResultWithMemoryStream? buildingFeatureImageContentInspectorResult)> ValidateInputAsync(CreateOrganizationRequest req)
        {
            ContentInspectorResultWithMemoryStream? organizationLogoContentInspectorResult = null;
            ContentInspectorResultWithMemoryStream? buildingFeatureImageContentInspectorResult = null;
            HashSet<string> uniqueDomains = new HashSet<string>();

            // Trim strings
            req.Name = req.Name?.Trim();
            req.RegionName = req.RegionName?.Trim();
            req.BuildingName = req.BuildingName?.Trim();
            req.BuildingAddress = req.BuildingAddress?.Trim();
            req.BuildingTimezone = req.BuildingTimezone?.Trim();
            req.BuildingFacilitiesManagementEmail = req.BuildingFacilitiesManagementEmail?.Trim();
            req.BuildingFacilitiesManagementEmailDisplayName = req.BuildingFacilitiesManagementEmailDisplayName?.Trim();
            req.BuildingCheckInQRCode = req.BuildingCheckInQRCode?.Trim();
            req.BuildingAccessCardCheckInWithBookingMessage = req.BuildingAccessCardCheckInWithBookingMessage?.Trim();
            req.BuildingAccessCardCheckInWithoutBookingMessage = req.BuildingAccessCardCheckInWithoutBookingMessage?.Trim();
            req.BuildingQRCodeCheckInWithBookingMessage = req.BuildingQRCodeCheckInWithBookingMessage?.Trim();
            req.BuildingQRCodeCheckInWithoutBookingMessage = req.BuildingQRCodeCheckInWithoutBookingMessage?.Trim();
            req.BuildingCheckInReminderMessage = req.BuildingCheckInReminderMessage?.Trim();
            req.BuildingDeskBookingReminderMessage = req.BuildingDeskBookingReminderMessage?.Trim();
            req.FunctionName = req.FunctionName?.Trim();
            req.FunctionHtmlColor = req.FunctionHtmlColor?.Trim().ToLowerInvariant();

            // Remove duplicates
            if (req.Domains is not null)
            {
                req.Domains = Toolbox.DedupeList(req.Domains);
            }

            // Validate input

            // Organization Validation

            // Validate Name
            if (string.IsNullOrWhiteSpace(req.Name))
            {
                AddError(m => m.Name!, "Organization Name is required.", "error.organization.nameIsRequired");
            }
            else if (req.Name.Length > 100)
            {
                AddError(m => m.Name!, "Organization Name must be 100 characters or less.", "error.organization.nameLength|{\"length\":\"100\"}");
            }

            // Validate Domains
            if (req.Domains is null || req.Domains.Count == 0)
            {
                AddError(m => m.Domains!, "Email Domains is required.", "error.organization.emailDomainsIsRequired");
            }
            else if (req.Domains.Count > 25)
            {
                AddError(m => m.Domains!, "The maximum number of Email Domains is 25.", "error.organization.emailDomainsMaximum|{\"maximum\":\"25\"}");
            }
            else
            {
                for (int i = 0; i < req.Domains.Count; ++i)
                {
                    string domain = req.Domains[i];

                    // Check domain length
                    if (domain.Length > 252)
                    {
                        var errorParams = new
                        {
                            domain = $"{domain[..25]}...",
                            length = 252
                        };
                        AddError(m => m.Domains!, $"The email domain \"{domain[..25]}...\" must be 252 characters or less.", $"error.organization.emailDomainLength|{System.Text.Json.JsonSerializer.Serialize(errorParams)}");
                        continue;
                    }

                    // Validate domain is a valid DNS hostname, and that it contains a dot, but not at the start or end of the string.
                    int indexOfDot = domain.IndexOf('.');
                    if (indexOfDot == -1 || indexOfDot == 0 || indexOfDot == domain.Length - 1 || Uri.CheckHostName(domain) != UriHostNameType.Dns)
                    {
                        var errorParams = new
                        {
                            domain
                        };
                        AddError(m => m.Domains!, $"The email domain \"{domain}\" is invalid.", $"error.organization.emailDomainIsInvalid|{System.Text.Json.JsonSerializer.Serialize(errorParams)}");
                        continue;
                    }

                    // Change domain to lowercase and add to HashSet
                    uniqueDomains.Add(domain.ToLowerInvariant());
                }
            }

            // Validate LogoImage
            if (req.LogoImage is not null)
            {
                if (req.LogoImage.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.LogoImage!, $"Organization Logo maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.organization.maximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    organizationLogoContentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.LogoImage);

                    if (organizationLogoContentInspectorResult is null
                        || organizationLogoContentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidAnyVectorOrImageExtension(organizationLogoContentInspectorResult.InspectedExtension))
                    {
                        AddError(m => m.LogoImage!, $"Organization Logo image should be one of the following formats: {ImageStorageHelpers.ValidAnyVectorOrImageFormats}",
                            "error.organization.invalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidAnyVectorOrImageFormats + "\"}");
                    }
                }
            }

            // Validate Disabled
            if (!req.Disabled.HasValue)
            {
                AddError(m => m.Disabled!, "Disabled is required.", "error.organization.disabledIsRequired");
            }

            // Validate AutomaticUserInactivityEnabled
            if (!req.AutomaticUserInactivityEnabled.HasValue)
            {
                AddError(m => m.AutomaticUserInactivityEnabled!, "Automatic User Inactivity is required.", "error.organization.automaticUserInactivityEnabledIsRequired");
            }

            // Validate CheckInEnabled
            if (!req.CheckInEnabled.HasValue)
            {
                AddError(m => m.CheckInEnabled!, "Check-In is required.", "error.organization.checkInEnabledIsRequired");
            }

            // Validate MaxCapacityEnabled
            if (!req.MaxCapacityEnabled.HasValue)
            {
                AddError(m => m.MaxCapacityEnabled!, "Max Capacity is required.", "error.organization.maxCapacityEnabledIsRequired");
            }

            // Validate WorkplacePortalEnabled
            if (!req.WorkplacePortalEnabled.HasValue)
            {
                AddError(m => m.WorkplacePortalEnabled!, "Workplace Portal is required.", "error.organization.workplacePortalEnabledIsRequired");
            }

            // Validate WorkplaceAccessRequestsEnabled
            if (!req.WorkplaceAccessRequestsEnabled.HasValue)
            {
                AddError(m => m.WorkplaceAccessRequestsEnabled!, "Workplace Access Requests is required.", "error.organization.workplaceAccessRequestsEnabledIsRequired");
            }

            // Validate WorkplaceInductionsEnabled
            if (!req.WorkplaceInductionsEnabled.HasValue)
            {
                AddError(m => m.WorkplaceInductionsEnabled!, "Workplace Inductions is required.", "error.organization.workplaceInductionsEnabledIsRequired");
            }

            // Validate Enforce2faEnabled
            if (!req.Enforce2faEnabled.HasValue)
            {
                AddError(m => m.Enforce2faEnabled!, "Enforce Two-Factor Authentication is required.", "error.organization.enforce2faEnabledIsRequired");
            }

            // Validate DisableLocalLoginEnabled
            if (!req.DisableLocalLoginEnabled.HasValue)
            {
                AddError(m => m.DisableLocalLoginEnabled!, "Disable Local Login is required.", "error.organization.disableLocalLoginIsRequired");
            }

            // Region Validation

            // Validate RegionName
            if (string.IsNullOrWhiteSpace(req.RegionName))
            {
                AddError(m => m.RegionName!, "Region Name is required.", "error.region.nameIsRequired");
            }
            else if (req.RegionName.Length > 100)
            {
                AddError(m => m.RegionName!, "Region Name must be 100 characters or less.", "error.region.nameLength|{\"length\":\"100\"}");
            }

            // Building Validation

            // Validate BuildingFeatureImage
            if (req.BuildingFeatureImage is not null)
            {
                if (req.BuildingFeatureImage.Length > _appSettings.ImageUpload.MaxFilesizeBytes)
                {
                    AddError(m => m.BuildingFeatureImage!, $"Building Feature Image maximum image filesize is {_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB.",
                        "error.building.featureImageMaximumImageFilesize|{\"filesize\":\"" + $"{_appSettings.ImageUpload.MaxFilesizeBytes / 1048576M:0.##}MB" + "\"}");
                }
                else
                {
                    buildingFeatureImageContentInspectorResult = await ImageStorageHelpers.CopyFormFileContentAndInspectImageAsync(req.BuildingFeatureImage);

                    if (buildingFeatureImageContentInspectorResult is null
                        || buildingFeatureImageContentInspectorResult.InspectedExtension is null
                        || !ImageStorageHelpers.IsValidImageExtension(buildingFeatureImageContentInspectorResult.InspectedExtension))
                    {
                        AddError(m => m.BuildingFeatureImage!, $"Building Feature Image should be one of the following formats: {ImageStorageHelpers.ValidImageFormats}",
                            "error.building.featureImageInvalidImageFormat|{\"validImageFormats\":\"" + ImageStorageHelpers.ValidImageFormats + "\"}");
                    }
                }
            }

            // Validate BuildingName
            if (string.IsNullOrWhiteSpace(req.BuildingName))
            {
                AddError(m => m.BuildingName!, "Building Name is required.", "error.building.nameIsRequired");
            }
            else if (req.BuildingName.Length > 100)
            {
                AddError(m => m.BuildingName!, "Building Name must be 100 characters or less.", "error.building.nameLength|{\"length\":\"100\"}");
            }

            // Validate BuildingAddress
            if (string.IsNullOrWhiteSpace(req.BuildingAddress))
            {
                AddError(m => m.BuildingAddress!, "Address is required.", "error.building.addressIsRequired");
            }
            else if (req.BuildingAddress.Length > 250)
            {
                AddError(m => m.BuildingAddress!, "Address must be 250 characters or less.", "error.building.addressLength|{\"length\":\"250\"}");
            }

            // Validate BuildingLatitude
            if (!req.BuildingLatitude.HasValue)
            {
                AddError(m => m.BuildingLatitude!, "Latitude is required.", "error.building.latitudeIsRequired");
            }
            else if (!Toolbox.IsValidLatitude((float)req.BuildingLatitude.Value))
            {
                AddError(m => m.BuildingLatitude!, "Latitude must be between -90 and 90.", "error.building.latitudeIsInvalid");
            }

            // Validate BuildingLongitude
            if (!req.BuildingLongitude.HasValue)
            {
                AddError(m => m.BuildingLongitude!, "Longitude is required.", "error.building.longitudeIsRequired");
            }
            else if (!Toolbox.IsValidLongtitude((float)req.BuildingLongitude.Value))
            {
                AddError(m => m.BuildingLongitude!, "Longitude must be between -180 and 180.", "error.building.longitudeIsInvalid");
            }

            // Validate BuildingTimezone
            if (string.IsNullOrWhiteSpace(req.BuildingTimezone))
            {
                AddError(m => m.BuildingTimezone!, "Timezone is required.", "error.building.timezoneIsRequired");
            }
            else
            {
                if (req.BuildingTimezone.Length > 50)
                {
                    AddError(m => m.BuildingTimezone!, "Timezone must be 50 characters or less.", "error.building.timezoneLength|{\"length\":\"50\"}");
                }
                else if (!Toolbox.IsValidTimezone(req.BuildingTimezone))
                {
                    AddError(m => m.BuildingTimezone!, "The specified timezone is not a valid timezone.", "error.building.timezoneIsInvalid");
                }
            }

            // Validate BuildingFacilitiesManagementEmail
            if (string.IsNullOrWhiteSpace(req.BuildingFacilitiesManagementEmail))
            {
                AddError(m => m.BuildingFacilitiesManagementEmail!, "Facilities Management Email is required.", "error.building.facilitiesManagementEmailIsRequired");
            }
            else
            {
                if (req.BuildingFacilitiesManagementEmail.Length > 254)
                {
                    AddError(m => m.BuildingFacilitiesManagementEmail!, "Facilities Management Email must be 254 characters or less.", "error.building.facilitiesManagementEmailLength|{\"length\":\"254\"}");
                }
                else if (!Toolbox.IsValidEmail(req.BuildingFacilitiesManagementEmail))
                {
                    AddError(m => m.BuildingFacilitiesManagementEmail!, "Facilities Management Email format is invalid.", "error.building.facilitiesManagementEmailIsInvalid");
                }
            }

            // Validate BuildingFacilitiesManagementEmailDisplayName
            if (string.IsNullOrWhiteSpace(req.BuildingFacilitiesManagementEmailDisplayName))
            {
                AddError(m => m.BuildingFacilitiesManagementEmailDisplayName!, "Facilities Management Email Display Name is required.", "error.building.facilitiesManagementEmailDisplayNameIsRequired");
            }
            else
            {
                if (req.BuildingFacilitiesManagementEmailDisplayName.Length > 151)
                {
                    AddError(m => m.BuildingFacilitiesManagementEmailDisplayName!, "Facilities Management Email Display Name must be 151 characters or less.", "error.building.facilitiesManagementEmailDisplayNameLength|{\"length\":\"151\"}");
                }
            }

            // Validate BuildingCheckInEnabled
            if (!req.BuildingCheckInEnabled.HasValue)
            {
                AddError(m => m.BuildingCheckInEnabled!, "Check-In Enabled is required.", "error.building.checkInEnabledIsRequired");
            }
            else if (req.BuildingCheckInEnabled.Value)
            {
                // Validate BuildingAccessCardCheckInWithBookingMessage
                if (string.IsNullOrWhiteSpace(req.BuildingAccessCardCheckInWithBookingMessage))
                {
                    AddError(m => m.BuildingAccessCardCheckInWithBookingMessage!, "Check-In Message via Access Card with Existing Booking is required.", "error.building.accessCardCheckInWithBookingMessageIsRequired");
                }
                else if (req.BuildingAccessCardCheckInWithBookingMessage.Length > 2000)
                {
                    AddError(m => m.BuildingAccessCardCheckInWithBookingMessage!, "Check-In Message via Access Card with Existing Booking must be 2000 characters or less.", "error.building.accessCardCheckInWithBookingMessageLength|{\"length\":\"2000\"}");
                }

                // Validate BuildingAccessCardCheckInWithoutBookingMessage
                if (string.IsNullOrWhiteSpace(req.BuildingAccessCardCheckInWithoutBookingMessage))
                {
                    AddError(m => m.BuildingAccessCardCheckInWithoutBookingMessage!, "Check-In Message via Access Card without Existing Booking is required.", "error.building.accessCardCheckInWithoutBookingMessageIsRequired");
                }
                else if (req.BuildingAccessCardCheckInWithoutBookingMessage.Length > 2000)
                {
                    AddError(m => m.BuildingAccessCardCheckInWithoutBookingMessage!, "Check-In Message via Access Card without Existing Booking must be 2000 characters or less.", "error.building.accessCardCheckInWithoutBookingMessageLength|{\"length\":\"2000\"}");
                }

                // Clear BuildingCheckInQRCode if not specified
                if (string.IsNullOrWhiteSpace(req.BuildingCheckInQRCode))
                {
                    req.BuildingCheckInQRCode = null;
                }

                // Validate BuildingQRCodeCheckInWithBookingMessage
                if (string.IsNullOrWhiteSpace(req.BuildingQRCodeCheckInWithBookingMessage))
                {
                    AddError(m => m.BuildingQRCodeCheckInWithBookingMessage!, "Check-In Message via QR Code with Existing Booking is required.", "error.building.qrCodeCheckInWithBookingMessageIsRequired");
                }
                else if (req.BuildingQRCodeCheckInWithBookingMessage.Length > 2000)
                {
                    AddError(m => m.BuildingQRCodeCheckInWithBookingMessage!, "Check-In Message via QR Code with Existing Booking must be 2000 characters or less.", "error.building.qrCodeCheckInWithBookingMessageLength|{\"length\":\"2000\"}");
                }

                // Validate BuildingQRCodeCheckInWithoutBookingMessage
                if (string.IsNullOrWhiteSpace(req.BuildingQRCodeCheckInWithoutBookingMessage))
                {
                    AddError(m => m.BuildingQRCodeCheckInWithoutBookingMessage!, "Check-In Message via QR Code without Existing Booking is required.", "error.building.qrCodeCheckInWithoutBookingMessageIsRequired");
                }
                else if (req.BuildingQRCodeCheckInWithoutBookingMessage.Length > 2000)
                {
                    AddError(m => m.BuildingQRCodeCheckInWithoutBookingMessage!, "Check-In Message via QR Code without Existing Booking must be 2000 characters or less.", "error.building.qrCodeCheckInWithoutBookingMessageLength|{\"length\":\"2000\"}");
                }
            }

            // Validate BuildingCheckInReminderEnabled
            if (!req.BuildingCheckInReminderEnabled.HasValue)
            {
                AddError(m => m.BuildingCheckInReminderEnabled!, "Check-In Reminder Enabled is required.", "error.building.checkInReminderEnabledIsRequired");
            }
            else if (req.BuildingCheckInReminderEnabled.Value)
            {
                // Validate BuildingCheckInReminderTime
                if (!req.BuildingCheckInReminderTime.HasValue)
                {
                    AddError(m => m.BuildingCheckInReminderTime!, "Check-In Reminder Time is required.", "error.building.checkInReminderTimeIsRequired");
                }
                else
                {
                    var minute = req.BuildingCheckInReminderTime.Value.Minute;

                    if (minute != 0 && minute != 15 && minute != 30 && minute != 45)
                    {
                        AddError(m => m.BuildingCheckInReminderTime!, "Check-In Reminder Time is invalid.", "error.building.checkInReminderTimeIsInvalid");
                    }
                }

                // Validate BuildingCheckInReminderMessage
                if (string.IsNullOrWhiteSpace(req.BuildingCheckInReminderMessage))
                {
                    AddError(m => m.BuildingCheckInReminderMessage!, "Check-In Reminder Message is required.", "error.building.checkInReminderMessageIsRequired");
                }
                else if (req.BuildingCheckInReminderMessage.Length > 2000)
                {
                    AddError(m => m.BuildingCheckInReminderMessage!, "Check-In Reminder Message must be 2000 characters or less.", "error.building.checkInReminderMessage|{\"length\":\"2000\"}");
                }
            }

            // Validate BuildingCheckInQRCode
            if (!string.IsNullOrWhiteSpace(req.BuildingCheckInQRCode) && req.BuildingCheckInQRCode.Length > 100)
            {
                AddError(m => m.BuildingCheckInQRCode!, "Check-In QR Code must be 100 characters or less.", "error.building.checkInQRCodeLength|{\"length\":\"100\"}");
            }

            // Check if Organization has automatic user inactivity enabled
            if (req.AutomaticUserInactivityEnabled.HasValue && !req.AutomaticUserInactivityEnabled.Value) // Organization.AutomaticUserInactivityEnabled
            {
                req.BuildingAutoUserInactivityEnabled = false;
                req.BuildingAutoUserInactivityDurationDays = null;
                req.BuildingAutoUserInactivityScheduledIntervalMonths = null;
                req.BuildingAutoUserInactivityScheduleStartDateUtc = null;
            }
            else
            {
                // Validate BuildingAutoUserInactivityEnabled
                if (!req.BuildingAutoUserInactivityEnabled.HasValue)
                {
                    AddError(m => m.BuildingAutoUserInactivityEnabled!, "Automatic User Inactivity Enabled is required.", "error.building.autoUserInactivityEnabledIsRequired");
                }
                else if (req.BuildingAutoUserInactivityEnabled.Value)
                {
                    // Validate BuildingAutoUserInactivityDurationDays
                    if (!req.BuildingAutoUserInactivityDurationDays.HasValue)
                    {
                        AddError(m => m.BuildingAutoUserInactivityDurationDays!, "Automatic User Inactivity Duration Days is required.", "error.building.autoUserInactivityDurationDaysIsRequired");
                    }
                    else if (req.BuildingAutoUserInactivityDurationDays.Value <= 0)
                    {
                        AddError(m => m.BuildingAutoUserInactivityDurationDays!, "Automatic User Inactivity Duration Days must be a positive integer.", "error.building.autoUserInactivityDurationDaysMustBePositiveInteger");
                    }

                    // Validate BuildingAutoUserInactivityScheduledIntervalMonths
                    if (!req.BuildingAutoUserInactivityScheduledIntervalMonths.HasValue)
                    {
                        AddError(m => m.BuildingAutoUserInactivityScheduledIntervalMonths!, "Automatic User Inactivity Scheduled Interval Months is required.", "error.building.autoUserInactivityScheduledIntervalMonthsIsRequired");
                    }
                    else if (req.BuildingAutoUserInactivityScheduledIntervalMonths.Value <= 0 || req.BuildingAutoUserInactivityScheduledIntervalMonths > 12)
                    {
                        AddError(m => m.BuildingAutoUserInactivityScheduledIntervalMonths!, "Automatic User Inactivity Scheduled Interval Months must between 1 and 12.", "error.building.autoUserInactivityScheduledIntervalMonthsMustBePositiveInteger");
                    }

                    // Validate BuildingAutoUserInactivityScheduleStartDateUtc
                    if (!req.BuildingAutoUserInactivityScheduleStartDateUtc.HasValue)
                    {
                        AddError(m => m.BuildingAutoUserInactivityScheduleStartDateUtc!, "Automatic User Inactivity Schedule Start Date is required.", "error.building.autoUserInactivityScheduleStartDateUtcIsRequired");
                    }
                    else
                    {
                        DateTime scheduleStartDateUtc = req.BuildingAutoUserInactivityScheduleStartDateUtc.Value;
                        if (scheduleStartDateUtc.Day > 28 || scheduleStartDateUtc.Day < 1)
                        {
                            AddError(m => m.BuildingAutoUserInactivityScheduleStartDateUtc!, "Automatic User Inactivity Schedule Start Date's day must be between 1st and 28th.", "error.building.autoUserInactivityScheduleStartDateUtcDayIsInvalid");
                        }
                    }
                }
            }

            // Validate BuildingMaxCapacity
            if (req.MaxCapacityEnabled.HasValue && req.MaxCapacityEnabled.Value) // Organization.MaxCapacityEnabled
            {
                if (!req.BuildingMaxCapacityEnabled.HasValue)
                {
                    AddError(m => m.BuildingMaxCapacityEnabled!, "Max Capacity Enabled is required.", "error.building.maxCapacityEnabledIsRequired");
                }
                else if (req.BuildingMaxCapacityEnabled.Value)
                {
                    // Validate BuildingMaxCapacityUsers and check int
                    if (!req.BuildingMaxCapacityUsers.HasValue)
                    {
                        AddError(m => m.BuildingMaxCapacityUsers!, "Max Capacity Users is required.", "error.building.maxCapacityIsRequired");
                    }
                    else if (req.BuildingMaxCapacityUsers.Value <= 0)
                    {
                        AddError(m => m.BuildingMaxCapacityUsers!, "Max Capacity Users must be a positive integer.", "error.building.maxCapacityMustBePositiveInteger");
                    }

                    // Validate BuildingMaxCapacityNotificationMessage
                    if (string.IsNullOrWhiteSpace(req.BuildingMaxCapacityNotificationMessage))
                    {
                        AddError(m => m.BuildingMaxCapacityNotificationMessage!, "Max Capacity Message is required.", "error.building.maxCapacityNotificationMessageIsRequired");
                    }
                    else if (req.BuildingMaxCapacityNotificationMessage.Length > 2000)
                    {
                        AddError(m => m.BuildingMaxCapacityNotificationMessage!, "Max Capacity Message must be {length} characters or less.", "error.building.maxCapacityNotificationMessageLength|{\"length\":\"2000\"}");
                    }
                }
            }
            else
            {
                // If the organization has Max Capacity disabled, then we also disable it for the building
                req.BuildingMaxCapacityEnabled = false;
            }

            // Validate DeskBookingReminderEnabled
            if (!req.BuildingDeskBookingReminderEnabled.HasValue)
            {
                AddError(m => m.BuildingDeskBookingReminderEnabled!, "Desk Booking Reminder Enabled is required.", "error.building.deskBookingReminderEnabledIsRequired");
            }
            else if (req.BuildingDeskBookingReminderEnabled.Value)
            {
                // Validate BuildingDeskBookingReminderTime
                if (!req.BuildingDeskBookingReminderTime.HasValue)
                {
                    AddError(m => m.BuildingDeskBookingReminderTime!, "Desk Booking Reminder Time is required.", "error.building.deskBookingReminderTimeIsRequired");
                }
                else
                {
                    var minute = req.BuildingDeskBookingReminderTime.Value.Minute;

                    if (minute != 0 && minute != 15 && minute != 30 && minute != 45)
                    {
                        AddError(m => m.BuildingDeskBookingReminderTime!, "Check-In Reminder Time is invalid.", "error.building.checkInReminderTimeIsInvalid");
                    }
                }

                // Validate BuildingDeskBookingReminderMessage
                if (string.IsNullOrWhiteSpace(req.BuildingDeskBookingReminderMessage))
                {
                    AddError(m => m.BuildingDeskBookingReminderMessage!, "Desk Booking Reminder Message is required.", "error.building.deskBookingReminderMessageIsRequired");
                }
                else if (req.BuildingDeskBookingReminderMessage.Length > 2000)
                {
                    AddError(m => m.BuildingDeskBookingReminderMessage!, "Desk Booking Reminder Message must be 2000 characters or less.", "error.building.deskBookingReminderMessage|{\"length\":\"2000\"}");
                }
            }

            // Validate BuildingDeskBookingReservationDateRange
            if (!req.BuildingDeskBookingReservationDateRangeEnabled.HasValue)
            {
                AddError(m => m.BuildingDeskBookingReservationDateRangeEnabled!, "Desk booking reservation date enabled is required.", "error.building.deskBookingReservationDateEnabledIsRequired");
            }
            else if (req.BuildingDeskBookingReservationDateRangeEnabled.Value)
            {
                // Validate BuildingDeskBookingReservationDateRangeForUser
                if (!req.BuildingDeskBookingReservationDateRangeForUser.HasValue)
                {
                    AddError(m => m.BuildingDeskBookingReservationDateRangeForUser!, "Desk booking reservation date range for User is required.", "error.building.deskBookingReservationDateForUserIsRequired");
                }
                else if (req.BuildingDeskBookingReservationDateRangeForUser.Value <= 0)
                {
                    AddError(m => m.BuildingDeskBookingReservationDateRangeForUser!, "Desk booking reservation date range for User must be a positive integer.", "error.building.deskBookingReservationDateForUserMustBePositiveInteger");
                }

                // Validate BuildingDeskAssetBookingReservationDateRangeForAdmin
                if (!req.BuildingDeskBookingReservationDateRangeForAdmin.HasValue)
                {
                    AddError(m => m.BuildingDeskBookingReservationDateRangeForAdmin!, "Desk booking reservation date range for Admin is required.", "error.building.deskBookingReservationDateForAdminIsRequired");
                }
                else if (req.BuildingDeskBookingReservationDateRangeForAdmin.Value <= 0)
                {
                    AddError(m => m.BuildingDeskBookingReservationDateRangeForAdmin!, "Desk booking reservation date range for Admin must be a positive integer.", "error.building.deskBookingReservationDateForAdminMustBePositiveInteger");
                }
                // Validate BuildingDeskAssetBookingReservationDateRangeForSuperAdmin
                if (!req.BuildingDeskBookingReservationDateRangeForSuperAdmin.HasValue)
                {
                    AddError(m => m.BuildingDeskBookingReservationDateRangeForSuperAdmin!, "Desk booking reservation date range for Super Admin is required.", "error.building.deskBookingReservationDateForAdminIsRequired");
                }
                else if (req.BuildingDeskBookingReservationDateRangeForSuperAdmin.Value <= 0)
                {
                    AddError(m => m.BuildingDeskBookingReservationDateRangeForSuperAdmin!, "Desk booking reservation date range for Super Admin must be a positive integer.", "error.building.deskBookingReservationDateForAdminMustBePositiveInteger");
                }
            }

            // Function Validation

            // Validate FunctionName
            if (string.IsNullOrWhiteSpace(req.FunctionName))
            {
                AddError(m => m.FunctionName!, "Function Name is required.", "error.function.nameIsRequired");
            }
            else if (req.FunctionName.Length > 100)
            {
                AddError(m => m.FunctionName!, "Function Name must be 100 characters or less.", "error.function.nameLength|{\"length\":\"100\"}");
            }

            // Validate FunctionHtmlColor
            if (string.IsNullOrWhiteSpace(req.FunctionHtmlColor))
            {
                AddError(m => m.FunctionHtmlColor!, "Floor Plan Desk Color is required.", "error.function.htmlColorIsRequired");
            }
            else if (req.FunctionHtmlColor.Length != 7)
            {
                AddError(m => m.FunctionHtmlColor!, "Floor Plan Desk Color must be 7 characters.", "error.function.htmlColorLength|{\"length\":\"7\"}");
            }

            // If validation passed, keep only unique domains
            if (!ValidationFailed)
            {
                req.Domains = uniqueDomains.ToList();
            }

            return (organizationLogoContentInspectorResult, buildingFeatureImageContentInspectorResult);
        }

        private void ValidateOutput(SqlQueryResult queryResult, List<OrganizationDomainCollision>? organizationDomainCollisions)
        {
            // Validate queried data
            switch (queryResult)
            {
                case SqlQueryResult.Ok:
                    return;
                case SqlQueryResult.RecordAlreadyExists:
                    AddError(m => m.Name!, "Another organization already exists with the specified name.", "error.organization.nameExists");
                    break;
                case SqlQueryResult.SubRecordAlreadyExists:
                    if (organizationDomainCollisions is not null)
                    {
                        foreach (OrganizationDomainCollision collision in organizationDomainCollisions)
                        {
                            var errorParams = new
                            {
                                domain = collision.DomainName,
                                organization = collision.OrganizationName ?? collision.OrganizationId.ToString()
                            };
                            AddError(m => m.Domains!, $"The email domain \"{collision.DomainName}\" already belongs to the organization \"{collision.OrganizationName ?? collision.OrganizationId.ToString()}\".",
                                $"error.organization.emailDomainBelongsToOrganization|{System.Text.Json.JsonSerializer.Serialize(errorParams)}");
                        }
                    }
                    else
                    {
                        AddError(m => m.Domains!, "Unknown existing email domain error.", "error.organization.emailDomainBelongsToOrganization.unknown");
                    }
                    break;
                default:
                    AddError("An unknown error occurred.", "error.unknown");
                    break;
            }
        }
    }
}
