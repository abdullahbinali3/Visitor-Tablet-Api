using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.AddUserToOrganization;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromOrganization;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUserOrganization;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;
using System.Data;
using System.Text;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.ShaneAuth.Repositories
{
    public sealed class UserOrganizationsRepository
    {
        private readonly AppSettings _appSettings;
        private readonly AuthCacheService _authCacheService;

        public UserOrganizationsRepository(AppSettings appSettings,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _authCacheService = authCacheService;
        }

        // TODO: public ListUsersForOrganizationForDataTableAsync
        // TODO: Other useful functions

        public async Task<UserOrganizationRole> GetRoleForUserInOrganizationAsync(Guid uid, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select tblUserOrganizationJoin.UserOrganizationRole
from tblUserOrganizationJoin
inner join tblOrganizations
on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
and tblOrganizations.Disabled = 0
where tblUserOrganizationJoin.Uid = @uid
and tblUserOrganizationJoin.OrganizationId = @organizationId
";

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                int? userOrganizationRole = await sqlConnection.QueryFirstOrDefaultAsync<int?>(commandDefinition);

                if (userOrganizationRole.HasValue)
                {
                    if (ShaneAuthHelpers.TryParseUserOrganizationRole(userOrganizationRole.Value, out UserOrganizationRole? parsedUserOrganizationRole))
                    {
                        return parsedUserOrganizationRole!.Value;
                    }
                }

                return UserOrganizationRole.NoAccess;
            }
        }

        public async Task<SqlQueryResult> UpdateUserOrganizationNoteAsync(Guid uid, Guid organizationId, string? note, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription;

            if (uid != adminUserUid)
            {
                logDescription = "Update Note (Master)";
            }
            else
            {
                logDescription = "Update Note";
            }

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()

declare @_data table
(
    UserOrganizationRole int
   ,Note nvarchar(500)
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
   ,OldNote nvarchar(500)
)

update tblUserOrganizationJoin
set Note = @note
output inserted.UserOrganizationRole
      ,inserted.Note
      ,inserted.Contractor
      ,inserted.Visitor
      ,inserted.UserOrganizationDisabled
      ,deleted.Note
      into @_data
where Uid = @uid
and OrganizationId = @organizationId

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblUserOrganizationJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Note
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,OldUserOrganizationRole
    ,OldNote
    ,OldContractor
    ,OldVisitor
    ,OldUserOrganizationDisabled
    ,LogAction)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,@organizationId
          ,d.UserOrganizationRole
          ,d.Note
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,d.UserOrganizationRole
          ,d.OldNote
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,'Update' -- LogAction
    from @_data d
end
else
begin
    -- Record was not updated
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
                parameters.Add("@note", note, DbType.String, ParameterDirection.Input, 500);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

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

        /// <summary>
        /// <para>Updates a user's settings within an organization. Intended to be used in Master User Settings panel.</para>
        /// </summary>
        /// <param name="req"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserManagementResult, UserData?)> MasterUpdateUserOrganizationAsync(MasterUpdateUserOrganizationRequest req, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update User Organization (Master)";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')

declare @_data table
(
    UserOrganizationRole int
   ,Note nvarchar(500)
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
   ,OldUserOrganizationRole int
   ,OldNote nvarchar(500)
   ,OldContractor bit
   ,OldVisitor bit
   ,OldUserOrganizationDisabled bit
)

declare @_historyData table
(
    id uniqueidentifier
   ,UserOrganizationRole int
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_userAdminFunctionsLogData table
(
    BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
)

declare @_userAdminAssetTypesLogData table
(
    BuildingId uniqueidentifier
   ,AssetTypeId uniqueidentifier
)

update tblUserOrganizationJoin
set UserOrganizationRole = @userOrganizationRole
   ,Note = @note
   ,Contractor = @contractor
   ,Visitor = @visitor
   ,UserOrganizationDisabled = @userOrganizationDisabled
output inserted.UserOrganizationRole
      ,inserted.Note
      ,inserted.Contractor
      ,inserted.Visitor
      ,inserted.UserOrganizationDisabled
      ,deleted.UserOrganizationRole
      ,deleted.Note
      ,deleted.Contractor
      ,deleted.Visitor
      ,deleted.UserOrganizationDisabled
      into @_data
where Uid = @uid
and OrganizationId = @organizationId

if @@ROWCOUNT = 1
begin
    set @_result = 1

    insert into tblUserOrganizationJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Note
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,OldUserOrganizationRole
    ,OldNote
    ,OldContractor
    ,OldVisitor
    ,OldUserOrganizationDisabled
    ,LogAction)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,@organizationId
          ,d.UserOrganizationRole
          ,d.Note
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,d.OldUserOrganizationRole
          ,d.OldNote
          ,d.OldContractor
          ,d.OldVisitor
          ,d.OldUserOrganizationDisabled
          ,'Update' -- LogAction
    from @_data d

    -- Update the old row in tblUserOrganizationJoinHistories with updated EndDateUtc
    update tblUserOrganizationJoinHistories
    set UpdatedDateUtc = @_now
       ,EndDateUtc = @_last15MinuteIntervalUtc
    output inserted.id -- UserOrganizationJoinHistoryId
          ,inserted.UserOrganizationRole
          ,inserted.Contractor
          ,inserted.Visitor
          ,inserted.UserOrganizationDisabled
          ,inserted.StartDateUtc
          ,inserted.EndDateUtc
          ,deleted.EndDateUtc
          into @_historyData
    where Uid = @uid
    and OrganizationId = @organizationId
    and EndDateUtc > @_last15MinuteIntervalUtc

    insert into tblUserOrganizationJoinHistories_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,UserOrganizationJoinHistoryId
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,StartDateUtc
    ,EndDateUtc
    ,OldEndDateUtc
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select @userOrganizationJoinHistoryUpdateLogId -- id
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,h.id -- UserOrganizationJoinHistoryId
          ,@uid
          ,@organizationId
          ,h.UserOrganizationRole
          ,h.Contractor
          ,h.Visitor
          ,h.UserOrganizationDisabled
          ,h.StartDateUtc
          ,h.EndDateUtc
          ,h.OldEndDateUtc
          ,'Update' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_historyData h
            
    -- Insert a new row into tblUserOrganizationJoinHistories for the user/organization we just updated,
    -- using the last 15 minute interval for StartDateUtc and StartDateLocal
    insert into tblUserOrganizationJoinHistories
    (id
    ,InsertDateUtc
    ,UpdatedDateUtc
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,StartDateUtc
    ,EndDateUtc)
    select @userOrganizationJoinHistoryId -- id
          ,@_now -- InsertDateUtc
          ,@_now -- UpdatedDateUtc
          ,@uid
          ,@organizationId
          ,@userOrganizationRole
          ,@contractor
          ,@visitor
          ,@userOrganizationDisabled
          ,@_last15MinuteIntervalUtc -- StartDateUtc
          ,@endOfTheWorldUtc -- EndDateUtc

    -- Write to log for the user organization join history for the new row
    insert into tblUserOrganizationJoinHistories_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,UserOrganizationJoinHistoryId
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,StartDateUtc
    ,EndDateUtc
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select @userOrganizationJoinHistoryLogId -- id
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@userOrganizationJoinHistoryId
          ,@uid
          ,@organizationId
          ,@userOrganizationRole
          ,@contractor
          ,@visitor
          ,@userOrganizationDisabled
          ,@_last15MinuteIntervalUtc -- StartDateUtc
          ,@endOfTheWorldUtc -- EndDateUtc
          ,'Insert' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId

    -- If user organization role is not Admin, remove Admin Function and Admin Asset Types
    if @userOrganizationRole != {(int)UserOrganizationRole.Admin}
    begin
        -- Delete all user admin functions and insert into log table variable
        delete from tblUserAdminFunctions
        output deleted.BuildingId
              ,deleted.FunctionId
              into @_userAdminFunctionsLogData
        where Uid = @uid
        and BuildingId in
        (
            select id
            from tblBuildings
            where Deleted = 0
            and OrganizationId = @organizationId
        )

        -- Insert to user admin functions log
        ;with logIds as (
            select ids.BuildingId, ids.FunctionId, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_userAdminFunctionsLogData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by BuildingId, FunctionId) as RowNumber, BuildingId, FunctionId
                from @_userAdminFunctionsLogData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblUserAdminFunctions_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,Uid
        ,BuildingId
        ,FunctionId
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now -- InsertDateUtc
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,@uid
              ,d.BuildingId
              ,d.FunctionId
              ,'Delete' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- log ID for tblUserOrganizationJoin
        from @_userAdminFunctionsLogData d
        left join logIds l
        on d.BuildingId = l.BuildingId
        and d.FunctionId = l.FunctionId

        -- Delete removed user admin asset types and insert into log table variable
        delete from tblUserAdminAssetTypes
        output deleted.BuildingId
              ,deleted.AssetTypeId
              into @_userAdminAssetTypesLogData
        where Uid = @uid
        and BuildingId in
        (
            select id
            from tblBuildings
            where Deleted = 0
            and OrganizationId = @organizationId
        )

        -- Insert to user admin asset types log
        ;with logIds as (
            select ids.BuildingId, ids.AssetTypeId, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_userAdminAssetTypesLogData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by BuildingId, AssetTypeId) as RowNumber, BuildingId, AssetTypeId
                from @_userAdminAssetTypesLogData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblUserAdminAssetTypes_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,Uid
        ,BuildingId
        ,AssetTypeId
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now -- InsertDateUtc
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,@uid
              ,d.BuildingId
              ,d.AssetTypeId
              ,'Delete' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- log ID for tblUserOrganizationJoin
        from @_userAdminAssetTypesLogData d
        left join logIds l
        on d.BuildingId = l.BuildingId
        and d.AssetTypeId = l.AssetTypeId
    end
end
else
begin
    -- Record was not updated
    set @_result = 2
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select Uid
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,LastAccessDateUtc
          ,LastPasswordChangeDateUtc
          ,Email
          ,HasPassword
          ,TotpEnabled
          ,UserSystemRole
          ,DisplayName
          ,FirstName
          ,Surname
          ,Timezone
          ,AvatarUrl
          ,AvatarThumbnailUrl
          ,Disabled
          ,ConcurrencyKey
    from tblUsers
    where Deleted = 0
    and Uid = @uid

    if @@ROWCOUNT = 1
    begin
        -- Also query user's organization access
        select tblUserOrganizationJoin.OrganizationId as id
              ,tblOrganizations.Name
              ,tblOrganizations.LogoImageUrl
              ,tblOrganizations.CheckInEnabled
              ,tblOrganizations.WorkplacePortalEnabled
              ,tblOrganizations.WorkplaceAccessRequestsEnabled
              ,tblOrganizations.WorkplaceInductionsEnabled
              ,tblUserOrganizationJoin.UserOrganizationRole
              ,tblUserOrganizationJoin.Note
              ,tblUserOrganizationJoin.Contractor
              ,tblUserOrganizationJoin.Visitor
              ,tblUserOrganizationJoin.UserOrganizationDisabled
              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserOrganizationJoin
        inner join tblOrganizations
        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        and tblOrganizations.Disabled = 0
        where tblUserOrganizationJoin.Uid = @uid
        order by tblOrganizations.Name

        -- Also query user's last used building
        select Uid
              ,WebLastUsedOrganizationId
              ,WebLastUsedBuildingId
              ,MobileLastUsedOrganizationId
              ,MobileLastUsedBuildingId
        from tblUserLastUsedBuilding
        where Uid = @uid

        -- Also query user's building access
        select tblUserBuildingJoin.BuildingId as id
              ,tblBuildings.Name
              ,tblBuildings.OrganizationId
              ,tblBuildings.Timezone
              ,tblBuildings.CheckInEnabled
              ,0 as HasBookableMeetingRooms -- Queried separately
              ,0 as HasBookableAssetSlots -- Queried separately
              ,tblUserBuildingJoin.FunctionId
              ,tblFunctions.Name as FunctionName
              ,tblFunctions.HtmlColor as FunctionHtmlColor
              ,tblUserBuildingJoin.FirstAidOfficer
              ,tblUserBuildingJoin.FireWarden
              ,tblUserBuildingJoin.PeerSupportOfficer
              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblUserBuildingJoin.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblFunctions
        on tblUserBuildingJoin.FunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblUserBuildingJoin.Uid = @uid
        order by tblBuildings.Name

        -- Also query user's buildings with bookable desks
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblDesks
            inner join tblFloors
            on tblDesks.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblDesks.Deleted = 0
            and tblDesks.DeskType != {(int)DeskType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query user's buildings with bookable meeting rooms
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblMeetingRooms
            inner join tblFloors
            on tblMeetingRooms.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblMeetingRooms.Deleted = 0
            and tblMeetingRooms.OfflineRoom = 0
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
            and
            (
                tblMeetingRooms.RestrictedRoom = 0
                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
            )
        )

        -- Also query user's buildings with bookable asset slots
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblAssetSlots
            inner join tblAssetSections
            on tblAssetSlots.AssetSectionId = tblAssetSections.id
            and tblAssetSections.Deleted = 0
            inner join tblAssetTypes
            on tblAssetSections.AssetTypeId = tblAssetTypes.id
            and tblAssetTypes.Deleted = 0
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblAssetSlots.Deleted = 0
            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query the user's permanent seat
        select tblDesks.id as DeskId
              ,tblBuildings.id as BuildingId
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType = {(int)DeskType.Permanent}
        and tblDesks.PermanentOwnerUid = @uid

        -- Also query the user's asset types
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblUserAssetTypeJoin
        inner join tblAssetTypes
        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblUserAssetTypeJoin.Uid = @uid

        -- Also query the user's permanent assets
        select tblAssetSlots.id as AssetSlotId
              ,tblAssetSections.AssetTypeId
              ,tblBuildings.id as BuildingId
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
        and tblAssetSlots.PermanentOwnerUid = @uid

        -- Also query the user's admin functions if the user is an Admin,
        -- or all functions if they are a Super Admin.
        select tblFunctions.id
              ,tblFunctions.Name
              ,tblFunctions.BuildingId
        from tblFunctions
        where tblFunctions.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminFunctions
            on tblFunctions.id = tblUserAdminFunctions.FunctionId
            and tblUserAdminFunctions.Uid = @uid
            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminFunctions.FunctionId is not null
                )
            )
        )

        -- Also query the user's admin asset types if the user is an Admin,
        -- or all asset types if they are a Super Admin.
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblAssetTypes
        where tblAssetTypes.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminAssetTypes
            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
            and tblUserAdminAssetTypes.Uid = @uid
            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminAssetTypes.AssetTypeId is not null
                )
            )
        )
    end
end
";
                Guid logId = RT.Comb.Provider.Sql.Create();

                // Generate ids to be used when updating old tblUserOrganizationJoinHistories,
                // as well as inserting to tblUserOrganizationJoinHistories and tblUserOrganizationJoinHistories_Log
                Guid userOrganizationJoinHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", req.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@organizationId", req.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@note", req.Note, DbType.String, ParameterDirection.Input, 500);
                parameters.Add("@userOrganizationRole", req.UserOrganizationRole, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@contractor", req.Contractor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@visitor", req.Visitor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@userOrganizationDisabled", req.UserOrganizationDisabled, DbType.Boolean, ParameterDirection.Input);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@userOrganizationJoinHistoryUpdateLogId", userOrganizationJoinHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinHistoryId", userOrganizationJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinHistoryLogId", userOrganizationJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);

                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = null;

                UserManagementResult sqlQueryResult;

                switch (resultCode)
                {
                    case 1:
                        sqlQueryResult = UserManagementResult.Ok;

                        // If update was successful, also get the data
                        if (!gridReader.IsConsumed)
                        {
                            userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                            if (!gridReader.IsConsumed && userData is not null)
                            {
                                // Read extended data
                                userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                                userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                                List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                                List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                                List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                                List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                                List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                                List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                                List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                                List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                                List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                                UsersRepository.FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                            }
                        }

                        // Invalidate cache so that if the user is currently logged in and using the system,
                        // the change will take effect right away.
                        await _authCacheService.InvalidateUserOrganizationPermissionCacheAsync(req.Uid!.Value, req.OrganizationId!.Value);
                        break;
                    case 2:
                        sqlQueryResult = UserManagementResult.UserDidNotExist;
                        break;
                    default:
                        sqlQueryResult = UserManagementResult.UnknownError;
                        break;
                }

                return (sqlQueryResult, userData);
            }
        }

        /// <summary>
        /// <para>Adds a userto an organization. Intended to be used in Master User Settings panel.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<(UserManagementResult, UserData?)> MasterAddUserToOrganizationAsync(MasterAddUserToOrganizationRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            // Note: request.Uid, request.OrganizationId, request.BuildingId and request.FunctionId are already validated in the endpoint before calling this function

            string logDescription = "Add User To Organization (Master)";

            DynamicParameters parameters = new DynamicParameters();

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                StringBuilder sql = new StringBuilder();

                sql.AppendLine(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult1 int
declare @_lockResult2 int
declare @_userAssetTypesValid bit = 1
declare @_userAdminFunctionsValid bit = 1
declare @_userAdminAssetTypesValid bit = 1

declare @_userAssetTypesData table
(
    AssetTypeId uniqueidentifier
   ,LogId uniqueidentifier
)

declare @_userAdminFunctionsData table
(
    FunctionId uniqueidentifier
   ,LogId uniqueidentifier
)

declare @_userAdminAssetTypesData table
(
    AssetTypeId uniqueidentifier
   ,LogId uniqueidentifier
)
");
                // Insert UserAssetTypes into query
                if (request.UserAssetTypes is not null && request.UserAssetTypes.Count > 0)
                {
                    sql.AppendLine(@"
insert into @_userAssetTypesData
(AssetTypeId, LogId)
values");
                    for (int i = 0; i < request.UserAssetTypes.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sql.Append(',');
                        }

                        sql.AppendLine($"(@userAssetTypeId{i},@userAssetTypeLogId{i})");
                        parameters.Add($"@userAssetTypeId{i}", request.UserAssetTypes[i], DbType.Guid, ParameterDirection.Input);
                        parameters.Add($"@userAssetTypeLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                    }
                }

                // For Admin, populate UserAdminFunctions and UserAdminAssetTypes into query 
                switch (request.UserOrganizationRole)
                {
                    case UserOrganizationRole.NoAccess:
                    case UserOrganizationRole.User:
                    case UserOrganizationRole.SuperAdmin:
                    case UserOrganizationRole.Tablet:
                        break;
                    case UserOrganizationRole.Admin:
                        // Insert UserAdminFunctions into query
                        if (request.UserAdminFunctions is not null && request.UserAdminFunctions.Count > 0)
                        {
                            sql.AppendLine(@"
insert into @_userAdminFunctionsData
(FunctionId, LogId)
values");
                            for (int i = 0; i < request.UserAdminFunctions.Count; ++i)
                            {
                                if (i > 0)
                                {
                                    sql.Append(',');
                                }

                                sql.AppendLine($"(@userAdminFunctionId{i},@userAdminFunctionLogId{i})");
                                parameters.Add($"@userAdminFunctionId{i}", request.UserAdminFunctions[i], DbType.Guid, ParameterDirection.Input);
                                parameters.Add($"@userAdminFunctionLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                            }
                        }

                        // Insert UserAdminAssetTypes into query
                        if (request.UserAdminAssetTypes is not null && request.UserAdminAssetTypes.Count > 0)
                        {
                            sql.AppendLine(@"
insert into @_userAdminAssetTypesData
(AssetTypeId, LogId)
values");
                            for (int i = 0; i < request.UserAdminAssetTypes.Count; ++i)
                            {
                                if (i > 0)
                                {
                                    sql.Append(',');
                                }

                                sql.AppendLine($"(@userAdminAssetTypeId{i},@userAdminAssetTypeLogId{i})");
                                parameters.Add($"@userAdminAssetTypeId{i}", request.UserAdminAssetTypes[i], DbType.Guid, ParameterDirection.Input);
                                parameters.Add($"@userAdminAssetTypeLogId{i}", RT.Comb.EnsureOrderedProvider.Sql.Create(), DbType.Guid, ParameterDirection.Input);
                            }
                        }
                        break;
                    default:
                        throw new Exception($"Unknown UserOrganizationRole: {request.UserOrganizationRole}");
                }

                sql.AppendLine($@"
-- Validate UserAssetTypes
select top 1 @_userAssetTypesValid = 0
from @_userAssetTypesData d
where not exists
(
    select *
    from tblAssetTypes
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetTypes.Deleted = 0
    and tblAssetTypes.BuildingId = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and d.AssetTypeId = tblAssetTypes.id
)

-- Validate UserAdminFunctions
select top 1 @_userAdminFunctionsValid = 0
from @_userAdminFunctionsData d
where not exists
(
    select *
    from tblFunctions
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblFunctions.Deleted = 0
    and tblFunctions.BuildingId = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and d.FunctionId = tblFunctions.id
)

-- Validate UserAdminAssetTypes
select top 1 @_userAdminAssetTypesValid = 0
from @_userAdminAssetTypesData d
where not exists
(
    select *
    from tblAssetTypes
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetTypes.Deleted = 0
    and tblAssetTypes.BuildingId = @buildingId
    and tblBuildings.OrganizationId = @organizationId
    and d.AssetTypeId = tblAssetTypes.id
)

if @_userAssetTypesValid = 0
begin
    -- At least one of UserAssetTypes is invalid
    set @_result = 3
end
else if @_userAdminFunctionsValid = 0
begin
    -- At least one of UserAdminFunctions is invalid
    set @_result = 4
end
else if @_userAdminAssetTypesValid = 0
begin
    -- At least one of UserAdminAssetTypes is invalid
    set @_result = 5
end
else
begin
    begin transaction

    -- Get a lock on both adding an organization to the user, and adding a building to the user,
    -- since we are going to do both of those.
    exec @_lockResult1 = sp_getapplock
         @Resource = @lockResourceName1,
         @LockMode = 'Exclusive',
         @LockOwner = 'Transaction',
         @LockTimeout = 0

    exec @_lockResult2 = sp_getapplock
         @Resource = @lockResourceName2,
         @LockMode = 'Exclusive',
         @LockOwner = 'Transaction',
         @LockTimeout = 0

    if @_lockResult1 < 0 or @_lockResult2 < 0
    begin
        set @_result = 999
        rollback
    end
    else
    begin
        insert into tblUserOrganizationJoin
        (Uid
        ,OrganizationId
        ,UserOrganizationRole
        ,Note
        ,Contractor
        ,Visitor
        ,UserOrganizationDisabled
        ,InsertDateUtc)
        select @uid
              ,@organizationId
              ,@userOrganizationRole
              ,@note
              ,@contractor
              ,@visitor
              ,@userOrganizationDisabled
              ,@_now -- InsertDateUtc
        where not exists
        (
            select *
            from tblUserOrganizationJoin
            where Uid = @uid
            and OrganizationId = @organizationId
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

            insert into tblUserOrganizationJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Note
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,LogAction)
            select @logid
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@uid
                  ,@organizationId
                  ,@userOrganizationRole
                  ,@note
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled
                  ,'Insert' -- LogAction

            -- Insert a new row into tblUserOrganizationJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserOrganizationJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc)
            select @userOrganizationJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@uid
                  ,@organizationId
                  ,@userOrganizationRole
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user organization join history for the new user
            insert into tblUserOrganizationJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,UserOrganizationJoinHistoryId
            ,Uid
            ,OrganizationId
            ,UserOrganizationRole
            ,Contractor
            ,Visitor
            ,UserOrganizationDisabled
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userOrganizationJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@userOrganizationJoinHistoryId
                  ,@uid
                  ,@organizationId
                  ,@userOrganizationRole
                  ,@contractor
                  ,@visitor
                  ,@userOrganizationDisabled
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUserOrganizationJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin

            -- Delete existing rows just in case to prevent primary key violation.
            -- Should not be any existing rows unless database was touched manually,
            -- because if this user did not have access to the organization, then
            -- they should also not have access to the building.
            delete tblUserBuildingJoin
            where Uid = @uid
            and BuildingId = @buildingId

            delete tblUserAssetTypeJoin
            where Uid = @uid
            and BuildingId = @buildingId

            delete tblUserAdminFunctions
            where Uid = @uid
            and BuildingId = @buildingId

            delete tblUserAdminAssetTypes
            where Uid = @uid
            and BuildingId = @buildingId

            insert into tblUserBuildingJoin
            (Uid
            ,BuildingId
            ,InsertDateUtc
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere)
            select @uid
                  ,@buildingId
                  ,@_now -- InsertDateUtc
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere

            insert into tblUserBuildingJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinLogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere
                  ,'Insert' -- LogAction
                  ,'tblUserOrganizationJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin

            -- Insert a new row into tblUserBuildingJoinHistories for the user we just created,
            -- using the last 15 minute interval for StartDateUtc and StartDateLocal
            insert into tblUserBuildingJoinHistories
            (id
            ,InsertDateUtc
            ,UpdatedDateUtc
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc)
            select @userBuildingJoinHistoryId -- id
                  ,@_now -- InsertDateUtc
                  ,@_now -- UpdatedDateUtc
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc

            -- Write to log for the user building join history for the new user
            insert into tblUserBuildingJoinHistories_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,UserBuildingJoinHistoryId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,FirstAidOfficer
            ,FireWarden
            ,PeerSupportOfficer
            ,AllowBookingDeskForVisitor
            ,AllowBookingRestrictedRooms
            ,AllowBookingAnyoneAnywhere
            ,StartDateUtc
            ,EndDateUtc
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select @userBuildingJoinHistoryLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@userBuildingJoinHistoryId
                  ,@uid
                  ,@buildingId
                  ,@functionId
                  ,@firstAidOfficer
                  ,@fireWarden
                  ,@peerSupportOfficer
                  ,@allowBookingDeskForVisitor
                  ,@allowBookingRestrictedRooms
                  ,@allowBookingAnyoneAnywhere
                  ,@_last15MinuteIntervalUtc -- StartDateUtc
                  ,@endOfTheWorldUtc -- EndDateUtc
                  ,'Insert' -- LogAction
                  ,'tblUserOrganizationJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin

            insert into tblUserAssetTypeJoin
            (Uid
            ,BuildingId
            ,AssetTypeId
            ,InsertDateUtc)
            select @uid
                  ,d.AssetTypeId
                  ,@buildingId
                  ,@_now -- InsertDateUtc
            from @_userAssetTypesData d

            insert into tblUserAssetTypeJoin_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,AssetTypeId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select d.LogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,'Insert' -- LogAction
                  ,'tblUserOrganizationJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin
            from @_userAssetTypesData d

            insert into tblUserAdminFunctions
            (Uid
            ,BuildingId
            ,FunctionId
            ,InsertDateUtc)
            select @uid
                  ,@buildingId
                  ,d.FunctionId
                  ,@_now -- InsertDateUtc
            from @_userAdminFunctionsData d

            insert into tblUserAdminFunctions_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,BuildingId
            ,FunctionId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select d.LogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,d.FunctionId
                  ,'Insert' -- LogAction
                  ,'tblUserOrganizationJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin
            from @_userAdminFunctionsData d

            insert into tblUserAdminAssetTypes
            (Uid
            ,BuildingId
            ,AssetTypeId
            ,InsertDateUtc)
            select @uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,@_now -- InsertDateUtc
            from @_userAdminAssetTypesData d

            insert into tblUserAdminAssetTypes_Log
            (id
            ,InsertDateUtc
            ,UpdatedByUid
            ,UpdatedByDisplayName
            ,UpdatedByIpAddress
            ,LogDescription
            ,OrganizationId
            ,Uid
            ,AssetTypeId
            ,LogAction
            ,CascadeFrom
            ,CascadeLogId)
            select d.LogId
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,d.AssetTypeId
                  ,'Insert' -- LogAction
                  ,'tblUserOrganizationJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserOrganizationJoin
            from @_userAdminAssetTypesData d
        end
        else
        begin
            -- Record already exists
            set @_result = 2
        end

        commit
    end
end

select @_result

if @_result = 1
begin
    -- Select row to return with the API result
    select Uid
          ,InsertDateUtc
          ,UpdatedDateUtc
          ,LastAccessDateUtc
          ,LastPasswordChangeDateUtc
          ,Email
          ,HasPassword
          ,TotpEnabled
          ,UserSystemRole
          ,DisplayName
          ,FirstName
          ,Surname
          ,Timezone
          ,AvatarUrl
          ,AvatarThumbnailUrl
          ,Disabled
          ,ConcurrencyKey
    from tblUsers
    where Deleted = 0
    and Uid = @uid

    if @@ROWCOUNT = 1
    begin
        -- Also query user's organization access
        select tblUserOrganizationJoin.OrganizationId as id
              ,tblOrganizations.Name
              ,tblOrganizations.LogoImageUrl
              ,tblOrganizations.CheckInEnabled
              ,tblOrganizations.WorkplacePortalEnabled
              ,tblOrganizations.WorkplaceAccessRequestsEnabled
              ,tblOrganizations.WorkplaceInductionsEnabled
              ,tblUserOrganizationJoin.UserOrganizationRole
              ,tblUserOrganizationJoin.Note
              ,tblUserOrganizationJoin.Contractor
              ,tblUserOrganizationJoin.Visitor
              ,tblUserOrganizationJoin.UserOrganizationDisabled
              ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserOrganizationJoin
        inner join tblOrganizations
        on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
        and tblOrganizations.Deleted = 0
        and tblOrganizations.Disabled = 0
        where tblUserOrganizationJoin.Uid = @uid
        order by tblOrganizations.Name

        -- Also query user's last used building
        select Uid
              ,WebLastUsedOrganizationId
              ,WebLastUsedBuildingId
              ,MobileLastUsedOrganizationId
              ,MobileLastUsedBuildingId
        from tblUserLastUsedBuilding
        where Uid = @uid

        -- Also query user's building access
        select tblUserBuildingJoin.BuildingId as id
              ,tblBuildings.Name
              ,tblBuildings.OrganizationId
              ,tblBuildings.Timezone
              ,tblBuildings.CheckInEnabled
              ,0 as HasBookableMeetingRooms -- Queried separately
              ,0 as HasBookableAssetSlots -- Queried separately
              ,tblUserBuildingJoin.FunctionId
              ,tblFunctions.Name as FunctionName
              ,tblFunctions.HtmlColor as FunctionHtmlColor
              ,tblUserBuildingJoin.FirstAidOfficer
              ,tblUserBuildingJoin.FireWarden
              ,tblUserBuildingJoin.PeerSupportOfficer
              ,tblUserBuildingJoin.AllowBookingDeskForVisitor
              ,tblUserBuildingJoin.AllowBookingRestrictedRooms
              ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
              ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblUserBuildingJoin.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblFunctions
        on tblUserBuildingJoin.FunctionId = tblFunctions.id
        and tblFunctions.Deleted = 0
        where tblUserBuildingJoin.Uid = @uid
        order by tblBuildings.Name

        -- Also query user's buildings with bookable desks
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblDesks
            inner join tblFloors
            on tblDesks.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblDesks.Deleted = 0
            and tblDesks.DeskType != {(int)DeskType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query user's buildings with bookable meeting rooms
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblMeetingRooms
            inner join tblFloors
            on tblMeetingRooms.FloorId = tblFloors.id
            and tblFloors.Deleted = 0
            inner join tblBuildings
            on tblFloors.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblMeetingRooms.Deleted = 0
            and tblMeetingRooms.OfflineRoom = 0
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
            and
            (
                tblMeetingRooms.RestrictedRoom = 0
                or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
            )
        )

        -- Also query user's buildings with bookable asset slots
        select tblUserBuildingJoin.BuildingId
        from tblUserBuildingJoin
        where tblUserBuildingJoin.Uid = @uid
        and exists
        (
            select *
            from tblAssetSlots
            inner join tblAssetSections
            on tblAssetSlots.AssetSectionId = tblAssetSections.id
            and tblAssetSections.Deleted = 0
            inner join tblAssetTypes
            on tblAssetSections.AssetTypeId = tblAssetTypes.id
            and tblAssetTypes.Deleted = 0
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            where tblAssetSlots.Deleted = 0
            and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
            and tblBuildings.id = tblUserBuildingJoin.BuildingId
        )

        -- Also query the user's permanent seat
        select tblDesks.id as DeskId
              ,tblBuildings.id as BuildingId
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType = {(int)DeskType.Permanent}
        and tblDesks.PermanentOwnerUid = @uid

        -- Also query the user's asset types
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblUserAssetTypeJoin
        inner join tblAssetTypes
        on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblUserAssetTypeJoin.Uid = @uid

        -- Also query the user's permanent assets
        select tblAssetSlots.id as AssetSlotId
              ,tblAssetSections.AssetTypeId
              ,tblBuildings.id as BuildingId
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
        and tblAssetSlots.PermanentOwnerUid = @uid

        -- Also query the user's admin functions if the user is an Admin,
        -- or all functions if they are a Super Admin.
        select tblFunctions.id
              ,tblFunctions.Name
              ,tblFunctions.BuildingId
        from tblFunctions
        where tblFunctions.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblFunctions.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminFunctions
            on tblFunctions.id = tblUserAdminFunctions.FunctionId
            and tblUserAdminFunctions.Uid = @uid
            where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminFunctions.FunctionId is not null
                )
            )
        )

        -- Also query the user's admin asset types if the user is an Admin,
        -- or all asset types if they are a Super Admin.
        select tblAssetTypes.id
              ,tblAssetTypes.Name
              ,tblAssetTypes.BuildingId
              ,tblAssetTypes.LogoImageUrl
        from tblAssetTypes
        where tblAssetTypes.Deleted = 0
        and exists
        (
            select *
            from tblUserBuildingJoin
            inner join tblBuildings
            on tblAssetTypes.BuildingId = tblBuildings.id
            and tblBuildings.Deleted = 0
            inner join tblUserOrganizationJoin
            on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
            and tblUserOrganizationJoin.Uid = @uid
            left join tblUserAdminAssetTypes
            on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
            and tblUserAdminAssetTypes.Uid = @uid
            where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
            and tblUserBuildingJoin.Uid = @uid
            and
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
                or
                (
                    tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                    and tblUserAdminAssetTypes.AssetTypeId is not null
                )
            )
        )
    end
end
");
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                // Generate ids to be used when inserting to tblUserOrganizationJoinHistories and tblUserOrganizationJoinHistories_Log
                // as well as tblUserBuildingJoinHistories and tblUserBuildingJoinHistories_Log
                Guid userOrganizationJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                parameters.Add("@lockResourceName1", $"tblUserOrganizationJoin_{request.Uid}_{request.OrganizationId}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@lockResourceName2", $"tblUserBuildingJoin_{request.Uid}_{request.BuildingId}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                // Organization details
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationRole", request.UserOrganizationRole, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@contractor", request.Contractor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@visitor", request.Visitor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@note", request.Note, DbType.String, ParameterDirection.Input, 500);
                parameters.Add("@userOrganizationDisabled", request.UserOrganizationDisabled, DbType.Boolean, ParameterDirection.Input);

                // Building details
                parameters.Add("@functionId", request.FunctionId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@firstAidOfficer", request.FirstAidOfficer, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@fireWarden", request.FireWarden, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@peerSupportOfficer", request.PeerSupportOfficer, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@allowBookingDeskForVisitor", request.AllowBookingDeskForVisitor, DbType.Boolean, ParameterDirection.Input);
                parameters.Add("@allowBookingRestrictedRooms", request.AllowBookingRestrictedRooms, DbType.Boolean, ParameterDirection.Input);

                // Building Admin details
                parameters.Add("@allowBookingAnyoneAnywhere", request.AllowBookingAnyoneAnywhere, DbType.Boolean, ParameterDirection.Input);

                // Histories
                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@userOrganizationJoinHistoryId", userOrganizationJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userOrganizationJoinHistoryLogId", userOrganizationJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryId", userBuildingJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryLogId", userBuildingJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);

                // Logs
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinLogId", userBuildingJoinLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using SqlMapper.GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql.ToString(), parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = null;

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                    if (!gridReader.IsConsumed && userData is not null)
                    {
                        // Read extended data
                        userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                        userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                        List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                        List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                        List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                        List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                        List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                        List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                        UsersRepository.FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                    }
                }

                UserManagementResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = UserManagementResult.Ok;

                        // Invalidate cache so that if the user is currently logged in and using the system,
                        // the change will take effect right away.
                        await _authCacheService.InvalidateUserOrganizationPermissionCacheAsync(request.Uid!.Value, request.OrganizationId!.Value);
                        break;
                    case 2:
                        queryResult = UserManagementResult.UserAlreadyExistsInOrganization;
                        break;
                    case 3:
                        queryResult = UserManagementResult.UserAssetTypesInvalid;
                        break;
                    case 4:
                        queryResult = UserManagementResult.UserAdminFunctionsInvalid;
                        break;
                    case 5:
                        queryResult = UserManagementResult.UserAdminAssetTypesInvalid;
                        break;
                    default:
                        queryResult = UserManagementResult.UnknownError;
                        break;
                }

                return (queryResult, userData);
            }
        }

        /// <summary>
        /// <para>Removes the specified user from the specified organization.</para>
        /// <para>Returns: <see cref="UserManagementResult.Ok"/>, <see cref="UserManagementResult.UserDidNotExist"/>, <see cref="UserManagementResult.UserDidNotExistInOrganization"/>,
        /// <see cref="UserManagementResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserManagementResult, UserData?)> MasterRemoveUserFromOrganizationAsync(MasterRemoveUserFromOrganizationRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Remove User From Organization (Master)";

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                // Get timezones for all buildings
                string sql = @"
select id
      ,Timezone
from tblBuildings
where OrganizationId = @organizationId
and Deleted = 0
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);

                List<(Guid buildingId, string timezone)> buildingTimezones = (await sqlConnection.QueryAsync<(Guid, string)>(sql, parameters)).AsList();

                // Build SQL query to store all building timezone UtcOffsetMinutes in table variable
                StringBuilder timezoneSql = new StringBuilder(@"
declare @_buildingTimezones table
(
    BuildingId uniqueidentifier
   ,UtcOffsetMinutes int
   ,NowLocal datetime2(3)
   ,Last15MinuteIntervalLocal datetime2(3)
   ,EndOfTheWorldLocal datetime2(3)
)
");
                parameters = new DynamicParameters();

                if (buildingTimezones.Count > 0)
                {
                    timezoneSql.AppendLine(@"
insert into @_buildingTimezones
(BuildingId
,UtcOffsetMinutes
,NowLocal
,Last15MinuteIntervalLocal
,EndOfTheWorldLocal)
values");

                    for (int i = 0; i < buildingTimezones.Count; ++i)
                    {
                        if (i > 0)
                        {
                            timezoneSql.Append(',');
                        }

                        TimeZoneInfo timeZoneInfo = TimeZoneInfo.FindSystemTimeZoneById(buildingTimezones[i].timezone);
                        timezoneSql.AppendLine($"(@buildingId{i},@utcOffsetMinutes{i},null,null,null)");
                        parameters.Add($"@buildingId{i}", buildingTimezones[i].buildingId, DbType.Guid, ParameterDirection.Input); ;
                        parameters.Add($"@utcOffsetMinutes{i}", (int)timeZoneInfo.BaseUtcOffset.TotalMinutes, DbType.Int32, ParameterDirection.Input); ;
                    }

                    timezoneSql.AppendLine(@"
update @_buildingTimezones
set NowLocal = dateadd(minute, UtcOffsetMinutes, @_now)
   ,Last15MinuteIntervalLocal = dateadd(minute, datediff(minute, '2000-01-01', dateadd(minute, UtcOffsetMinutes, @_now)) / 15 * 15, '2000-01-01')
   ,EndOfTheWorldLocal = dateadd(minute, UtcOffsetMinutes, @endOfTheWorldUtc)
");
                }

                sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_nowPlus1 datetime2(3) = dateadd(millisecond, 1, sysutcdatetime())
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')

{timezoneSql}

declare @_userOrganizationJoinData table
(
    UserOrganizationRole int
   ,Note nvarchar(500)
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
)

declare @_userOrganizationJoinHistoryData table
(
    id uniqueidentifier
   ,UserOrganizationRole int
   ,Contractor bit
   ,Visitor bit
   ,UserOrganizationDisabled bit
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_userBuildingJoinData table
(
    BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
   ,FirstAidOfficer bit
   ,FireWarden bit
   ,PeerSupportOfficer bit
   ,AllowBookingDeskForVisitor bit
   ,AllowBookingRestrictedRooms bit
   ,AllowBookingAnyoneAnywhere bit
)

declare @_userBuildingJoinHistoryData table
(
    id uniqueidentifier
   ,BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
   ,FirstAidOfficer bit
   ,FireWarden bit
   ,PeerSupportOfficer bit
   ,AllowBookingDeskForVisitor bit
   ,AllowBookingRestrictedRooms bit
   ,AllowBookingAnyoneAnywhere bit
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,OldEndDateUtc datetime2(3)
)

declare @_userAssetTypesLogData table
(
    BuildingId uniqueidentifier
   ,AssetTypeId uniqueidentifier
)

declare @_userAdminFunctionsLogData table
(
    BuildingId uniqueidentifier
   ,FunctionId uniqueidentifier
)

declare @_userAdminAssetTypesLogData table
(
    BuildingId uniqueidentifier
   ,AssetTypeId uniqueidentifier
)

declare @_deskData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,DeskType int
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,PermanentOwnerUid uniqueidentifier
   ,XAxis float
   ,YAxis float
   ,OldDeskType int
   ,OldPermanentOwnerUid uniqueidentifier
)

declare @_deskHistoryData table
(
    id uniqueidentifier
   ,DeskId uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,DeskType int
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,OldEndDateUtc datetime2(3)
   ,OldEndDateLocal datetime2(3)
)

declare @_newDeskHistoryData table
(
    id uniqueidentifier
   ,DeskId uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,DeskType int
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
)

declare @_permanentDeskHistoryData table
(
    id uniqueidentifier
   ,DeskId uniqueidentifier
   ,PermanentOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_previousPermanentDesksAvailabilityData table
(
    id uniqueidentifier
   ,DeskId uniqueidentifier
   ,AvailabilityCreatorUid uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
)

declare @_previousDeskBookingsData table
(
    id uniqueidentifier
   ,DeskId uniqueidentifier
   ,BookingCreatorUid uniqueidentifier
   ,BookingOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,BookingCancelledByUid uniqueidentifier
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
   ,Cancelled bit
   ,Truncated bit
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_previousLocalMeetingRoomBookingsData table
(
    id uniqueidentifier
   ,MeetingRoomId uniqueidentifier
   ,BookingCreatorUid uniqueidentifier
   ,BookingOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,BookingCancelledByUid uniqueidentifier
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
   ,Cancelled bit
   ,Truncated bit
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_assetSlotData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,AssetSectionId uniqueidentifier
   ,AssetSlotType int
   ,PermanentOwnerUid uniqueidentifier
   ,XAxis float
   ,YAxis float
)

declare @_assetSlotHistoryData table
(
    id uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,Name nvarchar(100)
   ,AssetSectionId uniqueidentifier
   ,AssetSlotType int
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,OldEndDateUtc datetime2(3)
   ,OldEndDateLocal datetime2(3)
)

declare @_newAssetSlotHistoryData table
(
    id uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,Name nvarchar(100)
   ,AssetSectionId uniqueidentifier
   ,AssetSlotType int
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
)

declare @_previousAssetSlotBookingsData table
(
    id uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,BookingCreatorUid uniqueidentifier
   ,BookingOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,BookingCancelledByUid uniqueidentifier
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
   ,Cancelled bit
   ,Truncated bit
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

declare @_previousPermanentAssetSlotsData table
(
    id uniqueidentifier
   ,Name nvarchar(100)
   ,FloorId uniqueidentifier
   ,FunctionType int
   ,FunctionId uniqueidentifier
   ,XAxis float
   ,YAxis float
)

declare @_previousPermanentAssetSlotsAvailabilityData table
(
    id uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,AvailabilityCreatorUid uniqueidentifier
   ,StartDateUtc datetime2(3)
   ,EndDateUtc datetime2(3)
   ,StartDateLocal datetime2(3)
   ,EndDateLocal datetime2(3)
   ,CancelledDateUtc datetime2(3)
   ,CancelledDateLocal datetime2(3)
)

declare @_permanentAssetSlotHistoryData table
(
    id uniqueidentifier
   ,AssetSlotId uniqueidentifier
   ,PermanentOwnerUid uniqueidentifier
   ,BookingStartUtc datetime2(3)
   ,BookingEndUtc datetime2(3)
   ,BookingStartLocal datetime2(3)
   ,BookingEndLocal datetime2(3)
   ,OldBookingEndUtc datetime2(3)
   ,OldBookingEndLocal datetime2(3)
)

delete from tblUserOrganizationJoin
output deleted.UserOrganizationRole
      ,deleted.Note
      ,deleted.Contractor
      ,deleted.Visitor
      ,deleted.UserOrganizationDisabled
      into @_userOrganizationJoinData
from tblUserOrganizationJoin
inner join tblUsers
on tblUserOrganizationJoin.Uid = tblUsers.Uid
and tblUsers.Deleted = 0
where tblUserOrganizationJoin.Uid = @uid
and tblUserOrganizationJoin.OrganizationId = @organizationId
and tblUsers.ConcurrencyKey = @concurrencyKey

if @@ROWCOUNT = 1
begin
    set @_result = 1

    -- Insert to log
    insert into tblUserOrganizationJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Note
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,OldUserOrganizationRole
    ,OldNote
    ,OldContractor
    ,OldVisitor
    ,OldUserOrganizationDisabled
    ,LogAction)
    select @logId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@uid
          ,@organizationId
          ,d.UserOrganizationRole
          ,d.Note
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,d.UserOrganizationRole
          ,d.Note
          ,d.Contractor
          ,d.Visitor
          ,d.UserOrganizationDisabled
          ,'Delete' -- LogAction
    from @_userOrganizationJoinData d

    -- Update the old row in tblUserOrganizationJoinHistories with updated EndDateUtc
    update tblUserOrganizationJoinHistories
    set UpdatedDateUtc = @_now
       ,EndDateUtc = @_last15MinuteIntervalUtc
    output inserted.id -- UserOrganizationJoinHistoryId
          ,inserted.UserOrganizationRole
          ,inserted.Contractor
          ,inserted.Visitor
          ,inserted.UserOrganizationDisabled
          ,inserted.StartDateUtc
          ,inserted.EndDateUtc
          ,deleted.EndDateUtc
          into @_userOrganizationJoinHistoryData
    where Uid = @uid
    and OrganizationId = @organizationId
    and EndDateUtc > @_last15MinuteIntervalUtc

    insert into tblUserOrganizationJoinHistories_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,UserOrganizationJoinHistoryId
    ,Uid
    ,OrganizationId
    ,UserOrganizationRole
    ,Contractor
    ,Visitor
    ,UserOrganizationDisabled
    ,StartDateUtc
    ,EndDateUtc
    ,OldEndDateUtc
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select @userOrganizationJoinHistoryUpdateLogId -- id
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,h.id -- UserOrganizationJoinHistoryId
          ,@uid
          ,@organizationId
          ,h.UserOrganizationRole
          ,h.Contractor
          ,h.Visitor
          ,h.UserOrganizationDisabled
          ,h.StartDateUtc
          ,h.EndDateUtc
          ,h.OldEndDateUtc
          ,'Update' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userOrganizationJoinHistoryData h

    -- Remove buildings for the user in the specified organization
    delete from tblUserBuildingJoin
    output deleted.BuildingId
          ,deleted.FunctionId
          ,deleted.FirstAidOfficer
          ,deleted.FireWarden
          ,deleted.PeerSupportOfficer
          ,deleted.AllowBookingDeskForVisitor
          ,deleted.AllowBookingRestrictedRooms
          ,deleted.AllowBookingAnyoneAnywhere
          into @_userBuildingJoinData
    from tblUserBuildingJoin
    inner join tblBuildings
    on tblUserBuildingJoin.BuildingId = tblBuildings.id
    where tblUserBuildingJoin.Uid = @uid
    and tblBuildings.OrganizationId = @organizationId

    -- Insert to tblUserBuildingJoin_Log
    ;with logIds as (
        select ids.BuildingId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userBuildingJoinData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by BuildingId) as RowNumber, BuildingId
            from @_userBuildingJoinData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserBuildingJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,FunctionId
    ,FirstAidOfficer
    ,FireWarden
    ,PeerSupportOfficer
    ,AllowBookingDeskForVisitor
    ,AllowBookingRestrictedRooms
    ,AllowBookingAnyoneAnywhere
    ,OldFunctionId
    ,OldFirstAidOfficer
    ,OldFireWarden
    ,OldPeerSupportOfficer
    ,OldAllowBookingDeskForVisitor
    ,OldAllowBookingRestrictedRooms
    ,OldAllowBookingAnyoneAnywhere
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,@uid
          ,d.BuildingId
          ,d.FunctionId
          ,d.FirstAidOfficer
          ,d.FireWarden
          ,d.PeerSupportOfficer
          ,d.AllowBookingDeskForVisitor
          ,d.AllowBookingRestrictedRooms
          ,d.AllowBookingAnyoneAnywhere
          ,d.FunctionId
          ,d.FirstAidOfficer
          ,d.FireWarden
          ,d.PeerSupportOfficer
          ,d.AllowBookingDeskForVisitor
          ,d.AllowBookingRestrictedRooms
          ,d.AllowBookingAnyoneAnywhere
          ,'Delete' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userBuildingJoinData d
    left join logIds l
    on d.BuildingId = l.BuildingId

    -- Update the old row in tblUserBuildingJoinHistories with updated EndDateUtc
    update tblUserBuildingJoinHistories
    set UpdatedDateUtc = @_now
       ,EndDateUtc = @_last15MinuteIntervalUtc
    output inserted.id -- UserBuildingJoinHistoryId
          ,inserted.BuildingId
          ,inserted.FunctionId
          ,inserted.FirstAidOfficer
          ,inserted.FireWarden
          ,inserted.PeerSupportOfficer
          ,inserted.AllowBookingDeskForVisitor
          ,inserted.AllowBookingRestrictedRooms
          ,inserted.AllowBookingAnyoneAnywhere
          ,inserted.StartDateUtc
          ,inserted.EndDateUtc
          ,deleted.EndDateUtc
          into @_userBuildingJoinHistoryData
    from tblUserBuildingJoinHistories
    inner join tblBuildings
    on tblUserBuildingJoinHistories.BuildingId = tblBuildings.id
    where tblUserBuildingJoinHistories.Uid = @uid
    and tblBuildings.OrganizationId = @organizationId
    and tblUserBuildingJoinHistories.EndDateUtc > @_last15MinuteIntervalUtc

    -- Insert to tblUserBuildingJoinHistories_Log
    ;with logIds as (
        select ids.BuildingId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userBuildingJoinHistoryData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by BuildingId) as RowNumber, BuildingId
            from @_userBuildingJoinHistoryData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserBuildingJoinHistories_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,UserBuildingJoinHistoryId
    ,Uid
    ,BuildingId
    ,FunctionId
    ,FirstAidOfficer
    ,FireWarden
    ,PeerSupportOfficer
    ,AllowBookingDeskForVisitor
    ,AllowBookingRestrictedRooms
    ,AllowBookingAnyoneAnywhere
    ,StartDateUtc
    ,EndDateUtc
    ,OldEndDateUtc
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,h.id -- UserBuildingJoinHistoryId
          ,@uid
          ,h.BuildingId
          ,h.FunctionId
          ,h.FirstAidOfficer
          ,h.FireWarden
          ,h.PeerSupportOfficer
          ,h.AllowBookingDeskForVisitor
          ,h.AllowBookingRestrictedRooms
          ,h.AllowBookingAnyoneAnywhere
          ,h.StartDateUtc
          ,h.EndDateUtc
          ,h.OldEndDateUtc
          ,'Update' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userBuildingJoinHistoryData h
    left join logIds l
    on h.BuildingId = l.BuildingId

    -- Remove user asset types for the user for all buildings in the specified organization
    delete from tblUserAssetTypeJoin
    output deleted.BuildingId
          ,deleted.AssetTypeId
          into @_userAssetTypesLogData
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    where tblUserAssetTypeJoin.Uid = @uid
    and tblBuildings.OrganizationId = @organizationId

    -- Insert to tblUserAssetTypeJoin_Log
    ;with logIds as (
        select ids.AssetTypeId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAssetTypesLogData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by AssetTypeId) as RowNumber, AssetTypeId
            from @_userAssetTypesLogData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAssetTypeJoin_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,AssetTypeId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,@uid
          ,d.BuildingId
          ,d.AssetTypeId
          ,'Delete' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAssetTypesLogData d
    left join logIds l
    on d.AssetTypeId = l.AssetTypeId

    -- Remove user admin functions for the user for all buildings in the specified organization
    delete from tblUserAdminFunctions
    output deleted.BuildingId
          ,deleted.FunctionId
          into @_userAdminFunctionsLogData
    from tblUserAdminFunctions
    inner join tblFunctions
    on tblUserAdminFunctions.FunctionId = tblFunctions.id
    inner join tblBuildings
    on tblFunctions.BuildingId = tblBuildings.id
    where tblUserAdminFunctions.Uid = @uid
    and tblBuildings.OrganizationId = @organizationId

    -- Insert to tblUserAdminFunctions_Log
    ;with logIds as (
        select ids.FunctionId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAdminFunctionsLogData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by FunctionId) as RowNumber, FunctionId
            from @_userAdminFunctionsLogData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAdminFunctions_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,FunctionId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,@uid
          ,d.BuildingId
          ,d.FunctionId
          ,'Delete' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAdminFunctionsLogData d
    left join logIds l
    on d.FunctionId = l.FunctionId

    -- Remove user admin asset types for the user for all buildings in the specified organization
    delete from tblUserAdminAssetTypes
    output deleted.BuildingId
          ,deleted.AssetTypeId
          into @_userAdminAssetTypesLogData
    from tblUserAdminAssetTypes
    inner join tblAssetTypes
    on tblUserAdminAssetTypes.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    where tblUserAdminAssetTypes.Uid = @uid
    and tblBuildings.OrganizationId = @organizationId

    -- Insert to tblUserAdminAssetTypes_Log
    ;with logIds as (
        select ids.AssetTypeId, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_userAdminAssetTypesLogData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by AssetTypeId) as RowNumber, AssetTypeId
            from @_userAdminAssetTypesLogData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblUserAdminAssetTypes_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,Uid
    ,BuildingId
    ,AssetTypeId
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,@uid
          ,d.BuildingId
          ,d.AssetTypeId
          ,'Delete' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAdminAssetTypesLogData d
    left join logIds l
    on d.AssetTypeId = l.AssetTypeId

    -- Revoke the user's permanent desk if they have one in the specified organization
    update tblDesks
    set UpdatedDateUtc = @_now
       ,DeskType = {(int)DeskType.Flexi}
       ,PermanentOwnerUid = null
    output inserted.id -- DeskId
          ,inserted.Name
          ,inserted.FloorId
          ,inserted.DeskType
          ,inserted.FunctionType
          ,inserted.FunctionId
          ,inserted.PermanentOwnerUid
          ,inserted.XAxis
          ,inserted.YAxis
          ,deleted.DeskType
          ,deleted.PermanentOwnerUid
          into @_deskData
    from tblDesks
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    and tblFloors.Deleted = 0
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblDesks.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblDesks.PermanentOwnerUid = @uid

    -- If user had a permanent desk in the specified organization, more steps to be taken
    if @@ROWCOUNT > 0
    begin
        -- Insert to desks log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_deskData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_deskData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDesks_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,PermanentOwnerUid
        ,XAxis
        ,YAxis
        ,Deleted
        ,OldName
        ,OldDeskType
        ,OldFunctionType
        ,OldFunctionId
        ,OldPermanentOwnerUid
        ,OldXAxis
        ,OldYAxis
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,d.id -- DeskId
              ,d.Name
              ,d.FloorId
              ,d.DeskType
              ,d.FunctionType
              ,d.FunctionId
              ,d.PermanentOwnerUid
              ,d.XAxis
              ,d.YAxis
              ,0 -- Deleted
              ,d.Name
              ,d.OldDeskType
              ,d.FunctionType
              ,d.FunctionId
              ,d.OldPermanentOwnerUid
              ,d.XAxis
              ,d.YAxis
              ,0 -- OldDeleted
              ,'Delete' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_deskData d
        left join logIds l
        on d.id = l.id

        -- Update the old row in tblDeskHistories with updated EndDateUtc
        update tblDeskHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
           ,EndDateLocal = tz.Last15MinuteIntervalLocal
        output inserted.id -- DeskHistoryId
              ,inserted.DeskId
              ,inserted.Name
              ,inserted.FloorId
              ,inserted.DeskType
              ,inserted.FunctionType
              ,inserted.FunctionId
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,deleted.EndDateUtc
              ,deleted.EndDateLocal
              into @_deskHistoryData
        from tblDeskHistories
        inner join @_deskData d
        on tblDeskHistories.DeskId = d.id
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblDeskHistories.EndDateUtc > @_last15MinuteIntervalUtc

        -- Insert to log for the old row in tblDeskHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_deskHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_deskHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDeskHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,DeskHistoryId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,OldEndDateUtc
        ,OldEndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- DeskHistoryId
              ,h.DeskId
              ,h.Name
              ,h.FloorId
              ,h.DeskType
              ,h.FunctionType
              ,h.FunctionId
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,h.OldEndDateUtc
              ,h.OldEndDateLocal
              ,'Update' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_deskHistoryData h
        left join logIds l
        on h.id = l.id

        -- Insert a new row into tblDeskHistories for the desk we just updated,
        -- using the last 15 minute interval for StartDateUtc and StartDateLocal
        ;with generatedIds as (
            select ids.id, combs.GeneratedId
            from
            (
                select ROW_NUMBER() over (order by GeneratedId) as RowNumber, GeneratedId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as GeneratedId
                    from @_deskData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_deskData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDeskHistories
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,OrganizationId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal)
        output inserted.id
              ,inserted.DeskId
              ,inserted.Name
              ,inserted.FloorId
              ,inserted.DeskType
              ,inserted.FunctionType
              ,inserted.FunctionId
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              into @_newDeskHistoryData
        select l.GeneratedId
              ,@_now -- InsertDateUtc
              ,@_now -- UpdatedDateUtc
              ,@organizationId
              ,d.id -- DeskId
              ,d.Name
              ,d.FloorId
              ,d.DeskType
              ,d.FunctionType
              ,d.FunctionId
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc
              ,tz.Last15MinuteIntervalLocal -- StartDateLocal
              ,tz.EndOfTheWorldLocal -- EndDateLocal
        from @_deskData d
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        left join generatedIds l
        on d.id = l.id

        -- Write to log for the desk history for the new row
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_nowPlus1) as binary(6)) as uniqueidentifier) as LogId
                    from @_newDeskHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_newDeskHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblDeskHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,DeskHistoryId
        ,DeskId
        ,Name
        ,FloorId
        ,DeskType
        ,FunctionType
        ,FunctionId
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- DeskHistoryId
              ,h.DeskId -- DeskId
              ,h.Name
              ,h.FloorId
              ,h.DeskType
              ,h.FunctionType
              ,h.FunctionId
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,'Insert' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_newDeskHistoryData h
        left join logIds l
        on h.id = l.id

        -- Cancel permanent desk availabilities, as they are no longer needed now that the
        -- desk has been changed to flexi.
        update tblPermanentDeskAvailabilities
        set UpdatedDateUtc = @_now
           ,CancelledDateUtc = @_now
           ,CancelledDateLocal = tz.NowLocal
           ,Cancelled = 1
        output inserted.id
              ,inserted.DeskId
              ,inserted.AvailabilityCreatorUid
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,inserted.CancelledDateUtc
              ,inserted.CancelledDateLocal
              into @_previousPermanentDesksAvailabilityData
        from tblPermanentDeskAvailabilities
        inner join @_deskData d
        on tblPermanentDeskAvailabilities.DeskId = d.id
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentDeskAvailabilities.Deleted = 0
        and tblPermanentDeskAvailabilities.Cancelled = 0
        and tblPermanentDeskAvailabilities.EndDateUtc > @_last15MinuteIntervalUtc -- Only update availabilities that are current or in the future

        -- Insert cancelled permanent desk availabilities for previous permanent desks into log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_previousPermanentDesksAvailabilityData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_previousPermanentDesksAvailabilityData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentDeskAvailabilities_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentDeskAvailabilityId
        ,DeskId
        ,AvailabilityCreatorUid
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,CancelledByUid
        ,CancelledDateUtc
        ,CancelledDateLocal
        ,Cancelled
        ,Deleted
        ,OldCancelled
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,d.id -- PermanentDeskAvailabilityId
              ,d.DeskId
              ,d.AvailabilityCreatorUid
              ,d.StartDateUtc
              ,d.EndDateUtc
              ,d.StartDateLocal
              ,d.EndDateLocal
              ,@adminUserUid -- CancelledByUid
              ,d.CancelledDateUtc
              ,d.CancelledDateLocal
              ,1 -- Cancelled
              ,0 -- Deleted
              ,0 -- OldCancelled
              ,0 -- OldDeleted
              ,'Update' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_previousPermanentDesksAvailabilityData d
        left join logIds l
        on d.id = l.id

        -- End the existing booking in tblPermanentDeskHistories (used for reporting/dashboard only)
        -- for this desk by setting the BookingEndUtc and BookingEndLocal to the last 15 minute interval
        update tblPermanentDeskHistories
        set UpdatedDateUtc = @_now
           ,BookingEndUtc = @_last15MinuteIntervalUtc
           ,BookingEndLocal = tz.Last15MinuteIntervalLocal
        output inserted.id
              ,inserted.DeskId
              ,inserted.PermanentOwnerUid
              ,inserted.BookingStartUtc
              ,inserted.BookingEndUtc
              ,inserted.BookingStartLocal
              ,inserted.BookingEndLocal
              ,deleted.BookingEndUtc
              ,deleted.BookingEndLocal
              into @_permanentDeskHistoryData
        from tblPermanentDeskHistories
        inner join @_deskData d
        on tblPermanentDeskHistories.DeskId = d.id
        inner join tblFloors
        on d.FloorId = tblFloors.id
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentDeskHistories.BookingEndUtc > @_last15MinuteIntervalUtc

        -- Insert to log for updates made to tblPermanentDeskHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_permanentDeskHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_permanentDeskHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentDeskHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentDeskHistoryId
        ,DeskId
        ,PermanentOwnerUid
        ,BookingStartUtc
        ,BookingEndUtc
        ,BookingStartLocal
        ,BookingEndLocal
        ,OldBookingEndUtc
        ,OldBookingEndLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,d.id -- PermanentDeskHistoryId
              ,d.DeskId
              ,d.PermanentOwnerUid
              ,d.BookingStartUtc
              ,d.BookingEndUtc
              ,d.BookingStartLocal
              ,d.BookingEndLocal
              ,d.OldBookingEndUtc
              ,d.OldBookingEndLocal
              ,'Update' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_permanentDeskHistoryData d
        inner join logIds l
        on d.id = l.id
    end

    -- Truncate current desk bookings where the user is the booking owner in the specified organization
    update tblDeskBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = tz.Last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = tz.NowLocal -- Log when the booking was truncated
       ,OriginalBookingEndUtc = BookingEndUtc
       ,OriginalBookingEndLocal = BookingEndLocal
       ,Truncated = 1
    output inserted.id
          ,inserted.DeskId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousDeskBookingsData
    from tblDeskBookings
    inner join tblDesks
    on tblDeskBookings.DeskId = tblDesks.id
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblDeskBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblDeskBookings.BookingStartUtc and @_now < tblDeskBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblDeskBookings.Cancelled = 0
    and tblDeskBookings.Truncated = 0
    and tblDeskBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId

    -- Cancel future desk bookings where the user is the booking owner in the specified organization
    update tblDeskBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = tz.NowLocal
       ,Cancelled = 1
    output inserted.id
          ,inserted.DeskId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousDeskBookingsData
    from tblDeskBookings
    inner join tblDesks
    on tblDeskBookings.DeskId = tblDesks.id
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblDeskBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblDeskBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblDeskBookings.Cancelled = 0
    and tblDeskBookings.Truncated = 0
    and tblDeskBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in the specified organization
    ;with logIds as (
        select ids.id, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_previousDeskBookingsData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by id) as RowNumber, id
            from @_previousDeskBookingsData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblDeskBookings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,DeskBookingId
    ,DeskId
    ,BookingCreatorUid
    ,BookingOwnerUid
    ,BookingStartUtc
    ,BookingEndUtc
    ,BookingStartLocal
    ,BookingEndLocal
    ,BookingCancelledByUid
    ,CancelledDateUtc
    ,CancelledDateLocal
    ,Cancelled
    ,Truncated
    ,Deleted
    ,OldBookingEndUtc
    ,OldBookingEndLocal
    ,OldCancelled
    ,OldTruncated
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,d.id -- DeskBookingId
          ,d.DeskId
          ,d.BookingCreatorUid
          ,d.BookingOwnerUid
          ,d.BookingStartUtc
          ,d.BookingEndUtc
          ,d.BookingStartLocal
          ,d.BookingEndLocal
          ,d.BookingCancelledByUid
          ,d.CancelledDateUtc
          ,d.CancelledDateLocal
          ,d.Cancelled
          ,d.Truncated
          ,0 -- Deleted
          ,d.OldBookingEndUtc
          ,d.OldBookingEndLocal
          ,0 -- OldCancelled
          ,0 -- OldTruncated
          ,0 -- OldDeleted
          ,'Update' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousDeskBookingsData d
    left join logIds l
    on d.id = l.id

    -- Truncate current local meeting room bookings where the user is the booking owner in the specified organization
    update tblMeetingRoomBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = tz.Last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = tz.NowLocal -- Log when the booking was truncated
       ,OriginalBookingEndUtc = BookingEndUtc
       ,OriginalBookingEndLocal = BookingEndLocal
       ,Truncated = 1
    output inserted.id
          ,inserted.MeetingRoomId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousLocalMeetingRoomBookingsData
    from tblMeetingRoomBookings
    inner join tblMeetingRooms
    on tblMeetingRoomBookings.MeetingRoomId = tblMeetingRooms.id
    inner join tblFloors
    on tblMeetingRooms.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblMeetingRoomBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblMeetingRoomBookings.BookingStartUtc and @_now < tblMeetingRoomBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblMeetingRoomBookings.Cancelled = 0
    and tblMeetingRoomBookings.Truncated = 0
    and tblMeetingRoomBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId

    -- Cancel future local meeting room bookings where the user is the booking owner in the specified organization
    update tblMeetingRoomBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = tz.NowLocal
       ,Cancelled = 1
    output inserted.id
          ,inserted.MeetingRoomId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousLocalMeetingRoomBookingsData
    from tblMeetingRoomBookings
    inner join tblMeetingRooms
    on tblMeetingRoomBookings.MeetingRoomId = tblMeetingRooms.id
    inner join tblFloors
    on tblMeetingRooms.FloorId = tblFloors.id
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblMeetingRoomBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblMeetingRoomBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblMeetingRoomBookings.Cancelled = 0
    and tblMeetingRoomBookings.Truncated = 0
    and tblMeetingRoomBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in the specified organization
    ;with logIds as (
        select ids.id, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_previousLocalMeetingRoomBookingsData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by id) as RowNumber, id
            from @_previousLocalMeetingRoomBookingsData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblMeetingRoomBookings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,MeetingRoomBookingId
    ,MeetingRoomId
    ,BookingCreatorUid
    ,BookingOwnerUid
    ,BookingStartUtc
    ,BookingEndUtc
    ,BookingStartLocal
    ,BookingEndLocal
    ,BookingCancelledByUid
    ,CancelledDateUtc
    ,CancelledDateLocal
    ,Cancelled
    ,Truncated
    ,Deleted
    ,OldBookingEndUtc
    ,OldBookingEndLocal
    ,OldCancelled
    ,OldTruncated
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,d.id -- MeetingRoomBookingId
          ,d.MeetingRoomId
          ,d.BookingCreatorUid
          ,d.BookingOwnerUid
          ,d.BookingStartUtc
          ,d.BookingEndUtc
          ,d.BookingStartLocal
          ,d.BookingEndLocal
          ,d.BookingCancelledByUid
          ,d.CancelledDateUtc
          ,d.CancelledDateLocal
          ,d.Cancelled
          ,d.Truncated
          ,0 -- Deleted
          ,d.OldBookingEndUtc
          ,d.OldBookingEndLocal
          ,0 -- OldCancelled
          ,0 -- OldTruncated
          ,0 -- OldDeleted
          ,'Update' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousLocalMeetingRoomBookingsData d
    left join logIds l
    on d.id = l.id

    -- Revoke the user's permanent asset slots if they have any in the specified organization
    update tblAssetSlots
    set UpdatedDateUtc = @_now
       ,AssetSlotType = {(int)AssetSlotType.Flexi}
       ,PermanentOwnerUid = null
    output inserted.id
          ,inserted.Name
          ,inserted.AssetSectionId
          ,inserted.AssetSlotType
          ,inserted.PermanentOwnerUid
          ,inserted.XAxis
          ,inserted.YAxis
          into @_assetSlotData
    from tblAssetSlots
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    and tblAssetSections.Deleted = 0
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetSlots.Deleted = 0
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
    and tblAssetSlots.PermanentOwnerUid = @uid
    and tblBuildings.OrganizationId = @organizationId

    -- If user had a permanent asset slot in the specified organization, more steps to be taken
    if @@ROWCOUNT > 0
    begin
        -- Insert to asset slots log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_assetSlotData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_assetSlotData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlots_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,PermanentOwnerUid
        ,XAxis
        ,YAxis
        ,Deleted
        ,OldName
        ,OldXAxis
        ,OldYAxis
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,d.id -- AssetSlotId
              ,d.Name
              ,d.AssetSectionId
              ,d.AssetSlotType
              ,d.PermanentOwnerUid
              ,d.XAxis
              ,d.YAxis
              ,0 -- Deleted
              ,d.Name
              ,d.XAxis
              ,d.YAxis
              ,0 -- OldDeleted
              ,'Delete' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_assetSlotData d
        left join logIds l
        on d.id = l.id

        -- Update the old row in tblAssetSlotHistories with updated EndDateUtc
        update tblAssetSlotHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
           ,EndDateLocal = tz.Last15MinuteIntervalLocal
        output inserted.id -- AssetSlotHistoryId
              ,inserted.AssetSlotId
              ,inserted.Name
              ,inserted.AssetSectionId
              ,inserted.AssetSlotType
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,deleted.EndDateUtc
              ,deleted.EndDateLocal
              into @_assetSlotHistoryData
        from tblAssetSlotHistories
        inner join @_assetSlotData d
        on tblAssetSlotHistories.AssetSlotId = d.id
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblAssetSlotHistories.EndDateUtc > @_last15MinuteIntervalUtc

        -- Insert to log for the old row in tblAssetSlotHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_assetSlotHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_assetSlotHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlotHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,AssetSlotHistoryId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,OldEndDateUtc
        ,OldEndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- AssetSlotHistoryId
              ,h.AssetSlotId
              ,h.Name
              ,h.AssetSectionId
              ,h.AssetSlotType
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,h.OldEndDateUtc
              ,h.OldEndDateLocal
              ,'Update' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_assetSlotHistoryData h
        left join logIds l
        on h.id = l.id

        -- Insert a new row into tblAssetSlotHistories for the asset slot we just updated,
        -- using the last 15 minute interval for StartDateUtc and StartDateLocal
        ;with generatedIds as (
            select ids.id, combs.GeneratedId
            from
            (
                select ROW_NUMBER() over (order by GeneratedId) as RowNumber, GeneratedId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as GeneratedId
                    from @_assetSlotData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_assetSlotData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlotHistories
        (id
        ,InsertDateUtc
        ,UpdatedDateUtc
        ,OrganizationId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal)
        output inserted.id
              ,inserted.AssetSlotId
              ,inserted.Name
              ,inserted.AssetSectionId
              ,inserted.AssetSlotType
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              into @_newAssetSlotHistoryData
        select l.GeneratedId
              ,@_now -- InsertDateUtc
              ,@_now -- UpdatedDateUtc
              ,@organizationId
              ,d.id -- AssetSlotId
              ,d.Name
              ,d.AssetSectionId
              ,d.AssetSlotType
              ,@_last15MinuteIntervalUtc -- StartDateUtc
              ,@endOfTheWorldUtc -- EndDateUtc
              ,tz.Last15MinuteIntervalLocal -- StartDateLocal
              ,tz.EndOfTheWorldLocal -- EndDateLocal
        from @_assetSlotData d
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        left join generatedIds l
        on d.id = l.id

        -- Write to log for the asset slot history for the new row
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_nowPlus1) as binary(6)) as uniqueidentifier) as LogId
                    from @_newAssetSlotHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_newAssetSlotHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblAssetSlotHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,AssetSlotHistoryId
        ,AssetSlotId
        ,Name
        ,AssetSectionId
        ,AssetSlotType
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,h.id -- AssetSlotHistoryId
              ,h.AssetSlotId
              ,h.Name
              ,h.AssetSectionId
              ,h.AssetSlotType
              ,h.StartDateUtc
              ,h.EndDateUtc
              ,h.StartDateLocal
              ,h.EndDateLocal
              ,'Insert' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_newAssetSlotHistoryData h
        left join logIds l
        on h.id = l.id

        -- Cancel permanent asset slot availabilities, as they are no longer needed now that the
        -- asset slots have been changed to flexi.
        update tblPermanentAssetSlotAvailabilities
        set UpdatedDateUtc = @_now
           ,CancelledDateUtc = @_now
           ,CancelledDateLocal = tz.NowLocal
           ,Cancelled = 1
        output inserted.id
              ,inserted.AssetSlotId
              ,inserted.AvailabilityCreatorUid
              ,inserted.StartDateUtc
              ,inserted.EndDateUtc
              ,inserted.StartDateLocal
              ,inserted.EndDateLocal
              ,inserted.CancelledDateUtc
              ,inserted.CancelledDateLocal
              into @_previousPermanentAssetSlotsAvailabilityData
        from tblPermanentAssetSlotAvailabilities
        inner join @_assetSlotData d
        on tblPermanentAssetSlotAvailabilities.AssetSlotId = d.id
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentAssetSlotAvailabilities.Deleted = 0
        and tblPermanentAssetSlotAvailabilities.Cancelled = 0
        and tblPermanentAssetSlotAvailabilities.EndDateUtc > @_last15MinuteIntervalUtc -- Only update availabilities that are current or in the future

        -- Insert cancelled permanent asset slot availabilities into log
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_previousPermanentAssetSlotsAvailabilityData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_previousPermanentAssetSlotsAvailabilityData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentAssetSlotAvailabilities_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentAssetSlotAvailabilityId
        ,AssetSlotId
        ,AvailabilityCreatorUid
        ,StartDateUtc
        ,EndDateUtc
        ,StartDateLocal
        ,EndDateLocal
        ,CancelledByUid
        ,CancelledDateUtc
        ,CancelledDateLocal
        ,Cancelled
        ,Deleted
        ,OldCancelled
        ,OldDeleted
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,d.id -- PermanentAssetSlotAvailabilityId
              ,d.AssetSlotId
              ,d.AvailabilityCreatorUid
              ,d.StartDateUtc
              ,d.EndDateUtc
              ,d.StartDateLocal
              ,d.EndDateLocal
              ,@adminUserUid -- CancelledByUid
              ,d.CancelledDateUtc
              ,d.CancelledDateLocal
              ,1 -- Cancelled
              ,0 -- Deleted
              ,0 -- OldCancelled
              ,0 -- OldDeleted
              ,'Update' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_previousPermanentAssetSlotsAvailabilityData d
        left join logIds l
        on d.id = l.id

        -- End the existing booking in tblPermanentAssetSlotHistories (used for reporting/dashboard only)
        -- for this asset slot by setting the BookingEndUtc and BookingEndLocal to the last 15 minute interval
        update tblPermanentAssetSlotHistories
        set UpdatedDateUtc = @_now
           ,BookingEndUtc = @_last15MinuteIntervalUtc
           ,BookingEndLocal = tz.Last15MinuteIntervalLocal
        output inserted.id
              ,inserted.AssetSlotId
              ,inserted.PermanentOwnerUid
              ,inserted.BookingStartUtc
              ,inserted.BookingEndUtc
              ,inserted.BookingStartLocal
              ,inserted.BookingEndLocal
              ,deleted.BookingEndUtc
              ,deleted.BookingEndLocal
              into @_permanentAssetSlotHistoryData
        from tblPermanentAssetSlotHistories
        inner join @_assetSlotData d
        on tblPermanentAssetSlotHistories.AssetSlotId = d.id
        inner join tblAssetSections
        on d.AssetSectionId = tblAssetSections.id
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        inner join @_buildingTimezones tz
        on tblBuildings.id = tz.BuildingId
        where tblPermanentAssetSlotHistories.BookingEndUtc > @_last15MinuteIntervalUtc

        -- Insert to log for updates made to tblPermanentAssetSlotHistories
        ;with logIds as (
            select ids.id, combs.LogId
            from
            (
                select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
                from
                (
                    select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                    from @_permanentAssetSlotHistoryData
                ) combsInner
            ) combs
            inner join
            (
                select ROW_NUMBER() over (order by id) as RowNumber, id
                from @_permanentAssetSlotHistoryData
            ) ids
            on ids.RowNumber = combs.RowNumber
        )
        insert into tblPermanentAssetSlotHistories_Log
        (id
        ,InsertDateUtc
        ,UpdatedByUid
        ,UpdatedByDisplayName
        ,UpdatedByIpAddress
        ,LogDescription
        ,OrganizationId
        ,PermanentAssetSlotHistoryId
        ,AssetSlotId
        ,PermanentOwnerUid
        ,BookingStartUtc
        ,BookingEndUtc
        ,BookingStartLocal
        ,BookingEndLocal
        ,OldBookingEndUtc
        ,OldBookingEndLocal
        ,LogAction
        ,CascadeFrom
        ,CascadeLogId)
        select l.LogId
              ,@_now
              ,@adminUserUid
              ,@adminUserDisplayName
              ,@remoteIpAddress
              ,@logDescription
              ,@organizationId
              ,d.id -- PermanentAssetSlotHistoryId
              ,d.AssetSlotId
              ,d.PermanentOwnerUid
              ,d.BookingStartUtc
              ,d.BookingEndUtc
              ,d.BookingStartLocal
              ,d.BookingEndLocal
              ,d.OldBookingEndUtc
              ,d.OldBookingEndLocal
              ,'Update' -- LogAction
              ,'tblUserOrganizationJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_permanentAssetSlotHistoryData d
        inner join logIds l
        on d.id = l.id
    end

    -- Truncate current asset slot bookings where the user is the booking owner in the specified organization
    update tblAssetSlotBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = tz.Last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = tz.NowLocal -- Log when the booking was truncated
       ,OriginalBookingEndUtc = BookingEndUtc
       ,OriginalBookingEndLocal = BookingEndLocal
       ,Truncated = 1
    output inserted.id
          ,inserted.AssetSlotId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousAssetSlotBookingsData
    from tblAssetSlotBookings
    inner join tblAssetSlots
    on tblAssetSlotBookings.AssetSlotId = tblAssetSlots.id
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblAssetSlotBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblAssetSlotBookings.BookingStartUtc and @_now < tblAssetSlotBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblAssetSlotBookings.Cancelled = 0
    and tblAssetSlotBookings.Truncated = 0
    and tblAssetSlotBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId

    -- Cancel future asset slot bookings where the user is the booking owner in the specified organization
    update tblAssetSlotBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = tz.NowLocal
       ,Cancelled = 1
    output inserted.id
          ,inserted.AssetSlotId
          ,inserted.BookingCreatorUid
          ,inserted.BookingOwnerUid
          ,inserted.BookingStartUtc
          ,inserted.BookingEndUtc
          ,inserted.BookingStartLocal
          ,inserted.BookingEndLocal
          ,inserted.BookingCancelledByUid
          ,inserted.CancelledDateUtc
          ,inserted.CancelledDateLocal
          ,inserted.Cancelled
          ,inserted.Truncated
          ,deleted.BookingEndUtc
          ,deleted.BookingEndLocal
          into @_previousAssetSlotBookingsData
    from tblAssetSlotBookings
    inner join tblAssetSlots
    on tblAssetSlotBookings.AssetSlotId = tblAssetSlots.id
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    inner join @_buildingTimezones tz
    on tblBuildings.id = tz.BuildingId
    where tblAssetSlotBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblAssetSlotBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblAssetSlotBookings.Cancelled = 0
    and tblAssetSlotBookings.Truncated = 0
    and tblAssetSlotBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in the specified organization
    ;with logIds as (
        select ids.id, combs.LogId
        from
        (
            select ROW_NUMBER() over (order by LogId) as RowNumber, LogId
            from
            (
                select cast(cast(newid() AS binary(10)) + cast(datediff_big(millisecond, '1970-1-1', @_now) as binary(6)) as uniqueidentifier) as LogId
                from @_previousAssetSlotBookingsData
            ) combsInner
        ) combs
        inner join
        (
            select ROW_NUMBER() over (order by id) as RowNumber, id
            from @_previousAssetSlotBookingsData
        ) ids
        on ids.RowNumber = combs.RowNumber
    )
    insert into tblAssetSlotBookings_Log
    (id
    ,InsertDateUtc
    ,UpdatedByUid
    ,UpdatedByDisplayName
    ,UpdatedByIpAddress
    ,LogDescription
    ,OrganizationId
    ,AssetSlotBookingId
    ,AssetSlotId
    ,BookingCreatorUid
    ,BookingOwnerUid
    ,BookingStartUtc
    ,BookingEndUtc
    ,BookingStartLocal
    ,BookingEndLocal
    ,BookingCancelledByUid
    ,CancelledDateUtc
    ,CancelledDateLocal
    ,Cancelled
    ,Truncated
    ,Deleted
    ,OldBookingEndUtc
    ,OldBookingEndLocal
    ,OldCancelled
    ,OldTruncated
    ,OldDeleted
    ,LogAction
    ,CascadeFrom
    ,CascadeLogId)
    select l.LogId
          ,@_now
          ,@adminUserUid
          ,@adminUserDisplayName
          ,@remoteIpAddress
          ,@logDescription
          ,@organizationId
          ,d.id -- AssetSlotBookingId
          ,d.AssetSlotId
          ,d.BookingCreatorUid
          ,d.BookingOwnerUid
          ,d.BookingStartUtc
          ,d.BookingEndUtc
          ,d.BookingStartLocal
          ,d.BookingEndLocal
          ,d.BookingCancelledByUid
          ,d.CancelledDateUtc
          ,d.CancelledDateLocal
          ,d.Cancelled
          ,d.Truncated
          ,0 -- Deleted
          ,d.OldBookingEndUtc
          ,d.OldBookingEndLocal
          ,0 -- OldCancelled
          ,0 -- OldTruncated
          ,0 -- OldDeleted
          ,'Update' -- LogAction
          ,'tblUserOrganizationJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousAssetSlotBookingsData d
    left join logIds l
    on d.id = l.id
end
else
begin
    -- User either did not exist, did not belong to organization, or ConcurrencyKey was invalid
    set @_result = 2
end

select @_result

-- Select row to return with the API result
select Uid
      ,InsertDateUtc
      ,UpdatedDateUtc
      ,LastAccessDateUtc
      ,LastPasswordChangeDateUtc
      ,Email
      ,HasPassword
      ,TotpEnabled
      ,UserSystemRole
      ,DisplayName
      ,FirstName
      ,Surname
      ,Timezone
      ,AvatarUrl
      ,AvatarThumbnailUrl
      ,Disabled
      ,ConcurrencyKey
from tblUsers
where Deleted = 0
and Uid = @uid

if @@ROWCOUNT = 1
begin
    -- Also query user's organization access
    select tblUserOrganizationJoin.OrganizationId as id
          ,tblOrganizations.Name
          ,tblOrganizations.LogoImageUrl
          ,tblOrganizations.CheckInEnabled
          ,tblOrganizations.WorkplacePortalEnabled
          ,tblOrganizations.WorkplaceAccessRequestsEnabled
          ,tblOrganizations.WorkplaceInductionsEnabled
          ,tblUserOrganizationJoin.UserOrganizationRole
          ,tblUserOrganizationJoin.Note
          ,tblUserOrganizationJoin.Contractor
          ,tblUserOrganizationJoin.Visitor
          ,tblUserOrganizationJoin.UserOrganizationDisabled
          ,tblUserOrganizationJoin.InsertDateUtc as AccessGivenDateUtc
    from tblUserOrganizationJoin
    inner join tblOrganizations
    on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    and tblOrganizations.Disabled = 0
    where tblUserOrganizationJoin.Uid = @uid
    order by tblOrganizations.Name

    -- Also query user's last used building
    select Uid
          ,WebLastUsedOrganizationId
          ,WebLastUsedBuildingId
          ,MobileLastUsedOrganizationId
          ,MobileLastUsedBuildingId
    from tblUserLastUsedBuilding
    where Uid = @uid

    -- Also query user's building access
    select tblUserBuildingJoin.BuildingId as id
          ,tblBuildings.Name
          ,tblBuildings.OrganizationId
          ,tblBuildings.Timezone
          ,tblBuildings.CheckInEnabled
          ,0 as HasBookableMeetingRooms -- Queried separately
          ,0 as HasBookableAssetSlots -- Queried separately
          ,tblUserBuildingJoin.FunctionId
          ,tblFunctions.Name as FunctionName
          ,tblFunctions.HtmlColor as FunctionHtmlColor
          ,tblUserBuildingJoin.FirstAidOfficer
          ,tblUserBuildingJoin.FireWarden
          ,tblUserBuildingJoin.PeerSupportOfficer
          ,tblUserBuildingJoin.AllowBookingDeskForVisitor
          ,tblUserBuildingJoin.AllowBookingRestrictedRooms
          ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
          ,tblUserBuildingJoin.InsertDateUtc as AccessGivenDateUtc
    from tblUserBuildingJoin
    inner join tblBuildings
    on tblUserBuildingJoin.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblFunctions
    on tblUserBuildingJoin.FunctionId = tblFunctions.id
    and tblFunctions.Deleted = 0
    where tblUserBuildingJoin.Uid = @uid
    order by tblBuildings.Name

    -- Also query user's buildings with bookable desks
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblDesks
        inner join tblFloors
        on tblDesks.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblDesks.Deleted = 0
        and tblDesks.DeskType != {(int)DeskType.Offline}
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )

    -- Also query user's buildings with bookable meeting rooms
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblMeetingRooms
        inner join tblFloors
        on tblMeetingRooms.FloorId = tblFloors.id
        and tblFloors.Deleted = 0
        inner join tblBuildings
        on tblFloors.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblMeetingRooms.Deleted = 0
        and tblMeetingRooms.OfflineRoom = 0
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
        and
        (
            tblMeetingRooms.RestrictedRoom = 0
            or tblUserBuildingJoin.AllowBookingRestrictedRooms = 1
        )
    )

    -- Also query user's buildings with bookable asset slots
    select tblUserBuildingJoin.BuildingId
    from tblUserBuildingJoin
    where tblUserBuildingJoin.Uid = @uid
    and exists
    (
        select *
        from tblAssetSlots
        inner join tblAssetSections
        on tblAssetSlots.AssetSectionId = tblAssetSections.id
        and tblAssetSections.Deleted = 0
        inner join tblAssetTypes
        on tblAssetSections.AssetTypeId = tblAssetTypes.id
        and tblAssetTypes.Deleted = 0
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        where tblAssetSlots.Deleted = 0
        and tblAssetSlots.AssetSlotType != {(int)AssetSlotType.Offline}
        and tblBuildings.id = tblUserBuildingJoin.BuildingId
    )

    -- Also query the user's permanent seat
    select tblDesks.id as DeskId
          ,tblBuildings.id as BuildingId
    from tblDesks
    inner join tblFloors
    on tblDesks.FloorId = tblFloors.id
    and tblFloors.Deleted = 0
    inner join tblBuildings
    on tblFloors.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblDesks.Deleted = 0
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblDesks.PermanentOwnerUid = @uid

    -- Also query the user's asset types
    select tblAssetTypes.id
          ,tblAssetTypes.Name
          ,tblAssetTypes.BuildingId
          ,tblAssetTypes.LogoImageUrl
    from tblUserAssetTypeJoin
    inner join tblAssetTypes
    on tblUserAssetTypeJoin.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblUserAssetTypeJoin.Uid = @uid

    -- Also query the user's permanent assets
    select tblAssetSlots.id as AssetSlotId
          ,tblAssetSections.AssetTypeId
          ,tblBuildings.id as BuildingId
    from tblAssetSlots
    inner join tblAssetSections
    on tblAssetSlots.AssetSectionId = tblAssetSections.id
    and tblAssetSections.Deleted = 0
    inner join tblAssetTypes
    on tblAssetSections.AssetTypeId = tblAssetTypes.id
    and tblAssetTypes.Deleted = 0
    inner join tblBuildings
    on tblAssetTypes.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    where tblAssetSlots.Deleted = 0
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
    and tblAssetSlots.PermanentOwnerUid = @uid

    -- Also query the user's admin functions if the user is an Admin,
    -- or all functions if they are a Super Admin.
    select tblFunctions.id
          ,tblFunctions.Name
          ,tblFunctions.BuildingId
    from tblFunctions
    where tblFunctions.Deleted = 0
    and exists
    (
        select *
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblFunctions.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblUserOrganizationJoin
        on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
        and tblUserOrganizationJoin.Uid = @uid
        left join tblUserAdminFunctions
        on tblFunctions.id = tblUserAdminFunctions.FunctionId
        and tblUserAdminFunctions.Uid = @uid
        where tblFunctions.BuildingId = tblUserBuildingJoin.BuildingId
        and tblUserBuildingJoin.Uid = @uid
        and
        (
            tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
            or
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                and tblUserAdminFunctions.FunctionId is not null
            )
        )
    )

    -- Also query the user's admin asset types if the user is an Admin,
    -- or all asset types if they are a Super Admin.
    select tblAssetTypes.id
          ,tblAssetTypes.Name
          ,tblAssetTypes.BuildingId
          ,tblAssetTypes.LogoImageUrl
    from tblAssetTypes
    where tblAssetTypes.Deleted = 0
    and exists
    (
        select *
        from tblUserBuildingJoin
        inner join tblBuildings
        on tblAssetTypes.BuildingId = tblBuildings.id
        and tblBuildings.Deleted = 0
        inner join tblUserOrganizationJoin
        on tblBuildings.OrganizationId = tblUserOrganizationJoin.OrganizationId
        and tblUserOrganizationJoin.Uid = @uid
        left join tblUserAdminAssetTypes
        on tblAssetTypes.id = tblUserAdminAssetTypes.AssetTypeId
        and tblUserAdminAssetTypes.Uid = @uid
        where tblAssetTypes.BuildingId = tblUserBuildingJoin.BuildingId
        and tblUserBuildingJoin.Uid = @uid
        and
        (
            tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.SuperAdmin}
            or
            (
                tblUserOrganizationJoin.UserOrganizationRole = {(int)UserOrganizationRole.Admin}
                and tblUserAdminAssetTypes.AssetTypeId is not null
            )
        )
    )
end
";
                Guid logId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userOrganizationJoinHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);

                parameters.Add("@userOrganizationJoinHistoryUpdateLogId", userOrganizationJoinHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

                // If insert was successful, also get the data
                if (!gridReader.IsConsumed)
                {
                    if (!gridReader.IsConsumed && userData is not null)
                    {
                        // Read extended data
                        userData.ExtendedData.Organizations = (await gridReader.ReadAsync<UserData_UserOrganizations>()).AsList();
                        userData.ExtendedData.LastUsedBuilding = await gridReader.ReadFirstOrDefaultAsync<UserData_LastUsedBuilding>();

                        List<UserData_Building> buildings = (await gridReader.ReadAsync<UserData_Building>()).AsList();
                        List<Guid> buildingsWithBookableDesks = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableMeetingRooms = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<Guid> buildingsWithBookableAssetSlots = (await gridReader.ReadAsync<Guid>()).AsList();
                        List<UserData_PermanentSeat> permanentSeats = (await gridReader.ReadAsync<UserData_PermanentSeat>()).AsList();
                        List<UserData_AssetType> assetTypes = (await gridReader.ReadAsync<UserData_AssetType>()).AsList();
                        List<UserData_PermanentAsset> permanentAssets = (await gridReader.ReadAsync<UserData_PermanentAsset>()).AsList();
                        List<UserData_AdminFunction> adminFunctions = (await gridReader.ReadAsync<UserData_AdminFunction>()).AsList();
                        List<UserData_AdminAssetType> adminAssetTypes = (await gridReader.ReadAsync<UserData_AdminAssetType>()).AsList();

                        UsersRepository.FillExtendedDataOrganizations(userData, buildings, buildingsWithBookableDesks, buildingsWithBookableMeetingRooms, buildingsWithBookableAssetSlots, permanentSeats, assetTypes, permanentAssets, adminFunctions, adminAssetTypes);
                    }
                }

                UserManagementResult queryResult;

                switch (resultCode)
                {
                    case 1:
                        queryResult = UserManagementResult.Ok;
                        break;
                    case 2:
                        if (userData is null)
                        {
                            // User did not exist
                            queryResult = UserManagementResult.UserDidNotExist;
                        }
                        else if (!Toolbox.ByteArrayEqual(userData.ConcurrencyKey, request.ConcurrencyKey))
                        {
                            // User exists but concurrency key was invalid
                            queryResult = UserManagementResult.ConcurrencyKeyInvalid;
                        }
                        else
                        {
                            // User did not belong to the organization
                            queryResult = UserManagementResult.UserDidNotExistInOrganization;
                        }
                        break;
                    default:
                        queryResult = UserManagementResult.UnknownError;
                        break;
                }

                return (queryResult, userData);
            }
        }
    }
}
