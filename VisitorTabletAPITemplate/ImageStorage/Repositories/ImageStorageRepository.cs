using Dapper;
using Microsoft.Data.SqlClient;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ImageStorage.Enums;
using VisitorTabletAPITemplate.ImageStorage.Models;
using VisitorTabletAPITemplate.ImageStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using Svg.Skia;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace VisitorTabletAPITemplate.ImageStorage.Repositories
{
    public sealed class ImageStorageRepository
    {
        private readonly AppSettings _appSettings;
        private readonly IWebHostEnvironment _webEnvironment;

        public ImageStorageRepository(AppSettings appSettings,
            IWebHostEnvironment webEnvironment)
        {
            _appSettings = appSettings;
            _webEnvironment = webEnvironment;
        }

        public async Task<(SqlQueryResult, StoredImageFile?)> WriteImageAsync(int imgWidth, int imgHeight, bool maintainAspectRatio,
            bool growImage, ContentInspectorResultWithMemoryStream? contentInspectorResult, ImageStorageRelatedObjectType relatedObjectType,
            Guid? relatedObjectId, Guid organizationId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress,
            string? saveFileType = "webp")
        {
            if (contentInspectorResult == null
                || contentInspectorResult.InspectedExtension is null
                || contentInspectorResult.FileDataStream is null
                || !ImageStorageHelpers.IsValidImageExtension(contentInspectorResult.InspectedExtension))
            {
                return (SqlQueryResult.InvalidFileType, null);
            }

            int fileSizeBytes = -1;

            string folderPathOnDisk = GetFolderForImageTypeWithWebRootPath(organizationId, relatedObjectType);
            string fileNameOnDisk = $"{GetRandomFilename()}.{saveFileType}";
            string filePathOnDisk = Path.Combine(folderPathOnDisk, fileNameOnDisk);
            Directory.CreateDirectory(folderPathOnDisk);

            string fileUrlFolder = GetFolderForImageType(organizationId, relatedObjectType);
            string fileUrl = $"/{Path.Combine(fileUrlFolder, fileNameOnDisk).Replace("\\", "/")}";

            contentInspectorResult.FileDataStream.Position = 0;

            using (Image image = Image.Load(contentInspectorResult.FileDataStream))
            {
                // If we want the image to grow, or the image is too large, resize to fit within max dimensions
                if (growImage || image.Width > imgWidth || image.Height > imgHeight)
                {
                    // When resizing, maintain the aspect ratio if needed.
                    if (maintainAspectRatio)
                    {
                        image.Mutate(x => x.Resize(new ResizeOptions
                        {
                            Size = new Size(imgWidth, imgHeight),
                            TargetRectangle = new Rectangle(0, 0, imgWidth, imgHeight),
                            Mode = ResizeMode.Max
                        }));
                    }
                    else
                    {
                        image.Mutate(x => x.Resize(imgWidth, imgHeight));
                    }

                    imgWidth = image.Width;
                    imgHeight = image.Height;
                }

                await image.SaveAsync(filePathOnDisk);

                // Get the filesize
                FileInfo fi = new FileInfo(filePathOnDisk);

                if (fi.Exists)
                {
                    fileSizeBytes = (int)fi.Length;
                }
                else
                {
                    return (SqlQueryResult.UnknownError, null);
                }
            }

            contentInspectorResult.FileDataStream.Position = 0;

            string logDescription = "Create Image";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

insert into tblImageStorage
(id
,InsertDateUtc
,UpdatedDateUtc
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,MimeType
,FileUrl
,FileSizeBytes
,Width
,Height)
select @id
      ,@_now
      ,@_now
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
      ,@width
      ,@height

insert into tblImageStorage_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,ImageStorageId
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,MimeType
,FileUrl
,FileSizeBytes
,Width
,Height
,Deleted
,LogAction
,CascadeFrom
,CascadeLogId)
select @logid
      ,@_now
      ,@adminUserUid
      ,@adminUserDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@id
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
      ,@width
      ,@height
      ,0 -- Deleted
      ,'Insert' -- LogAction
      ,@cascadeFrom
      ,@cascadeLogId

-- Select row to return with the API result
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
from tblImageStorage
where id = @id
";
                Guid newImageId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", newImageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@relatedObjectType", relatedObjectType, DbType.Byte, ParameterDirection.Input);
                parameters.Add("@relatedObjectId", relatedObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@mimeType", "image/webp", DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileUrl", fileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileSizeBytes", fileSizeBytes, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@width", imgWidth, DbType.Int16, ParameterDirection.Input);
                parameters.Add("@height", imgHeight, DbType.Int16, ParameterDirection.Input);
                parameters.Add("@cascadeFrom", cascadeFrom, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                StoredImageFile result = await sqlConnection.QueryFirstAsync<StoredImageFile>(sql, parameters);

                return (SqlQueryResult.Ok, result);
            }
        }

        public async Task<(SqlQueryResult, StoredImageFile? image, StoredImageFile? thumbnail)> WriteImageAndThumbnailAsync(
            int imgWidth, int imgHeight, int thumbnailImgWidth, int thumbnailImgHeight, bool maintainAspectRatio,
            bool growImage, ContentInspectorResultWithMemoryStream? contentInspectorResult,
            ImageStorageRelatedObjectType relatedObjectType,
            ImageStorageRelatedObjectType relatedObjectTypeThumbnail,
            Guid? relatedObjectId, Guid organizationId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress,
            string? saveFileType = "webp")
        {
            // Write the image
            (SqlQueryResult sqlQueryResult, StoredImageFile? storedImageFile) = await WriteImageAsync(
                imgWidth, imgHeight, maintainAspectRatio, growImage, contentInspectorResult, relatedObjectType,
                relatedObjectId, organizationId, cascadeFrom, cascadeLogId, adminUserUid, adminUserDisplayName, remoteIpAddress, saveFileType);

            // If it failed, stop here.
            if (sqlQueryResult != SqlQueryResult.Ok)
            {
                return (sqlQueryResult, storedImageFile, null);
            }

            // Write the thumnail
            (SqlQueryResult sqlQueryResultThumbnail, StoredImageFile? storedImageFileThumbnail) = await WriteImageAsync(
                thumbnailImgWidth,
                thumbnailImgHeight,
                true, false, contentInspectorResult, relatedObjectTypeThumbnail,
                relatedObjectId, organizationId, cascadeFrom, cascadeLogId, adminUserUid, adminUserDisplayName, remoteIpAddress, "webp"); // always use webp for thumbnail

            // Return the result whether successful or not.
            return (sqlQueryResultThumbnail, storedImageFile, storedImageFileThumbnail);
        }

        public async Task<(SqlQueryResult, StoredImageFile?)> WriteSvgImageAsync(short precalculatedImgWidthForLogging,
            short precalculatedImgHeightForLogging,
            ContentInspectorResultWithMemoryStream? contentInspectorResult,
            ImageStorageRelatedObjectType relatedObjectType,
            Guid? relatedObjectId, Guid organizationId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            if (contentInspectorResult == null
                || contentInspectorResult.InspectedExtension is null
                || contentInspectorResult.FileDataStream is null
                || !ImageStorageHelpers.IsValidVectorImageExtension(contentInspectorResult.InspectedExtension))
            {
                return (SqlQueryResult.InvalidFileType, null);
            }

            string folderPathOnDisk = GetFolderForImageTypeWithWebRootPath(organizationId, relatedObjectType);
            string fileNameOnDisk = $"{GetRandomFilename()}.svg";
            string filePathOnDisk = Path.Combine(folderPathOnDisk, fileNameOnDisk);
            Directory.CreateDirectory(folderPathOnDisk);

            string fileUrlFolder = GetFolderForImageType(organizationId, relatedObjectType);
            string fileUrl = $"/{Path.Combine(fileUrlFolder, fileNameOnDisk).Replace("\\", "/")}";

            contentInspectorResult.FileDataStream.Position = 0;

            // If file type is SVG, sanitize the file and overwrite the FileDataStream
            if (!contentInspectorResult.IsSanitized && contentInspectorResult.InspectedExtension == "svg")
            {
                using (StreamReader sr = new StreamReader(contentInspectorResult.FileDataStream))
                {
                    string svgContent = sr.ReadToEnd();

                    string sanitized = SvgSanitizer.Sanitize(svgContent);
                    contentInspectorResult.FileDataStream = new MemoryStream(Encoding.UTF8.GetBytes(sanitized));
                    contentInspectorResult.IsSanitized = true;

                    contentInspectorResult.FileDataStream.Position = 0;
                }
            }

            // Save file to disk
            using (FileStream fileStream = File.Create(filePathOnDisk))
            {
                await contentInspectorResult.FileDataStream.CopyToAsync(fileStream);
                contentInspectorResult.FileDataStream.Position = 0;
            }

            // Get the filesize
            FileInfo fi = new FileInfo(filePathOnDisk);


            int fileSizeBytes;
            if (fi.Exists)
            {
                fileSizeBytes = (int)fi.Length;
            }
            else
            {
                return (SqlQueryResult.UnknownError, null);
            }

            // Write to database

            contentInspectorResult.FileDataStream.Position = 0;

            string logDescription = "Create Image";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

insert into tblImageStorage
(id
,InsertDateUtc
,UpdatedDateUtc
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,MimeType
,FileUrl
,FileSizeBytes
,Width
,Height)
select @id
      ,@_now
      ,@_now
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
      ,@width
      ,@height

insert into tblImageStorage_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,ImageStorageId
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,MimeType
,FileUrl
,FileSizeBytes
,Width
,Height
,Deleted
,LogAction
,CascadeFrom
,CascadeLogId)
select @logid
      ,@_now
      ,@adminUserUid
      ,@adminUserDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@id
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
      ,@width
      ,@height
      ,0 -- Deleted
      ,'Insert' -- LogAction
      ,@cascadeFrom
      ,@cascadeLogId

-- Select row to return with the API result
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
from tblImageStorage
where id = @id
";
                Guid newImageId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", newImageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@relatedObjectType", relatedObjectType, DbType.Byte, ParameterDirection.Input);
                parameters.Add("@relatedObjectId", relatedObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@mimeType", "image/svg+xml", DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileUrl", fileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileSizeBytes", fileSizeBytes, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@width", precalculatedImgWidthForLogging, DbType.Int16, ParameterDirection.Input);
                parameters.Add("@height", precalculatedImgHeightForLogging, DbType.Int16, ParameterDirection.Input);
                parameters.Add("@cascadeFrom", cascadeFrom, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                StoredImageFile result = await sqlConnection.QueryFirstAsync<StoredImageFile>(sql, parameters);

                return (SqlQueryResult.Ok, result);
            }
        }

        public async Task<(SqlQueryResult, StoredImageFile?, short? originalImgWidth, short? originalImgHeight)> WriteSvgRasterImageAsync(int imgWidth, int imgHeight,
            bool growImage, ContentInspectorResultWithMemoryStream? contentInspectorResult, ImageStorageRelatedObjectType relatedObjectType,
            Guid? relatedObjectId, Guid organizationId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress,
            string? saveFileType = "webp")
        {
            if (contentInspectorResult == null
                || contentInspectorResult.InspectedExtension is null
                || contentInspectorResult.FileDataStream is null
                || !ImageStorageHelpers.IsValidVectorImageExtension(contentInspectorResult.InspectedExtension))
            {
                return (SqlQueryResult.InvalidFileType, null, null, null);
            }

            int fileSizeBytes = -1;

            string folderPathOnDisk = GetFolderForImageTypeWithWebRootPath(organizationId, relatedObjectType);
            string fileNameOnDisk = $"{GetRandomFilename()}.{saveFileType}";
            string filePathOnDisk = Path.Combine(folderPathOnDisk, fileNameOnDisk);
            Directory.CreateDirectory(folderPathOnDisk);

            string fileUrlFolder = GetFolderForImageType(organizationId, relatedObjectType);
            string fileUrl = $"/{Path.Combine(fileUrlFolder, fileNameOnDisk).Replace("\\", "/")}";

            contentInspectorResult.FileDataStream.Position = 0;

            short? originalImgWidth = null;
            short? originalImgHeight = null;

            using (SKSvg svg = new SKSvg())
            {
                if (svg.Load(contentInspectorResult.FileDataStream) is { } && svg.Picture is not null)
                {
                    float svgWidth = svg.Picture.CullRect.Width;
                    float svgHeight = svg.Picture.CullRect.Height;

                    originalImgWidth = (short)svgWidth;
                    originalImgHeight = (short)svgHeight;

                    float scaleX = 1f;
                    float scaleY = 1f;

                    // (Maintains aspect ratio)
                    // If we don't want to grow the image, and only scale if it is too big
                    if ((growImage && (svgWidth != imgWidth || svgHeight != imgHeight)) || (!growImage && (svgWidth > imgWidth || svgHeight > imgHeight)))
                    {
                        SizeF newSize = ResizeFit(new SizeF(svgWidth, svgHeight), new SizeF(imgWidth, imgHeight), growImage);

                        scaleX = newSize.Width / svgWidth;
                        scaleY = newSize.Height / svgHeight;
                    }

                    // Save the file
                    using (FileStream fileStream = File.Create(filePathOnDisk))
                    {
                        switch (saveFileType?.ToLowerInvariant())
                        {
                            case "png":
                                svg.Save(fileStream, SKColors.Empty, SKEncodedImageFormat.Png, 100, scaleX, scaleY);
                                break;
                            case "webp":
                                svg.Save(fileStream, SKColors.Empty, SKEncodedImageFormat.Webp, 100, scaleX, scaleY);
                                break;
                            case "jpg":
                            case "jpeg":
                                svg.Save(fileStream, SKColors.Empty, SKEncodedImageFormat.Jpeg, 100, scaleX, scaleY);
                                break;
                            default:
                                throw new Exception($"Unknown file type: {saveFileType}");
                        }
                    }

                    // Get the filesize
                    FileInfo fi = new FileInfo(filePathOnDisk);

                    if (fi.Exists)
                    {
                        fileSizeBytes = (int)fi.Length;
                    }
                    else
                    {
                        return (SqlQueryResult.UnknownError, null, originalImgWidth, originalImgHeight);
                    }
                }
            }

            contentInspectorResult.FileDataStream.Position = 0;

            string logDescription = "Create Image";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

insert into tblImageStorage
(id
,InsertDateUtc
,UpdatedDateUtc
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,MimeType
,FileUrl
,FileSizeBytes
,Width
,Height)
select @id
      ,@_now
      ,@_now
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
      ,@width
      ,@height

insert into tblImageStorage_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,ImageStorageId
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,MimeType
,FileUrl
,FileSizeBytes
,Width
,Height
,Deleted
,LogAction
,CascadeFrom
,CascadeLogId)
select @logid
      ,@_now
      ,@adminUserUid
      ,@adminUserDisplayName
      ,@remoteIpAddress
      ,@logDescription
      ,@id
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
      ,@width
      ,@height
      ,0 -- Deleted
      ,'Insert' -- LogAction
      ,@cascadeFrom
      ,@cascadeLogId

-- Select row to return with the API result
select id
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
from tblImageStorage
where id = @id
";
                Guid newImageId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", newImageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@relatedObjectType", relatedObjectType, DbType.Byte, ParameterDirection.Input);
                parameters.Add("@relatedObjectId", relatedObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@mimeType", "image/webp", DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileUrl", fileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileSizeBytes", fileSizeBytes, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@width", imgWidth, DbType.Int16, ParameterDirection.Input);
                parameters.Add("@height", imgHeight, DbType.Int16, ParameterDirection.Input);
                parameters.Add("@cascadeFrom", cascadeFrom, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                StoredImageFile result = await sqlConnection.QueryFirstAsync<StoredImageFile>(sql, parameters);

                return (SqlQueryResult.Ok, result, originalImgWidth, originalImgHeight);
            }
        }

        private SizeF ResizeFit(SizeF originalSize, SizeF maxSize, bool grow = false)
        {
            float widthRatio = maxSize.Width / originalSize.Width;
            float heightRatio = maxSize.Height / originalSize.Height;
            float minAspectRatio = Math.Min(widthRatio, heightRatio);
            if (!grow && minAspectRatio > 1)
                return originalSize;
            return new SizeF(originalSize.Width * minAspectRatio, originalSize.Height * minAspectRatio);
        }

        public async Task<(SqlQueryResult, StoredImageFile? image, StoredImageFile? thumbnail)> WriteSvgImageAndThumbnailAsync(
            ContentInspectorResultWithMemoryStream? contentInspectorResult,
            ImageStorageRelatedObjectType relatedObjectType,
            ImageStorageRelatedObjectType relatedObjectTypeThumbnail,
            Guid? relatedObjectId, Guid organizationId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress,
            string? saveFileType = "webp")
        {
            // Write the thumnail
            (SqlQueryResult sqlQueryResultThumbnail, StoredImageFile? storedImageFileThumbnail, short? originalImgWidth, short? originalImgHeight) = await WriteSvgRasterImageAsync(
                _appSettings.ImageUpload.ObjectRestrictions.SvgRaster.ThumbnailMaxImageWidth,
                _appSettings.ImageUpload.ObjectRestrictions.SvgRaster.ThumbnailMaxImageHeight,
                false, contentInspectorResult, relatedObjectTypeThumbnail,
                relatedObjectId, organizationId, cascadeFrom, cascadeLogId, adminUserUid, adminUserDisplayName, remoteIpAddress, saveFileType);

            // If it failed, stop here.
            if (sqlQueryResultThumbnail != SqlQueryResult.Ok)
            {
                return (sqlQueryResultThumbnail, null, storedImageFileThumbnail);
            }

            // Write the image
            (SqlQueryResult sqlQueryResult, StoredImageFile? storedImageFile) = await WriteSvgImageAsync(
                originalImgWidth!.Value, originalImgHeight!.Value,
                contentInspectorResult, relatedObjectType,
                relatedObjectId, organizationId, cascadeFrom, cascadeLogId, adminUserUid, adminUserDisplayName, remoteIpAddress);

            // Return the result whether successful or not.
            return (sqlQueryResult, storedImageFile, storedImageFileThumbnail);
        }

        public (SqlQueryResult, short? widthPixels, short? heightPixels) GetSvgImageDimensions(ContentInspectorResultWithMemoryStream? contentInspectorResult)
        {
            if (contentInspectorResult == null
                || contentInspectorResult.InspectedExtension is null
                || contentInspectorResult.FileDataStream is null
                || !ImageStorageHelpers.IsValidVectorImageExtension(contentInspectorResult.InspectedExtension))
            {
                return (SqlQueryResult.InvalidFileType, null, null);
            }

            contentInspectorResult.FileDataStream.Position = 0;

            using (SKSvg svg = new SKSvg())
            {
                if (svg.Load(contentInspectorResult.FileDataStream) is { } && svg.Picture is not null)
                {
                    return (SqlQueryResult.Ok, (short)svg.Picture.CullRect.Width, (short)svg.Picture.CullRect.Height);
                }
                else
                {
                    return (SqlQueryResult.UnknownError, null, null);
                }
            }
        }

        public async Task<(SqlQueryResult, StoredImageFile? storedImageFile, StoredImageFile? thumbnailFile)> WriteAvatarImageAsync(ContentInspectorResultWithMemoryStream? contentInspectorResult,
            Guid? relatedObjectId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress,
            string? saveFileType = "webp")
        {
            // Write the image
            (SqlQueryResult sqlQueryResult, StoredImageFile? storedImageFile) = await WriteImageAsync(
                _appSettings.ImageUpload.ObjectRestrictions.UserAvatar.MaxImageWidth,
                _appSettings.ImageUpload.ObjectRestrictions.UserAvatar.MaxImageHeight,
                true, // maintainAspectRatio
                true, // growImage
                contentInspectorResult,
                ImageStorageRelatedObjectType.UserAvatar,
                relatedObjectId,
                Guid.Empty, // organizationId
                cascadeFrom,
                cascadeLogId,
                adminUserUid,
                adminUserDisplayName,
                remoteIpAddress,
                saveFileType);

            // If it failed, stop here.
            if (sqlQueryResult != SqlQueryResult.Ok)
            {
                return (sqlQueryResult, storedImageFile, null);
            }

            // Write the thumnail
            (SqlQueryResult sqlQueryResultThumbnail, StoredImageFile? storedImageFileThumbnail) = await WriteImageAsync(
                _appSettings.ImageUpload.ObjectRestrictions.UserAvatar.ThumbnailMaxImageWidth,
                _appSettings.ImageUpload.ObjectRestrictions.UserAvatar.ThumbnailMaxImageHeight,
                true, // maintainAspectRatio
                true, // growImage
                contentInspectorResult,
                ImageStorageRelatedObjectType.UserAvatarThumbnail,
                relatedObjectId,
                Guid.Empty, // organizationId
                cascadeFrom,
                cascadeLogId,
                adminUserUid,
                adminUserDisplayName,
                remoteIpAddress,
                "webp"); // always use webp for thumbnail

            // Return the result whether successful or not.
            return (sqlQueryResultThumbnail, storedImageFile, storedImageFileThumbnail);
        }

        public static string GetFolderForImageType(Guid organizationId, ImageStorageRelatedObjectType imageType)
        {
            if (imageType == ImageStorageRelatedObjectType.UserAvatar || imageType == ImageStorageRelatedObjectType.UserAvatarThumbnail)
            {
                return Path.Combine("upload", imageType.ToString());
            }

            return Path.Combine("upload", organizationId.ToString(), imageType.ToString());
        }

        public string GetFolderForImageTypeWithWebRootPath(Guid organizationId, ImageStorageRelatedObjectType imageType)
        {
            if (imageType == ImageStorageRelatedObjectType.UserAvatar || imageType == ImageStorageRelatedObjectType.UserAvatarThumbnail)
            {
                return Path.Combine(_webEnvironment.WebRootPath, "upload", imageType.ToString());
            }

            return Path.Combine(_webEnvironment.WebRootPath, "upload", organizationId.ToString(), imageType.ToString());
        }

        /// <summary>
        /// Generates a random hexadecimal string. Length of string returned will be double the input <paramref name="byteLength"/>.
        /// </summary>
        /// <param name="byteLength"></param>
        /// <returns></returns>
        private static string GetRandomFilename(int byteLength = 20)
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(byteLength)).ToLower();
        }

        public async Task<SqlQueryResult> DeleteImageAsync(Guid imageStorageId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete Image";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_result bit = 0

declare @_data table
(
    RelatedObjectType tinyint
   ,RelatedObjectId uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,MimeType varchar(255)
   ,FileUrl varchar(255)
   ,FileSizeBytes int
   ,Width int
   ,Height int
)

update tblImageStorage
set UpdatedDateUtc = @_now
   ,Deleted = 1
output inserted.RelatedObjectType
      ,inserted.RelatedObjectId
      ,inserted.OrganizationId
      ,inserted.MimeType
      ,inserted.FileUrl
      ,inserted.FileSizeBytes
      ,inserted.Width
      ,inserted.Height
      into @_data
where id = @id
and Deleted = 0

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblImageStorage_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,ImageStorageId
    ,RelatedObjectType
    ,RelatedObjectId
    ,OrganizationId
    ,MimeType
    ,FileUrl
    ,FileSizeBytes
    ,Width
    ,Height
    ,Deleted
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayname
          ,@remoteIpAddress
          ,@logDescription
          ,@id
          ,d.RelatedObjectType
          ,d.RelatedObjectId
          ,d.OrganizationId
          ,d.MimeType
          ,d.FileUrl
          ,d.FileSizeBytes
          ,d.Width
          ,d.Height
          ,1 -- Deleted
          ,0 -- OldDeleted
          ,'Delete' -- LogAction
          ,@cascadeFrom
          ,@cascadeLogId
    from @_data d
end
else
begin
    -- Record could not be updated
    set @_result = 2
end

select @_result

if @_result = 1
begin
    -- Select information used to generate the file path so we can delete the image off disk
    select @id as Id
          ,RelatedObjectType
          ,RelatedObjectId
          ,OrganizationId
          ,FileUrl
    from @_data
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", imageStorageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@cascadeFrom", cascadeFrom, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input, 100);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                DeletedImageFile? deletedImageFile = null;

                // If record was deleted, get the data
                if (!gridReader.IsConsumed)
                {
                    deletedImageFile = await gridReader.ReadFirstOrDefaultAsync<DeletedImageFile>();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        if (deletedImageFile is not null)
                        {
                            DeleteFileFromDisk(deletedImageFile);
                        }
                        break;
                    case 2:
                        // Row did not exist
                        queryResult = SqlQueryResult.RecordDidNotExist;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return queryResult;
            }
        }

        private void DeleteFileFromDisk(DeletedImageFile deletedImageFile)
        {
            try
            {
                string directory;

                if (deletedImageFile.RelatedObjectType == ImageStorageRelatedObjectType.UserAvatar
                    || deletedImageFile.RelatedObjectType == ImageStorageRelatedObjectType.UserAvatarThumbnail)
                {
                    directory = GetFolderForImageTypeWithWebRootPath(Guid.Empty, deletedImageFile.RelatedObjectType);
                }
                else
                {
                    directory = GetFolderForImageTypeWithWebRootPath(deletedImageFile.OrganizationId!.Value, deletedImageFile.RelatedObjectType);
                }

                string fileName = Path.GetFileName(deletedImageFile.FileUrl);

                string filePath = Path.Combine(directory, fileName);
            
                File.Delete(filePath);
            }
            catch
            {
                // Don't throw exception if delete from disk fails
            }
        }

        /// <summary>
        /// Retrieves a paginated list of image storage logs to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ImageStorageLog>> ListImageStorageLogsForDataTableAsync(Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Created:
                        sortColumn = "id desc";
                        break;
                    default:
                        sortColumn = "id desc";
                        break;
                }

                string whereQuery = "";

                if (searchTerms != null && searchTerms.Count > 0)
                {
                    foreach (SearchTerm searchTerm in searchTerms)
                    {
                        switch (searchTerm.Field)
                        {
                            /* TODO: Specify search columns as cases, and also in empty string case
                            case "someColumn":
                                whereQuery += SearchQueryParser.BuildSqlString(searchTerm, "SomeColumn");
                                break;
                            */
                            case "":
                                List<SearchTermSqlInfo> searchTermSqlInfos = new List<SearchTermSqlInfo>();
                                // TODO: Specify search columns here in empty string case, and also above as cases
                                //searchTermSqlInfos.Add(new SearchTermSqlInfo { SearchTerm = searchTerm, SqlColumnName = "SomeColumn" });
                                whereQuery += SearchQueryParser.BuildSqlString(searchTermSqlInfos);
                                break;
                        }
                    }
                }

                string sql = $@"
-- Get total number of image storage logs in database matching search term
select count(*)
from tblImageStorage_Log
where OrganizationId = @organizationId
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblImageStorage_Log
    where OrganizationId = @organizationId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select id
      ,InsertDateUtc
      ,UpdatedByUid
      ,UpdatedByDisplayName
      ,UpdatedByIpAddress
      ,LogDescription
      ,ImageStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblImageStorage_Log
where exists
(
    select 1
    from pg
    where pg.id = tblImageStorage_Log.id
)
order by tblImageStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ImageStorageLog> result = new DataTableResponse<ImageStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ImageStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of image storage logs by <paramref name="relatedObjectId"/> to be used for displaying a data table.
        /// </summary>
        /// <param name="relatedObjectId"></param>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ImageStorageLog>> ListImageStorageLogsByObjectIdForDataTableAsync(Guid relatedObjectId, List<int>? relatedObjectTypes, Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Created:
                        sortColumn = "id desc";
                        break;
                    default:
                        sortColumn = "id desc";
                        break;
                }

                string whereQuery = "";

                if (searchTerms != null && searchTerms.Count > 0)
                {
                    foreach (SearchTerm searchTerm in searchTerms)
                    {
                        switch (searchTerm.Field)
                        {
                            /* TODO: Specify search columns as cases, and also in empty string case
                            case "someColumn":
                                whereQuery += SearchQueryParser.BuildSqlString(searchTerm, "SomeColumn");
                                break;
                            */
                            case "":
                                List<SearchTermSqlInfo> searchTermSqlInfos = new List<SearchTermSqlInfo>();
                                // TODO: Specify search columns here in empty string case, and also above as cases
                                //searchTermSqlInfos.Add(new SearchTermSqlInfo { SearchTerm = searchTerm, SqlColumnName = "SomeColumn" });
                                whereQuery += SearchQueryParser.BuildSqlString(searchTermSqlInfos);
                                break;
                        }
                    }
                }

                string relatedObjectTypesWhereQuery = "";

                if (relatedObjectTypes is not null && relatedObjectTypes.Count > 0)
                {
                    relatedObjectTypesWhereQuery = $"and RelatedObjectType in ({string.Join(',', relatedObjectTypes)})";
                }

                string sql = $@"
-- Get total number of image storage logs in database matching search term
select count(*)
from tblImageStorage_Log
where OrganizationId = @organizationId
and RelatedObjectId = @relatedObjectId
{whereQuery}
{relatedObjectTypesWhereQuery}

-- Get data
;with pg as
(
    select id
    from tblImageStorage_Log
    where OrganizationId = @organizationId
    and RelatedObjectId = @relatedObjectId
    {whereQuery}
    {relatedObjectTypesWhereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select id
      ,InsertDateUtc
      ,UpdatedByUid
      ,UpdatedByDisplayName
      ,UpdatedByIpAddress
      ,LogDescription
      ,ImageStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblImageStorage_Log
where exists
(
    select 1
    from pg
    where pg.id = tblImageStorage_Log.id
)
order by tblImageStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@relatedObjectId", relatedObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ImageStorageLog> result = new DataTableResponse<ImageStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ImageStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of image storage logs by <paramref name="imageStorageId"/> to be used for displaying a data table.
        /// </summary>
        /// <param name="imageStorageId"></param>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ImageStorageLog>> ListImageStorageLogsByImageStorageIdForDataTableAsync(Guid imageStorageId, Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Created:
                        sortColumn = "id desc";
                        break;
                    default:
                        sortColumn = "id desc";
                        break;
                }

                string whereQuery = "";

                if (searchTerms != null && searchTerms.Count > 0)
                {
                    foreach (SearchTerm searchTerm in searchTerms)
                    {
                        switch (searchTerm.Field)
                        {
                            /* TODO: Specify search columns as cases, and also in empty string case
                            case "someColumn":
                                whereQuery += SearchQueryParser.BuildSqlString(searchTerm, "SomeColumn");
                                break;
                            */
                            case "":
                                List<SearchTermSqlInfo> searchTermSqlInfos = new List<SearchTermSqlInfo>();
                                // TODO: Specify search columns here in empty string case, and also above as cases
                                //searchTermSqlInfos.Add(new SearchTermSqlInfo { SearchTerm = searchTerm, SqlColumnName = "SomeColumn" });
                                whereQuery += SearchQueryParser.BuildSqlString(searchTermSqlInfos);
                                break;
                        }
                    }
                }

                string sql = $@"
-- Get total number of image storage logs in database matching search term
select count(*)
from tblImageStorage_Log
where OrganizationId = @organizationId
and ImageStorageId = @imageStorageId
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblImageStorage_Log
    where OrganizationId = @organizationId
    and ImageStorageId = @imageStorageId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select id
      ,InsertDateUtc
      ,UpdatedByUid
      ,UpdatedByDisplayName
      ,UpdatedByIpAddress
      ,LogDescription
      ,ImageStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblImageStorage_Log
where exists
(
    select 1
    from pg
    where pg.id = tblImageStorage_Log.id
)
order by tblImageStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@imageStorageId", imageStorageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ImageStorageLog> result = new DataTableResponse<ImageStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ImageStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of image storage logs by CascadeLogId to be used for displaying a data table.
        /// </summary>
        /// <param name="cascadeLogId"></param>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<ImageStorageLog>> ListImageStorageLogsByCascadeLogIdForDataTableAsync(Guid cascadeLogId, Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
        {
            // Query from: https://sqlperformance.com/2015/01/t-sql-queries/pagination-with-offset-fetch
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sortColumn;

                switch (sort)
                {
                    case SortType.Created:
                        sortColumn = "id desc";
                        break;
                    default:
                        sortColumn = "id desc";
                        break;
                }

                string whereQuery = "";

                if (searchTerms != null && searchTerms.Count > 0)
                {
                    foreach (SearchTerm searchTerm in searchTerms)
                    {
                        switch (searchTerm.Field)
                        {
                            /* TODO: Specify search columns as cases, and also in empty string case
                            case "someColumn":
                                whereQuery += SearchQueryParser.BuildSqlString(searchTerm, "SomeColumn");
                                break;
                            */
                            case "":
                                List<SearchTermSqlInfo> searchTermSqlInfos = new List<SearchTermSqlInfo>();
                                // TODO: Specify search columns here in empty string case, and also above as cases
                                //searchTermSqlInfos.Add(new SearchTermSqlInfo { SearchTerm = searchTerm, SqlColumnName = "SomeColumn" });
                                whereQuery += SearchQueryParser.BuildSqlString(searchTermSqlInfos);
                                break;
                        }
                    }
                }

                string sql = $@"
-- Get total number of image storage logs in database matching search term
select count(*)
from tblImageStorage_Log
where OrganizationId = @organizationId
and CascadeLogId = @cascadeLogId
{whereQuery}

-- Get data
;with pg as
(
    select CascadeLogId
    from tblImageStorage_Log
    where OrganizationId = @organizationId
    and CascadeLogId = @cascadeLogId
    {whereQuery}
    order by {sortColumn}
    offset @pageSize * (@pageNumber - 1) rows
    fetch next @pageSize rows only
)
select id
      ,InsertDateUtc
      ,UpdatedByUid
      ,UpdatedByDisplayName
      ,UpdatedByIpAddress
      ,LogDescription
      ,ImageStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblImageStorage_Log
where exists
(
    select 1
    from pg
    where pg.CascadeLogId = tblImageStorage_Log.CascadeLogId
)
order by tblImageStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<ImageStorageLog> result = new DataTableResponse<ImageStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<ImageStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves the specified image storage log from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <returns></returns>
        public async Task<ImageStorageLog?> GetImageStorageLogByLogIdAsync(Guid logId, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select id
      ,InsertDateUtc
      ,UpdatedByUid
      ,UpdatedByDisplayName
      ,UpdatedByIpAddress
      ,LogDescription
      ,ImageStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Width
      ,Height
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblImageStorage_Log
where OrganizationId = @organizationId
and id = @logId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<ImageStorageLog>(commandDefinition);
            }
        }
    }
}
