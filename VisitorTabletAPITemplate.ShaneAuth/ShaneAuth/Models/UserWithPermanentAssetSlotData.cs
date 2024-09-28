namespace VisitorTabletAPITemplate.ShaneAuth.Models
{
    public sealed class UserWithPermanentAssetSlotData
    {
        public Guid Uid { get; set; }
        public string Email { get; set; } = default!;
        public string? DisplayName { get; set; }
        public string? AvatarThumbnailUrl { get; set; }
        public Guid? PermanentAssetSlotId { get; set; }
        public string? PermanentAssetSlotName { get; set; }
        public Guid? PermanentAssetSlotAssetSectionId { get; set; }
        public string? PermanentAssetSlotAssetSectionName { get; set; }
        public Guid? PermanentAssetSlotAssetTypeId { get; set; }
        public string? PermanentAssetSlotAssetTypeName { get; set; }
        public string? PermanentAssetSlotLocation { get; set; }
        public bool Disabled { get; set; }
    }
}
