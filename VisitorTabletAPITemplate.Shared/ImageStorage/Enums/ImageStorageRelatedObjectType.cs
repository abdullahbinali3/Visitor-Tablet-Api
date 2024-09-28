namespace VisitorTabletAPITemplate.ImageStorage.Enums
{
    // Used in database in tblImageStorage table - RelatedObjectType column
    public enum ImageStorageRelatedObjectType
    {
        Unspecified = 0,
        OrganizationLogo = 1,
        UserAvatar = 2,
        SSFloorPlan = 3,
        SSFloorPlanThumbnail = 4,
        SSFloorFeatureImage = 5,
        SSFloorFeatureImageThumbnail = 6,
        SSBuildingFeatureImage = 7,
        WorkplacePortalFloorPlan = 8,
        WorkplacePortalFloorPlanThumbnail = 9,
        SSMeetingRoomFeatureImage = 10,
        SSMeetingRoomFeatureImageThumbnail = 11,
        SSSectionPlan = 12,
        SSSectionPlanThumbnail = 13,
        SSSectionFeatureImage = 14,
        SSSectionFeatureImageThumbnail = 15,
        SSBuildingMapImage = 16,
        SSBuildingMapImageThumbnail = 17,
        WorkplacePortalTileIcon = 18,
        WorkplacePortalSubTileIcon = 19,
        WorkplacePortalBuildingIndexIcon = 20,
        WorkplacePortalKeyDrawIcon = 21,
        UserAvatarThumbnail = 22,
        SSBuildingFeatureImageThumbnail = 23,
        AssetTypeIcon = 24,
        AzureProfilePhoto = 25,
        TinyMCEEditorImage = 255
    }
}
