namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class UserDataForUserManagement
    {
        // User Data
        public Guid Uid { get; set; }
        public DateTime InsertDateUtc { get; set; }
        public DateTime UpdatedDateUtc { get; set; }
        public DateTime? LastAccessDateUtc { get; set; }
        public DateTime? LastPasswordChangeDateUtc { get; set; }
        public string Email { get; set; } = default!;
        public bool HasPassword { get; set; }
        public bool TotpEnabled { get; set; }
        public string? DisplayName { get; set; }
        public string? FirstName { get; set; }
        public string? Surname { get; set; }
        public string? Timezone { get; set; }
        public string? AvatarUrl { get; set; }
        public Guid? AvatarImageStorageId { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
        public Guid? AvatarThumbnailStorageId { get; set; }
        public bool Disabled { get; set; }
        public byte[]? ConcurrencyKey { get; set; }

        // Organization Data
        public Guid OrganizationId { get; set; }
        public int UserOrganizationRole { get; set; }
        public string? Note { get; set; }
        public bool Contractor { get; set; }
        public bool Visitor { get; set; }
        public bool UserOrganizationDisabled { get; set; }
        public DateTime AccessGivenDateUtc { get; set; }
        public Guid? PermanentDeskId { get; set; }
        public string? PermanentDeskName { get; set; }
        public Guid? PermanentDeskFloorId { get; set; }
        public string? PermanentDeskFloorName { get; set; }
        public Guid? PermanentDeskBuildingId { get; set; }
        public string? PermanentDeskBuildingName { get; set; }
        public List<UserDataForUserManagement_Building> Buildings { get; set; } = new List<UserDataForUserManagement_Building>();
    }

    public sealed class UserDataForUserManagement_Building
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid FunctionId { get; set; }
        public string FunctionName { get; set; } = default!;
        public string FunctionHtmlColor { get; set; } = default!;
        public bool FirstAidOfficer { get; set; }
        public bool FireWarden { get; set; }
        public bool PeerSupportOfficer { get; set; }
        public bool AllowBookingDeskForVisitor { get; set; }
        public bool AllowBookingRestrictedRooms { get; set; }
        public bool AllowBookingAnyoneAnywhere { get; set; }
        public DateTime AccessGivenDateUtc { get; set; }
        public List<UserDataForUserManagement_AssetType> AssetTypes { get; set; } = new List<UserDataForUserManagement_AssetType>();
        public List<UserDataForUserManagement_AdminFunction> AdminFunctions { get; set; } = new List<UserDataForUserManagement_AdminFunction>();
        public List<UserDataForUserManagement_AdminAssetType> AdminAssetTypes { get; set; } = new List<UserDataForUserManagement_AdminAssetType>();
    }

    public sealed class UserDataForUserManagement_AssetType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
        public string? LogoImageUrl { get; set; }
        public Guid? PermanentAssetSlotId { get; set; }
        public string? PermanentAssetSlotName { get; set; }
        public Guid? PermanentAssetSlotAssetSectionId { get; set; }
        public string? PermanentAssetSlotAssetSectionName { get; set; }
    }

    public sealed class UserDataForUserManagement_AdminFunction
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
    }

    public sealed class UserDataForUserManagement_AdminAssetType
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = default!;
        public Guid BuildingId { get; set; }
        public string? LogoImageUrl { get; set; }
    }
}
