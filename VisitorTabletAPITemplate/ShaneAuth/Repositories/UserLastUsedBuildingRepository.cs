using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.User.UpdateLastUsedBuilding;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.ShaneAuth.ShaneAuth.Models;
using VisitorTabletAPITemplate.Utilities;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.ShaneAuth.Repositories
{
    public sealed class UserLastUsedBuildingRepository
    {
        private readonly AppSettings _appSettings;
        private readonly AuthCacheService _authCacheService;

        public UserLastUsedBuildingRepository(AppSettings appSettings,
            AuthCacheService authCacheService) 
        {
            _appSettings = appSettings;
            _authCacheService = authCacheService;
        }

        public async Task<SqlQueryResult> UpdateUserWebLastUsedBuildingAsync(Guid uid, Guid organizationId, Guid buildingId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress) 
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_isUserLastUsedBuildingExists uniqueidentifier = null

select @_isUserLastUsedBuildingExists = Uid
from tblUserLastUsedBuilding
where Uid = @uid

if @_isUserLastUsedBuildingExists is null
begin
    -- insert a new row
    insert into tblUserLastUsedBuilding
    (Uid
    ,InsertDateUtc
    ,UpdatedDateUtc
    ,WebLastUsedOrganizationId
    ,WebLastUsedBuildingId)
    select @uid
          ,@_now
          ,@_now
          ,@organizationId
          ,@buildingId
    where not exists
    (
        select *
        from tblUserLastUsedBuilding
        where Uid = @uid
    )
end
else
begin
    -- update the current row
    update tblUserLastUsedBuilding
    set UpdatedDateUtc = @_now
       ,WebLastUsedOrganizationId = @organizationId
       ,WebLastUsedBuildingId = @buildingId
    where Uid = @uid
end

if @@ROWCOUNT = 1
begin
    set @_result = 1
end
else
begin
    -- Record was not inserted or updated
    set @_result = 2
end

select @_result
";
                Guid logId = RT.Comb.Provider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);

                int resultCode = await sqlConnection.QueryFirstOrDefaultAsync<int>(sql, parameters);

                SqlQueryResult sqlQueryResult;

                switch (resultCode)
                {
                    case 1:
                        sqlQueryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        sqlQueryResult = SqlQueryResult.InsufficientPermissions;
                        break;
                    default:
                        sqlQueryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return sqlQueryResult;
            }
        }

        public async Task<SqlQueryResult> UpdateUserMobileLastUsedBuildingAsync(Guid uid, Guid organizationId, Guid buildingId, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_isUserLastUsedBuildingExists uniqueidentifier = null

select @_isUserLastUsedBuildingExists = Uid
from tblUserLastUsedBuilding
where Uid = @uid

if @_isUserLastUsedBuildingExists is null
begin
    -- insert a new row
    insert into tblUserLastUsedBuilding
    (Uid
    ,InsertDateUtc
    ,UpdatedDateUtc
    ,MobileLastUsedOrganizationId
    ,MobileLastUsedBuildingId)
    select @uid
          ,@_now
          ,@_now
          ,@organizationId
          ,@buildingId
    where not exists
    (
        select *
        from tblUserLastUsedBuilding
        where Uid = @uid
    )
end
else
begin
    -- update the current row
    update tblUserLastUsedBuilding
    set UpdatedDateUtc = @_now
       ,MobileLastUsedOrganizationId = @organizationId
       ,MobileLastUsedBuildingId = @buildingId
    where Uid = @uid
end

if @@ROWCOUNT = 1
begin
    set @_result = 1
end
else
begin
    -- Record was not inserted or updated
    set @_result = 2
end

select @_result
";
                Guid logId = RT.Comb.Provider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);

                int resultCode = await sqlConnection.QueryFirstOrDefaultAsync<int>(sql, parameters);

                SqlQueryResult sqlQueryResult;

                switch (resultCode)
                {
                    case 1:
                        sqlQueryResult = SqlQueryResult.Ok;
                        break;
                    case 2:
                        sqlQueryResult = SqlQueryResult.InsufficientPermissions;
                        break;
                    default:
                        sqlQueryResult = SqlQueryResult.UnknownError;
                        break;
                }

                return sqlQueryResult;
            }
        }
    }
}
