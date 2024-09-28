using Dapper;
using Microsoft.Data.SqlClient;
using MimeDetective;
using MimeDetective.Engine;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.FileStorage.Enums;
using VisitorTabletAPITemplate.FileStorage.Models;
using VisitorTabletAPITemplate.FileStorage.Models.LogModels;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using System.Collections.Immutable;
using System.Data;
using System.Security.Cryptography;
using System.Text;

namespace VisitorTabletAPITemplate.FileStorage.Repositories
{
    public sealed class FileStorageRepository
    {
        private readonly AppSettings _appSettings;
        private readonly IWebHostEnvironment _webEnvironment;

        public FileStorageRepository(AppSettings appSettings,
            IWebHostEnvironment webEnvironment)
        {
            _appSettings = appSettings;
            _webEnvironment = webEnvironment;
        }

        /// <summary>
        /// <para>Copies <see cref="IFormFile"/> stream to a <see cref="MemoryStream"/> and inspects the file's contents to determine the actual extension and mime type.</para>
        /// <para>After calling this function, 1) the <see cref="IFormFile"> stream will be consumed and should no longer be used, and 2) you need to dispose of <see cref="ContentInspectorResultWithMemoryStream.FileDataStream"/> manually.</para>
        /// <para>Returns null and doesn't consume the <see cref="IFormFile"/> stream if <paramref name="formFile"/> is null or length 0.</para>
        /// </summary>
        /// <param name="formFile">The <see cref="IFormFile"/> containing the uploaded attachment file.</param>
        /// <returns></returns>
        public static Task<ContentInspectorResultWithMemoryStream?> CopyFormFileContentAndInspectFileAsync(IFormFile formFile, CancellationToken cancellationToken = default)
        {
            return CopyFormFileContentAndInspectFileAsync(formFile, FileContentInspector.Instance.ContentInspector, cancellationToken);
        }

        /// <summary>
        /// <para>Copies <see cref="IFormFile"/> stream to a <see cref="MemoryStream"/> and inspects the file's contents to determine the actual extension and mime type.</para>
        /// <para>After calling this function, 1) the <see cref="IFormFile"> stream will be consumed and should no longer be used, and 2) you need to dispose of <see cref="ContentInspectorResultWithMemoryStream.FileDataStream"/> manually.</para>
        /// <para>Returns null and doesn't consume the <see cref="IFormFile"/> stream if <paramref name="formFile"/> is null or length 0.</para>
        /// </summary>
        /// <param name="formFile">The <see cref="IFormFile"/> containing the uploaded attachment file.</param>
        /// <returns></returns>
        public static async Task<ContentInspectorResultWithMemoryStream?> CopyFormFileContentAndInspectFileAsync(IFormFile formFile, ContentInspector contentInspector, CancellationToken cancellationToken = default)
        {
            if (formFile == null || formFile.Length == 0)
            {
                return null;
            }

            ContentInspectorResultWithMemoryStream result = new ContentInspectorResultWithMemoryStream();

            result.FileDataStream = new MemoryStream();
            await formFile.CopyToAsync(result.FileDataStream, cancellationToken);

            result.FileDataStream.Position = 0;

            ImmutableArray<DefinitionMatch> inspectResult = contentInspector.Inspect(result.FileDataStream);
            result.FileName = Path.GetFileName(formFile.FileName);
            result.InspectedExtension = inspectResult.ByFileExtension().FirstOrDefault()?.Extension;
            result.InspectedMimeType = inspectResult.ByMimeType().FirstOrDefault()?.MimeType;

            if (!string.IsNullOrEmpty(formFile.FileName))
            {
                result.OriginalExtension = Path.GetExtension(formFile.FileName);
            }

            result.FileDataStream.Position = 0;

            return result;
        }

        public async Task<(SqlQueryResult, StoredAttachmentFile?)> WriteFileAsync(ContentInspectorResultWithMemoryStream contentInspectorResult, FileStorageRelatedObjectType relatedObjectType, Guid? relatedObjectId, Guid organizationId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            if (contentInspectorResult == null
                || contentInspectorResult.InspectedExtension is null
                || contentInspectorResult.FileDataStream is null)
            {
                return (SqlQueryResult.InvalidFileType, null);
            }

            if (!FileStorageHelpers.IsValidAttachmentExtension(contentInspectorResult.InspectedExtension)
                && (contentInspectorResult.OriginalExtension is null || (contentInspectorResult.OriginalExtension is not null && !FileStorageHelpers.IsValidAttachmentExtension(contentInspectorResult.OriginalExtension))))
            {
                return (SqlQueryResult.InvalidFileType, null);
            }

            string? fileUrl = null;
            int fileSizeBytes;

            string folderPath = GetFolderForFileType(organizationId, relatedObjectType);
            string folderPathOnDisk = GetFolderForFileTypeWithWebRootPath(organizationId, relatedObjectType);
            string fileNameOnDisk = GetRandomFilename() + "." + contentInspectorResult.OriginalExtension;
            fileUrl = "/" + Path.Combine(folderPath, fileNameOnDisk).Replace("\\", "/");
            string filePathOnDisk = Path.Combine(folderPathOnDisk, fileNameOnDisk);
            Directory.CreateDirectory(folderPathOnDisk);

            contentInspectorResult.FileDataStream.Position = 0;

            // Save file to disk
            using (FileStream fileStream = new FileStream(filePathOnDisk, FileMode.Create, FileAccess.Write))
            {
                contentInspectorResult.FileDataStream.WriteTo(fileStream);
            }

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

            contentInspectorResult.FileDataStream.Position = 0;

            string logDescription = "Create File";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()

insert into tblFileStorage
(id
,InsertDateUtc
,UpdatedDateUtc
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,FileName
,MimeType
,FileUrl
,FileSizeBytes)
select @id
      ,@_now
      ,@_now
      ,@relatedObjectType
      ,@relatedObjectId
      ,@organizationId
      ,@fileName
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes

insert into tblFileStorage_Log
(id
,InsertDateUtc
,UpdatedByUid
,UpdatedByDisplayName
,UpdatedByIpAddress
,LogDescription
,FileStorageId
,RelatedObjectType
,RelatedObjectId
,OrganizationId
,FileName
,MimeType
,FileUrl
,FileSizeBytes
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
      ,@fileName
      ,@mimeType
      ,@fileUrl
      ,@fileSizeBytes
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
      ,FileName
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
from tblFileStorage
where id = @id
";
                Guid newFileId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", newFileId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@relatedObjectType", relatedObjectType, DbType.Byte, ParameterDirection.Input);
                parameters.Add("@relatedObjectId", relatedObjectId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@fileName", contentInspectorResult.FileName, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@mimeType", contentInspectorResult.InspectedMimeType, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileUrl", fileUrl, DbType.AnsiString, ParameterDirection.Input, 255);
                parameters.Add("@fileSizeBytes", fileSizeBytes, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@cascadeFrom", cascadeFrom, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                StoredAttachmentFile result = await sqlConnection.QueryFirstAsync<StoredAttachmentFile>(sql, parameters);

                return (SqlQueryResult.Ok, result);
            }
        }

        public async Task<SqlQueryResult> DeleteFileAsync(Guid fileStorageId, string? cascadeFrom, Guid? cascadeLogId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Delete File";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_now datetime2(3) = sysutcdatetime()
declare @_result int = 0

declare @_data table
(
    RelatedObjectType tinyint
   ,RelatedObjectId uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,FileName nvarchar(255)
   ,MimeType varchar(255)
   ,FileUrl varchar(255)
   ,FileSizeBytes int
)

update tblFileStorage
set UpdatedDateUtc = @_now
   ,Deleted = 1
output inserted.RelatedObjectType
      ,inserted.RelatedObjectId
      ,inserted.OrganizationId
      ,inserted.FileName
      ,inserted.MimeType
      ,inserted.FileUrl
      ,inserted.FileSizeBytes
      into @_data
where id = @id
and Deleted = 0

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblFileStorage_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,FileStorageId
    ,RelatedObjectType
    ,RelatedObjectId
    ,OrganizationId
    ,FileName
    ,MimeType
    ,FileUrl
    ,FileSizeBytes
    ,Deleted
    ,OldDeleted
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
          ,d.RelatedObjectType
          ,d.RelatedObjectId
          ,d.OrganizationId
          ,d.FileName
          ,d.MimeType
          ,d.FileUrl
          ,d.FileSizeBytes
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
                parameters.Add("@id", fileStorageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@cascadeFrom", cascadeFrom, DbType.AnsiString, ParameterDirection.Input, 50);
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                DeletedAttachmentFile? deletedAttachmentFile = null;

                // If record was deleted, get the data
                if (!gridReader.IsConsumed)
                {
                    deletedAttachmentFile = await gridReader.ReadFirstOrDefaultAsync<DeletedAttachmentFile>();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        if (deletedAttachmentFile is not null)
                        {
                            DeleteFileFromDisk(deletedAttachmentFile);
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

        public async Task<SqlQueryResult> DeleteFilesAsync(List<Guid> fileStorageIds, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            if (fileStorageIds is null || fileStorageIds.Count == 0)
            {
                return SqlQueryResult.Ok;
            }

            string logDescription = "Delete File";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();
                StringBuilder sql = new StringBuilder(@"
declare @_now datetime2(3) = sysutcdatetime()
declare @_result int = 0
declare @_hasInvalidFileStorageIds bit = 0

declare @_data table
(
    id uniqueidentifier
   ,RelatedObjectType tinyint
   ,RelatedObjectId uniqueidentifier
   ,OrganizationId uniqueidentifier
   ,FileName nvarchar(255)
   ,MimeType varchar(255)
   ,FileUrl varchar(255)
   ,FileSizeBytes int
)

declare @_fileStorageIdsData table
(
    LogId uniqueidentifier
   ,FileStorageId uniqueidentifier
)

declare @_fileStorageLogIds table
(
    LogId uniqueidentifier
)

insert into @_fileStorageIdsData
(LogId, FileStorageId)
values
");
                for (int i = 0; i < fileStorageIds.Count; ++i)
                {
                    if (i > 0)
                    {
                        sql.Append(',');
                    }

                    sql.AppendLine($"(@fileStorageLogId{i}, @fileStorageId{i})");
                    parameters.Add($"@fileStorageLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                    parameters.Add($"@fileStorageId{i}", fileStorageIds[i], DbType.Guid, ParameterDirection.Input);
                }

                sql.Append($@"
-- Check for provided FileStorageIds which:
-- 1) Do not exist in tblFileStorage 
-- 2) Are deleted in tblFileStorage 
-- If a storage id satisfies the above conditions, it is invalid

select top 1 @_hasInvalidFileStorageIds = 1
from @_fileStorageIdsData d
where not exists
(
    select *
    from tblFileStorage
    where tblFileStorage.id = d.fileStorageId
    and tblFileStorage.Deleted = 0
)

if @_hasInvalidFileStorageIds = 0
begin
    update tblFileStorage
    set UpdatedDateUtc = @_now
       ,Deleted = 1
    output inserted.id
          ,inserted.RelatedObjectType
          ,inserted.RelatedObjectId
          ,inserted.OrganizationId
          ,inserted.FileName
          ,inserted.MimeType
          ,inserted.FileUrl
          ,inserted.FileSizeBytes
          into @_data
    where Deleted = 0
    and id in
    (
	    select FileStorageId
	    from @_fileStorageIdsData
    )

    if @@ROWCOUNT = {fileStorageIds.Count}
    begin
        set @_result = 1

        insert into tblFileStorage_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,FileStorageId
        ,RelatedObjectType
        ,RelatedObjectId
        ,OrganizationId
        ,FileName
        ,MimeType
        ,FileUrl
        ,FileSizeBytes
        ,Deleted
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        output inserted.id
               into @_fileStorageLogIds
        select newid() -- Temporary ID to be overwritten later
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,d.id
              ,d.RelatedObjectType
              ,d.RelatedObjectId
              ,d.OrganizationId
              ,d.FileName
              ,d.MimeType
              ,d.FileUrl
              ,d.FileSizeBytes
              ,1 -- Deleted
              ,0 -- OldDeleted
              ,'Delete' -- LogAction
              ,null -- CascadeFrom
              ,null -- CascadeLogId
        from @_data d
    end
    else
    begin
        -- Record could not be updated
        set @_result = 2
    end
end
else
begin
    -- FileStorageIds list contains Invalid File Storage Ids
    set @_result = 3
end

select @_result

-- Select file log IDs to be overwritten
select LogId
from @_fileStorageLogIds

if @_result = 1
begin
    -- Select information used to generate the file path so we can delete the image off disk
    select id
            ,RelatedObjectType
            ,RelatedObjectId
            ,OrganizationId
            ,FileUrl
    from @_data
end
");
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                CommandDefinition commandDefinition = new CommandDefinition(sql.ToString(), parameters);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                List<Guid> fileStorageLogIds = (await gridReader.ReadAsync<Guid>()).AsList();

                List<DeletedAttachmentFile>? deletedAttachmentFiles = null;

                // If record was deleted, get the data
                if (!gridReader.IsConsumed)
                {
                    deletedAttachmentFiles = (await gridReader.ReadAsync<DeletedAttachmentFile>()).AsList();
                }

                SqlQueryResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = SqlQueryResult.Ok;

                        if (fileStorageLogIds is not null && fileStorageLogIds.Count > 0)
                        {
                            // Update log ids generated using newid() with RT.Comb instead
                            await Toolbox.UpdateLogGuidsWithRTCombAsync(sqlConnection, fileStorageLogIds, "tblFileStorage_Log");
                        }

                        if (deletedAttachmentFiles is not null && deletedAttachmentFiles.Count > 0)
                        {
                            for (int i = 0; i < deletedAttachmentFiles!.Count; ++i)
                            {
                                DeleteFileFromDisk(deletedAttachmentFiles[i]);
                            }
                        }
                        break;
                    case 2:
                        // Row did not exist
                        queryResult = SqlQueryResult.RecordDidNotExist;
                        break;
                    case 3:
                        // FileStorageIds contains invalid entries, i.e. don't exist in tblFileStorage,
                        // or is deleted
                        queryResult = SqlQueryResult.RecordInvalid;
                        break;
                    default:
                        queryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return queryResult;
            }
        }

        public string GetFolderForFileType(Guid organizationId, FileStorageRelatedObjectType fileType)
        {
            return Path.Combine("upload", organizationId.ToString(), fileType.ToString());
        }

        public string GetFolderForFileTypeWithWebRootPath(Guid organizationId, FileStorageRelatedObjectType fileType)
        {
            return Path.Combine(_webEnvironment.WebRootPath, "upload", organizationId.ToString(), fileType.ToString());
        }

        /// <summary>
        /// Generates a random hexadecimal string. Length of string returned will be double the input <paramref name="byteLength"/>.
        /// </summary>
        /// <param name="byteLength"></param>
        /// <returns></returns>
        public string GetRandomFilename(int byteLength = 20)
        {
            return Convert.ToHexString(RandomNumberGenerator.GetBytes(byteLength)).ToLower();
        }

        private void DeleteFileFromDisk(DeletedAttachmentFile deletedAttachmentFile)
        {
            try
            {
                string directory;

                directory = GetFolderForFileTypeWithWebRootPath(deletedAttachmentFile.OrganizationId!.Value, deletedAttachmentFile.RelatedObjectType);

                string fileName = Path.GetFileName(deletedAttachmentFile.FileUrl);

                string filePath = Path.Combine(directory, fileName);

                File.Delete(filePath);
            }
            catch
            {
                // Don't throw exception if delete from disk fails
            }
        }

        /// <summary>
        /// Retrieves a paginated list of file storage logs to be used for displaying a data table.
        /// </summary>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<FileStorageLog>> ListFileStorageLogsForDataTableAsync(Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
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
-- Get total number of file storage logs in database matching search term
select count(*)
from tblFileStorage_Log
where OrganizationId = @organizationId
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblFileStorage_Log
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
      ,FileStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,FileName
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblFileStorage_Log
where exists
(
    select 1
    from pg
    where pg.id = tblFileStorage_Log.id
)
order by tblFileStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<FileStorageLog> result = new DataTableResponse<FileStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<FileStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of file storage logs by fileStorageId to be used for displaying a data table.
        /// </summary>
        /// <param name="fileStorageId"></param>
        /// <param name="organizationId"></param>
        /// <param name="pageNumber"></param>
        /// <param name="pageSize"></param>
        /// <param name="sort"></param>
        /// <param name="requestCounter"></param>
        /// <param name="searchTerms"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<DataTableResponse<FileStorageLog>> ListFileStorageLogsByObjectIdForDataTableAsync(Guid fileStorageId, Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
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
-- Get total number of file storage logs in database matching search term
select count(*)
from tblFileStorage_Log
where OrganizationId = @organizationId
and FileStorageId = @fileStorageId
{whereQuery}

-- Get data
;with pg as
(
    select id
    from tblFileStorage_Log
    where OrganizationId = @organizationId
    and FileStorageId = @fileStorageId
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
      ,FileStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,FileName
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblFileStorage_Log
where exists
(
    select 1
    from pg
    where pg.id = tblFileStorage_Log.id
)
order by tblFileStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@fileStorageId", fileStorageId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<FileStorageLog> result = new DataTableResponse<FileStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<FileStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// Retrieves a paginated list of file storage logs by CascadeLogId to be used for displaying a data table.
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
        public async Task<DataTableResponse<FileStorageLog>> ListFileStorageLogsByCascadeLogIdForDataTableAsync(Guid cascadeLogId, Guid organizationId, int pageNumber, int pageSize, SortType sort, long? requestCounter, List<SearchTerm>? searchTerms = null, CancellationToken cancellationToken = default)
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
-- Get total number of file storage logs in database matching search term
select count(*)
from tblFileStorage_Log
where OrganizationId = @organizationId
and CascadeLogId = @cascadeLogId
{whereQuery}

-- Get data
;with pg as
(
    select CascadeLogId
    from tblFileStorage_Log
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
      ,FileStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,FileName
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblFileStorage_Log
where exists
(
    select 1
    from pg
    where pg.CascadeLogId = tblFileStorage_Log.CascadeLogId
)
order by tblFileStorage_Log.{sortColumn}
--option (recompile)
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@cascadeLogId", cascadeLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@pageNumber", pageNumber, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@pageSize", pageSize, DbType.Int32, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);
                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                DataTableResponse<FileStorageLog> result = new DataTableResponse<FileStorageLog>();
                result.RequestCounter = requestCounter;
                result.PageNumber = pageNumber;
                result.PageSize = pageSize;
                result.TotalCount = await gridReader.ReadFirstOrDefaultAsync<int>();
                result.Records = (await gridReader.ReadAsync<FileStorageLog>()).AsList();

                return result;
            }
        }

        /// <summary>
        /// <para>Retrieves the specified file storage log from the database.</para>
        /// <para>Returns null if no record is found.</para>
        /// </summary>
        /// <param name="logId"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<FileStorageLog?> GetFileStorageLogByLogIdAsync(Guid logId, Guid organizationId, CancellationToken cancellationToken = default)
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
      ,FileStorageId
      ,RelatedObjectType
      ,RelatedObjectId
      ,OrganizationId
      ,FileName
      ,MimeType
      ,FileUrl
      ,FileSizeBytes
      ,Deleted
      ,OldDeleted
      ,LogAction
      ,CascadeFrom
      ,CascadeLogId
from tblFileStorage_Log
where OrganizationId = @organizationId
and id = @logId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@logId", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<FileStorageLog>(commandDefinition);
            }
        }

        /// <summary>
        /// Returns true if a record exists with the given FileStorageId and RelatedObjectType.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="fileStorageRelatedObjectType"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsFileStorageIdExistsWithRelatedObjectTypeAsync(Guid id, FileStorageRelatedObjectType fileStorageRelatedObjectType, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select case when exists
(
    select *
    from tblFileStorage
    where Deleted = 0
    and id = @id
    and RelatedObjectType = @relatedObjectType
)
then 1 else 0 end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@id", id, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@relatedObjectType", (int)fileStorageRelatedObjectType, DbType.Byte, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<bool>(commandDefinition);
            }
        }

        /// <summary>
        /// Returns true if a record exists for all of the given FileStorageIds.
        /// </summary>
        /// <param name="fileStorageIds"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsFileStorageIdsExistAsync(List<Guid?> fileStorageIds, CancellationToken cancellationToken = default)
        {
            List<Guid> dedupedList = Toolbox.DedupeGuidList(fileStorageIds);

            if (dedupedList.Count == 0)
            {
                return false;
            }

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();

                StringBuilder sql = new StringBuilder(@"
select count(*)
from tblFileStorage
where Deleted = 0
and id in
(
");
                for (int i = 0; i < dedupedList.Count; ++i)
                {
                    if (i > 0)
                    {
                        sql.Append(", ");
                    }

                    sql.AppendLine($"@fileStorageId{i}");
                    parameters.Add($"@fileStorageId{i}", dedupedList[i], DbType.Guid, ParameterDirection.Input);
                }

                sql.AppendLine(@"
)
");
                CommandDefinition commandDefinition = new CommandDefinition(sql.ToString(), parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<int>(commandDefinition) == dedupedList.Count;
            }
        }

        /// <summary>
        /// Returns true if a record exists for all of the given FileStorageIds and if all have the specified RelatedObjectType.
        /// </summary>
        /// <param name="fileStorageIds"></param>
        /// <param name="fileStorageRelatedObjectType"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<bool> IsFileStorageIdsExistWithRelatedObjectTypeAsync(List<Guid?> fileStorageIds, FileStorageRelatedObjectType fileStorageRelatedObjectType, CancellationToken cancellationToken = default)
        {
            List<Guid> dedupedList = Toolbox.DedupeGuidList(fileStorageIds);

            if (dedupedList.Count == 0)
            {
                return false;
            }

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@relatedObjectType", (int)fileStorageRelatedObjectType, DbType.Byte, ParameterDirection.Input);

                StringBuilder sql = new StringBuilder(@"
select count(*)
from tblFileStorage
where Deleted = 0
and RelatedObjectType = @relatedObjectType
and id in
(
");
                for (int i = 0; i < dedupedList.Count; ++i)
                {
                    if (i > 0)
                    {
                        sql.Append(", ");
                    }

                    sql.AppendLine($"@fileStorageId{i}");
                    parameters.Add($"@fileStorageId{i}", dedupedList[i], DbType.Guid, ParameterDirection.Input);
                }

                sql.AppendLine(@"
)
");
                CommandDefinition commandDefinition = new CommandDefinition(sql.ToString(), parameters, cancellationToken: cancellationToken);

                return await sqlConnection.QueryFirstOrDefaultAsync<int>(commandDefinition) == dedupedList.Count;
            }
        }
    }
}
