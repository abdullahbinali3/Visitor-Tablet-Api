namespace VisitorTabletAPITemplate.FileStorage.Enums
{
    // Used in database in tblFileStorage table - RelatedObjectType column

    // NOTE!!! Any changes to this should also be made in the following application:
    // SspPdfSanitizerService
    // Which is in the same git repository, and must be updated and deployed to live
    // on the "ocms-dc01" server inside the Azure resource group.
    // To access this server, mstsc to one of the iis servers then to ocms-dc01.

    public enum FileStorageRelatedObjectType
    {
        Unspecified = 0,
        UserGuideUnsanitized = 1,
        UserGuide = 2,
        WorkplaceReportAnIssueRequestAttachment = 3,
        WorkplaceTilePdfUnsanitized = 4,
        WorkplaceTilePdf = 5,
        WorkplaceTileVideoUnsanitized = 6, // Placeholder, videos are currently not processed
        WorkplaceTileVideo = 7,
        WorkplaceInductionPdfUnsanitized = 8,
        WorkplaceInductionPdf = 9,
        WorkplaceInductionVideoUnsanitized = 10,
        WorkplaceInductionVideo = 11,
        WorkplaceEmailTemplateAttachment = 12
    }
}
