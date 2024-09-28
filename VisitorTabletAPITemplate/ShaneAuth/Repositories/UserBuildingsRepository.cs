using Dapper;
using Microsoft.Data.SqlClient;
using VisitorTabletAPITemplate.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.AddUserToBuilding;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.RemoveUserFromBuilding;
using VisitorTabletAPITemplate.ShaneAuth.Features.Master.Users.UpdateUserBuilding;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.ShaneAuth.Services;
using VisitorTabletAPITemplate.Utilities;
using System.Data;
using System.Text;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.ShaneAuth.Repositories
{
    public sealed class UserBuildingsRepository
    {
        private readonly AppSettings _appSettings;
        private readonly AuthCacheService _authCacheService;

        public UserBuildingsRepository(AppSettings appSettings,
            AuthCacheService authCacheService)
        {
            _appSettings = appSettings;
            _authCacheService = authCacheService;
        }

        /// <summary>
        /// Adds a user to a building if they aren't already assigned to it. Intended to be used in Master User Settings panel.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="userOrganizationRole"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public async Task<(UserManagementResult, UserData?)> MasterAddUserToBuildingAsync(MasterAddUserToBuildingRequest request, UserOrganizationRole userOrganizationRole, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            // Note: request.Uid, request.OrganizationId, request.BuildingId and request.FunctionId are already validated in the endpoint before calling this function

            string logDescription = "Add User To Building (Master)";

            DynamicParameters parameters = new DynamicParameters();

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                StringBuilder sql = new StringBuilder();

                sql.AppendLine(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
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
                switch (userOrganizationRole)
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
                        throw new Exception($"Unknown UserOrganizationRole: {userOrganizationRole}");
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

    -- Get a lock on adding a building to the user.
    exec @_lockResult = sp_getapplock
         @Resource = @lockResourceName,
         @LockMode = 'Exclusive',
         @LockOwner = 'Transaction',
         @LockTimeout = 0

    if @_lockResult < 0
    begin
        set @_result = 999
        rollback
    end
    else
    begin
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
              ,@_now
              ,@functionId
              ,@firstAidOfficer
              ,@fireWarden
              ,@peerSupportOfficer
              ,@allowBookingDeskForVisitor
              ,@allowBookingRestrictedRooms
              ,@allowBookingAnyoneAnywhere
        where not exists
        (
            select *
            from tblUserBuildingJoin
            where Uid = @uid
            and BuildingId = @buildingId
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

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
            ,LogAction)
            select @logId
                  ,@_now
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
                  ,'tblUserBuildingJoin' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Delete existing rows just in case to prevent primary key violation.
            -- Should not be any existing rows unless database was touched manually,
            -- because if this user did not have access to the organization, then
            -- they should also not have access to the building.
            delete tblUserAssetTypeJoin
            where Uid = @uid
            and BuildingId = @buildingId

            delete tblUserAdminFunctions
            where Uid = @uid
            and BuildingId = @buildingId

            delete tblUserAdminAssetTypes
            where Uid = @uid
            and BuildingId = @buildingId

            insert into tblUserAssetTypeJoin
            (Uid
            ,BuildingId
            ,AssetTypeId
            ,InsertDateUtc)
            select @uid
                  ,@buildingId
                  ,d.AssetTypeId
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

                // Generate ids to be used when inserting to tblUserBuildingJoinHistories and tblUserBuildingJoinHistories_Log
                Guid userBuildingJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                parameters.Add("@lockResourceName", $"tblUserBuildingJoin_{request.Uid}_{request.BuildingId}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                // Building details
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
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
                parameters.Add("@userBuildingJoinHistoryId", userBuildingJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryLogId", userBuildingJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);

                // Logs
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
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
                        queryResult = UserManagementResult.UserAlreadyExistsInBuilding;
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
        /// Updates a user's building access if they are assigned to it. Intended to be used in Master User Settings panel.
        /// </summary>
        /// <param name="request"></param>
        /// <param name="userOrganizationRole"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserManagementResult, UserData?)> MasterUpdateUserBuildingAsync(MasterUpdateUserBuildingRequest request, UserOrganizationRole userOrganizationRole, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Update User Building (Master)";

            DynamicParameters parameters = new DynamicParameters();

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                StringBuilder sql = new StringBuilder();

                sql.AppendLine(@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_lockResult int
declare @_userAssetTypesValid bit = 1
declare @_userAdminFunctionsValid bit = 1
declare @_userAdminAssetTypesValid bit = 1

declare @_data table
(
    FunctionId uniqueidentifier
   ,FirstAidOfficer bit
   ,FireWarden bit
   ,PeerSupportOfficer bit
   ,AllowBookingDeskForVisitor bit
   ,AllowBookingRestrictedRooms bit
   ,AllowBookingAnyoneAnywhere bit
   ,OldFunctionId uniqueidentifier
   ,OldFirstAidOfficer bit
   ,OldFireWarden bit
   ,OldPeerSupportOfficer bit
   ,OldAllowBookingDeskForVisitor bit
   ,OldAllowBookingRestrictedRooms bit
   ,OldAllowBookingAnyoneAnywhere bit
)

declare @_historyData table
(
    id uniqueidentifier
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

declare @_userAssetTypesData table
(
    AssetTypeId uniqueidentifier
)

declare @_userAssetTypesLogData table
(
    OrderKey int
   ,AssetTypeId uniqueidentifier
   ,LogAction varchar(6)
)

declare @_userAdminFunctionsData table
(
    FunctionId uniqueidentifier
)

declare @_userAdminFunctionsLogData table
(
    OrderKey int
   ,FunctionId uniqueidentifier
   ,LogAction varchar(6)
)

declare @_userAdminAssetTypesData table
(
    AssetTypeId uniqueidentifier
)

declare @_userAdminAssetTypesLogData table
(
    OrderKey int
   ,AssetTypeId uniqueidentifier
   ,LogAction varchar(6)
)
");
                // Insert UserAssetTypes into query
                if (request.UserAssetTypes is not null && request.UserAssetTypes.Count > 0)
                {
                    sql.AppendLine(@"
insert into @_userAssetTypesData
(AssetTypeId)
values");
                    for (int i = 0; i < request.UserAssetTypes.Count; ++i)
                    {
                        if (i > 0)
                        {
                            sql.Append(',');
                        }

                        sql.AppendLine($"(@userAssetTypeId{i})");
                        parameters.Add($"@userAssetTypeId{i}", request.UserAssetTypes[i], DbType.Guid, ParameterDirection.Input);
                    }
                }

                // For Admin, populate UserAdminFunctions and UserAdminAssetTypes into query 
                switch (userOrganizationRole)
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
(FunctionId)
values");
                            for (int i = 0; i < request.UserAdminFunctions.Count; ++i)
                            {
                                if (i > 0)
                                {
                                    sql.Append(',');
                                }

                                sql.AppendLine($"(@userAdminFunctionId{i})");
                                parameters.Add($"@userAdminFunctionId{i}", request.UserAdminFunctions[i], DbType.Guid, ParameterDirection.Input);
                            }
                        }

                        // Insert UserAdminAssetTypes into query
                        if (request.UserAdminAssetTypes is not null && request.UserAdminAssetTypes.Count > 0)
                        {
                            sql.AppendLine(@"
insert into @_userAdminAssetTypesData
(AssetTypeId)
values");
                            for (int i = 0; i < request.UserAdminAssetTypes.Count; ++i)
                            {
                                if (i > 0)
                                {
                                    sql.Append(',');
                                }

                                sql.AppendLine($"(@userAdminAssetTypeId{i})");
                                parameters.Add($"@userAdminAssetTypeId{i}", request.UserAdminAssetTypes[i], DbType.Guid, ParameterDirection.Input);
                            }
                        }
                        break;
                    default:
                        throw new Exception($"Unknown UserOrganizationRole: {userOrganizationRole}");
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

    -- Get a lock on adding a building to the user.
    exec @_lockResult = sp_getapplock
         @Resource = @lockResourceName,
         @LockMode = 'Exclusive',
         @LockOwner = 'Transaction',
         @LockTimeout = 0

    if @_lockResult < 0
    begin
        set @_result = 999
        rollback
    end
    else
    begin
        update tblUserBuildingJoin
        set FunctionId = @functionId
           ,FirstAidOfficer = @firstAidOfficer
           ,FireWarden = @fireWarden
           ,PeerSupportOfficer = @peerSupportOfficer
           ,AllowBookingDeskForVisitor = @allowBookingDeskForVisitor
           ,AllowBookingRestrictedRooms = @allowBookingRestrictedRooms
           ,AllowBookingAnyoneAnywhere = @allowBookingAnyoneAnywhere
        output inserted.FunctionId
              ,inserted.FirstAidOfficer
              ,inserted.FireWarden
              ,inserted.PeerSupportOfficer
              ,inserted.AllowBookingDeskForVisitor
              ,inserted.AllowBookingRestrictedRooms
              ,inserted.AllowBookingAnyoneAnywhere
              ,deleted.FunctionId
              ,deleted.FirstAidOfficer
              ,deleted.FireWarden
              ,deleted.PeerSupportOfficer
              ,deleted.AllowBookingDeskForVisitor
              ,deleted.AllowBookingRestrictedRooms
              ,deleted.AllowBookingAnyoneAnywhere
              into @_data
        where Uid = @uid
        and BuildingId = @buildingId
        and exists
        (
            select *
            from tblUserOrganizationJoin
            where tblUserOrganizationJoin.Uid = @uid
            and tblUserOrganizationJoin.OrganizationId = @organizationId
        )

        if @@ROWCOUNT = 1
        begin
            set @_result = 1

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
            ,LogAction)
            select @logId
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,d.FunctionId
                  ,d.FirstAidOfficer
                  ,d.FireWarden
                  ,d.PeerSupportOfficer
                  ,d.AllowBookingDeskForVisitor
                  ,d.AllowBookingRestrictedRooms
                  ,d.AllowBookingAnyoneAnywhere
                  ,d.OldFunctionId
                  ,d.OldFirstAidOfficer
                  ,d.OldFireWarden
                  ,d.OldPeerSupportOfficer
                  ,d.OldAllowBookingDeskForVisitor
                  ,d.OldAllowBookingRestrictedRooms
                  ,d.OldAllowBookingAnyoneAnywhere
                  ,'Update' -- LogAction
            from @_data d

            -- Update the old row in tblUserBuildingJoinHistories with updated EndDateUtc
            update tblUserBuildingJoinHistories
            set UpdatedDateUtc = @_now
               ,EndDateUtc = @_last15MinuteIntervalUtc
            output inserted.id -- UserBuildingJoinHistoryId
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
                  into @_historyData
            where Uid = @uid
            and BuildingId = @buildingId
            and EndDateUtc > @_last15MinuteIntervalUtc

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
            select @userBuildingJoinHistoryUpdateLogId -- id
                  ,@_now
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,h.id -- UserBuildingJoinHistoryId
                  ,@uid
                  ,@buildingId
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
                  ,'tblUserBuildingJoin' -- CascadeFrom
                  ,@logId -- CascadeLogId
            from @_historyData h
            
            -- Insert a new row into tblUserBuildingJoinHistories for the user/building we just updated,
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

            -- Write to log for the user building join history for the new row
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
                  ,'tblUserBuildingJoin' -- CascadeFrom
                  ,@logId -- CascadeLogId

            -- Delete removed user asset types and insert into log table variable
            delete from tblUserAssetTypeJoin
            output 1 -- OrderKey
                  ,deleted.AssetTypeId
                  ,'Delete' -- LogAction
                  into @_userAssetTypesLogData
            where Uid = @uid
            and BuildingId = @buildingId
            and not exists
            (
                select *
                from @_userAssetTypesData d
                where tblUserAssetTypeJoin.Uid = @uid
                and d.AssetTypeId = tblUserAssetTypeJoin.AssetTypeId
            )

            -- Insert new user asset types
            insert into tblUserAssetTypeJoin
            (Uid, BuildingId, AssetTypeId, InsertDateUtc)
            output 2 -- OrderKey
                  ,inserted.AssetTypeId
                  ,'Insert' -- LogAction
                   into @_userAssetTypesLogData
            select @uid, @buildingId, AssetTypeId, @_now
            from @_userAssetTypesData d
            where not exists
            (
                select *
                from tblUserAssetTypeJoin
                where tblUserAssetTypeJoin.Uid = @uid
                and d.AssetTypeId = tblUserAssetTypeJoin.AssetTypeId
            )

            -- Insert to user asset types log
            ;with logIds as (
                select ids.OrderKey, ids.AssetTypeId, combs.LogId
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
                    select ROW_NUMBER() over (order by OrderKey, AssetTypeId) as RowNumber, OrderKey, AssetTypeId
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
                  ,@_now -- InsertDateUtc
                  ,@adminUserUid
                  ,@adminUserDisplayName
                  ,@remoteIpAddress
                  ,@logDescription
                  ,@organizationId
                  ,@uid
                  ,@buildingId
                  ,d.AssetTypeId
                  ,d.LogAction
                  ,'tblUserBuildingJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserBuildingJoin
            from @_userAssetTypesLogData d
            left join logIds l
            on d.OrderKey = l.OrderKey
            and d.AssetTypeId = l.AssetTypeId

            -- Delete removed user admin functions and insert into log table variable
            delete from tblUserAdminFunctions
            output 1 -- OrderKey
                  ,deleted.FunctionId
                  ,'Delete' -- LogAction
                  into @_userAdminFunctionsLogData
            where Uid = @uid
            and BuildingId = @buildingId
            and not exists
            (
                select *
                from @_userAdminFunctionsData d
                where tblUserAdminFunctions.Uid = @uid
                and tblUserAdminFunctions.BuildingId = @buildingId
                and d.FunctionId = tblUserAdminFunctions.FunctionId
            )

            -- Insert new user admin functions
            insert into tblUserAdminFunctions
            (Uid, BuildingId, FunctionId, InsertDateUtc)
            output 2 -- OrderKey
                  ,inserted.FunctionId
                  ,'Insert' -- LogAction
                  into @_userAdminFunctionsLogData
            select @uid, @buildingId, FunctionId, @_now
            from @_userAdminFunctionsData d
            where not exists
            (
                select *
                from tblUserAdminFunctions
                where tblUserAdminFunctions.Uid = @uid
                and d.FunctionId = tblUserAdminFunctions.FunctionId
            )

            -- Insert to user admin functions log
            ;with logIds as (
                select ids.OrderKey, ids.FunctionId, combs.LogId
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
                    select ROW_NUMBER() over (order by OrderKey, FunctionId) as RowNumber, OrderKey, FunctionId
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
                  ,@buildingId
                  ,d.FunctionId
                  ,d.LogAction
                  ,'tblUserBuildingJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserBuildingJoin
            from @_userAdminFunctionsLogData d
            left join logIds l
            on d.OrderKey = l.OrderKey
            and d.FunctionId = l.FunctionId

            -- Delete removed user admin asset types and insert into log table variable
            delete from tblUserAdminAssetTypes
            output 1 -- OrderKey
                  ,deleted.AssetTypeId
                  ,'Delete' -- LogAction
                  into @_userAdminAssetTypesLogData
            where Uid = @uid
            and BuildingId = @buildingId
            and not exists
            (
                select *
                from @_userAdminAssetTypesData d
                where tblUserAdminAssetTypes.Uid = @uid
                and tblUserAdminAssetTypes.BuildingId = @buildingId
                and d.AssetTypeId = tblUserAdminAssetTypes.AssetTypeId
            )

            -- Insert new user admin asset types
            insert into tblUserAdminAssetTypes
            (Uid, BuildingId, AssetTypeId, InsertDateUtc)
            output 2 -- OrderKey
                  ,inserted.AssetTypeId
                  ,'Insert' -- LogAction
                  into @_userAdminAssetTypesLogData
            select @uid, @buildingId, AssetTypeId, @_now
            from @_userAdminAssetTypesData d
            where not exists
            (
                select *
                from tblUserAdminAssetTypes
                where tblUserAdminAssetTypes.Uid = @uid
                and d.AssetTypeId = tblUserAdminAssetTypes.AssetTypeId
            )

            -- Insert to user admin asset types log
            ;with logIds as (
                select ids.OrderKey, ids.AssetTypeId, combs.LogId
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
                    select ROW_NUMBER() over (order by OrderKey, AssetTypeId) as RowNumber, OrderKey, AssetTypeId
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
                  ,@buildingId
                  ,d.AssetTypeId
                  ,d.LogAction
                  ,'tblUserBuildingJoin' -- CascadeFrom
                  ,@logId -- log ID for tblUserBuildingJoin
            from @_userAdminAssetTypesLogData d
            left join logIds l
            on d.OrderKey = l.OrderKey
            and d.AssetTypeId = l.AssetTypeId
        end
        else
        begin
            -- Record was not updated
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

                // Generate ids to be used when updating old tblUserBuildingJoinHistories, as well as inserting to tblUserBuildingJoinHistories and tblUserBuildingJoinHistories_Log
                Guid userBuildingJoinHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryId = RT.Comb.EnsureOrderedProvider.Sql.Create();
                Guid userBuildingJoinHistoryLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                parameters.Add("@lockResourceName", $"tblUserBuildingJoin_{request.Uid}_{request.BuildingId}", DbType.String, ParameterDirection.Input, 255);
                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);

                // Building details
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
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
                parameters.Add("@userBuildingJoinHistoryUpdateLogId", userBuildingJoinHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryId", userBuildingJoinHistoryId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@userBuildingJoinHistoryLogId", userBuildingJoinHistoryLogId, DbType.Guid, ParameterDirection.Input);

                // Logs
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
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
                        queryResult = UserManagementResult.UserDidNotExistInBuilding;
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
        /// <para>Removes the specified user from the specified building. Intended to be used in Master User Settings panel.</para>
        /// <para>Returns: <see cref="UserManagementResult.Ok"/>, <see cref="UserManagementResult.UserDidNotExist"/>, <see cref="UserManagementResult.UserDidNotExistInBuilding"/>,
        /// <see cref="UserManagementResult.ConcurrencyKeyInvalid"/>.</para>
        /// </summary>
        /// <param name="request"></param>
        /// <param name="adminUserUid"></param>
        /// <param name="adminUserDisplayName"></param>
        /// <param name="remoteIpAddress"></param>
        /// <returns></returns>
        public async Task<(UserManagementResult, UserData?)> MasterRemoveUserFromBuildingAsync(MasterRemoveUserFromBuildingRequest request, Guid? adminUserUid, string? adminUserDisplayName, string? remoteIpAddress)
        {
            string logDescription = "Remove User From Building (Master)";

            TimeZoneInfo? buildingTimeZoneInfo = await _authCacheService.GetBuildingTimeZoneInfoAsync(request.BuildingId!.Value, request.OrganizationId!.Value);

            if (buildingTimeZoneInfo is null)
            {
                return (UserManagementResult.UserDidNotExistInBuilding, null);
            }

            DateTime utcNow = DateTime.UtcNow;

            // Get current utc offset in minutes so we can calculate local time in SQL
            int utcOffsetMinutes = (int)buildingTimeZoneInfo.GetUtcOffset(utcNow).TotalMinutes;
            DateTime endOfTheWorldLocal = TimeZoneInfo.ConvertTimeFromUtc(Globals.EndOfTheWorldUtc, buildingTimeZoneInfo);

            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = $@"
declare @_result int = 0
declare @_now datetime2(3) = sysutcdatetime()
declare @_nowPlus1 datetime2(3) = dateadd(millisecond, 1, sysutcdatetime())
declare @_nowLocal datetime2(3) = dateadd(minute, @utcOffsetMinutes, @_now)
declare @_last15MinuteIntervalUtc datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_now) / 15 * 15, '2000-01-01')
declare @_last15MinuteIntervalLocal datetime2(3) = dateadd(minute, datediff(minute, '2000-01-01', @_nowLocal) / 15 * 15, '2000-01-01')

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
inner join tblUsers
on tblUserBuildingJoin.Uid = tblUsers.Uid
and tblUsers.Deleted = 0
where tblUserBuildingJoin.Uid = @uid
and tblBuildings.OrganizationId = @organizationId
and tblBuildings.id = @buildingId
and tblUsers.ConcurrencyKey = @concurrencyKey

if @@ROWCOUNT = 1
begin
    set @_result = 1

    -- Insert to log
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
    ,LogAction)
    select @logId
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
    from @_userBuildingJoinData d

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
    where Uid = @uid
    and BuildingId = @buildingId
    and EndDateUtc > @_last15MinuteIntervalUtc

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
          ,'tblUserBuildingJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userBuildingJoinHistoryData h
    left join logIds l
    on h.BuildingId = l.BuildingId

    -- Delete user asset types for the user in the specified building
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
    and tblBuildings.id = @buildingId

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
          ,'tblUserBuildingJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAssetTypesLogData d
    left join logIds l
    on d.AssetTypeId = l.AssetTypeId

    -- Delete user admin functions for the user in the specified building
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
    and tblBuildings.id = @buildingId

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
          ,'tblUserBuildingJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAdminFunctionsLogData d
    left join logIds l
    on d.FunctionId = l.FunctionId

    -- Delete user admin asset types for the user in the specified building
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
    and tblBuildings.id = @buildingId

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
          ,'tblUserBuildingJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_userAdminAssetTypesLogData d
    left join logIds l
    on d.AssetTypeId = l.AssetTypeId

    -- Revoke the user's permanent desk if they have one in the specified building
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
    and tblDesks.DeskType = {(int)DeskType.Permanent}
    and tblDesks.PermanentOwnerUid = @uid
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- If user had a permanent desk in the specified building, more steps to be taken
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_deskData d
        left join logIds l
        on d.id = l.id

        -- Update the old row in tblDeskHistories with updated EndDateUtc
        update tblDeskHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
           ,EndDateLocal = @_last15MinuteIntervalLocal
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
              ,'tblUserBuildingJoin' -- CascadeFrom
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
              ,@_last15MinuteIntervalLocal -- StartDateLocal
              ,@endOfTheWorldLocal -- EndDateLocal
        from @_deskData d
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_newDeskHistoryData h
        left join logIds l
        on h.id = l.id

        -- Cancel permanent desk availabilities, as they are no longer needed now that the
        -- desk has been changed to flexi.
        update tblPermanentDeskAvailabilities
        set UpdatedDateUtc = @_now
           ,CancelledDateUtc = @_now
           ,CancelledDateLocal = @_nowLocal
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_previousPermanentDesksAvailabilityData d
        left join logIds l
        on d.id = l.id

        -- End the existing booking in tblPermanentDeskHistories (used for reporting/dashboard only)
        -- for this desk by setting the BookingEndUtc and BookingEndLocal to the last 15 minute interval
        update tblPermanentDeskHistories
        set UpdatedDateUtc = @_now
           ,BookingEndUtc = @_last15MinuteIntervalUtc
           ,BookingEndLocal = @_last15MinuteIntervalLocal
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_permanentDeskHistoryData d
        inner join logIds l
        on d.id = l.id
    end

    -- Truncate current desk bookings where the user is the booking owner in the specified building
    update tblDeskBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = @_last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = @_nowLocal -- Log when the booking was truncated
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
    where tblDeskBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblDeskBookings.BookingStartUtc and @_now < tblDeskBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblDeskBookings.Cancelled = 0
    and tblDeskBookings.Truncated = 0
    and tblDeskBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- Cancel future desk bookings where the user is the booking owner in the specified building
    update tblDeskBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = @_nowLocal
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
    where tblDeskBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblDeskBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblDeskBookings.Cancelled = 0
    and tblDeskBookings.Truncated = 0
    and tblDeskBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in the specified building
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
          ,'tblUserBuildingJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousDeskBookingsData d
    left join logIds l
    on d.id = l.id

    -- Truncate current local meeting room bookings where the user is the booking owner in the specified building
    update tblMeetingRoomBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = @_last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = @_nowLocal -- Log when the booking was truncated
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
    where tblMeetingRoomBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblMeetingRoomBookings.BookingStartUtc and @_now < tblMeetingRoomBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblMeetingRoomBookings.Cancelled = 0
    and tblMeetingRoomBookings.Truncated = 0
    and tblMeetingRoomBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- Cancel future local meeting room bookings where the user is the booking owner in the specified building
    update tblMeetingRoomBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = @_nowLocal
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
    where tblMeetingRoomBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblMeetingRoomBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblMeetingRoomBookings.Cancelled = 0
    and tblMeetingRoomBookings.Truncated = 0
    and tblMeetingRoomBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in the specified building
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
          ,'tblUserBuildingJoin' -- CascadeFrom
          ,@logId -- CascadeLogId
    from @_previousLocalMeetingRoomBookingsData d
    left join logIds l
    on d.id = l.id

    -- Revoke the user's permanent asset slots if they have any in the specified building
    update tblAssetSlots
    set UpdatedDateUtc = @_now
       ,AssetSlotType = {(int)AssetSlotType.Flexi}
       ,PermanentOwnerUid = null
    output inserted.id -- AssetSlotId
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
    and tblBuildings.OrganizationId = @organizationId
    and tblAssetSlots.AssetSlotType = {(int)AssetSlotType.Permanent}
    and tblAssetSlots.PermanentOwnerUid = @uid
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- If user had a permanent asset slot in the specified building, more steps to be taken
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_assetSlotData d
        left join logIds l
        on d.id = l.id

        -- Update the old row in tblAssetSlotHistories with updated EndDateUtc
        update tblAssetSlotHistories
        set UpdatedDateUtc = @_now
           ,EndDateUtc = @_last15MinuteIntervalUtc
           ,EndDateLocal = @_last15MinuteIntervalLocal
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
              ,'tblUserBuildingJoin' -- CascadeFrom
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
              ,@_last15MinuteIntervalLocal -- StartDateLocal
              ,@endOfTheWorldLocal -- EndDateLocal
        from @_assetSlotData d
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_newAssetSlotHistoryData h
        left join logIds l
        on h.id = l.id

        -- Cancel permanent asset slot availabilities, as they are no longer needed now that the
        -- asset slots have been changed to flexi.
        update tblPermanentAssetSlotAvailabilities
        set UpdatedDateUtc = @_now
           ,CancelledDateUtc = @_now
           ,CancelledDateLocal = @_nowLocal
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_previousPermanentAssetSlotsAvailabilityData d
        left join logIds l
        on d.id = l.id

        -- End the existing booking in tblPermanentAssetSlotHistories (used for reporting/dashboard only)
        -- for this asset slot by setting the BookingEndUtc and BookingEndLocal to the last 15 minute interval
        update tblPermanentAssetSlotHistories
        set UpdatedDateUtc = @_now
           ,BookingEndUtc = @_last15MinuteIntervalUtc
           ,BookingEndLocal = @_last15MinuteIntervalLocal
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
              ,'tblUserBuildingJoin' -- CascadeFrom
              ,@logId -- CascadeLogId
        from @_permanentAssetSlotHistoryData d
        inner join logIds l
        on d.id = l.id
    end

    -- Truncate current asset slot bookings where the user is the booking owner in the specified building
    update tblAssetSlotBookings
    set UpdatedDateUtc = @_now
       ,BookingEndUtc = @_last15MinuteIntervalUtc
       ,BookingEndLocal = @_last15MinuteIntervalLocal
       ,BookingCancelledByUid = @adminUserUid -- Log who truncated the booking
       ,CancelledDateUtc = @_now -- Log when the booking was truncated
       ,CancelledDateLocal = @_nowLocal -- Log when the booking was truncated
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
    where tblAssetSlotBookings.BookingOwnerUid = @uid
    and (@_last15MinuteIntervalUtc > tblAssetSlotBookings.BookingStartUtc and @_now < tblAssetSlotBookings.BookingEndUtc) -- Current, i.e. started before last 15 minute interval but not finished
    and tblAssetSlotBookings.Cancelled = 0
    and tblAssetSlotBookings.Truncated = 0
    and tblAssetSlotBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- Cancel future asset slot bookings where the user is the booking owner in the specified building
    update tblAssetSlotBookings
    set UpdatedDateUtc = @_now
       ,BookingCancelledByUid = @adminUserUid
       ,CancelledDateUtc = @_now
       ,CancelledDateLocal = @_nowLocal
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
    where tblAssetSlotBookings.BookingOwnerUid = @uid
    and @_last15MinuteIntervalUtc <= tblAssetSlotBookings.BookingStartUtc -- Not yet started, or started within current 15 minute interval
    and tblAssetSlotBookings.Cancelled = 0
    and tblAssetSlotBookings.Truncated = 0
    and tblAssetSlotBookings.Deleted = 0
    and tblBuildings.OrganizationId = @organizationId
    and tblBuildings.id = @buildingId

    -- Write to log for both truncated and cancelled bookings where the user is the booking owner in the specified building
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
          ,'tblUserBuildingJoin' -- CascadeFrom
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
                Guid userBuildingJoinHistoryUpdateLogId = RT.Comb.EnsureOrderedProvider.Sql.Create();

                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@utcOffsetMinutes", utcOffsetMinutes, DbType.Int32, ParameterDirection.Input);
                parameters.Add("@uid", request.Uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", request.OrganizationId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@buildingId", request.BuildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserUid", adminUserUid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@adminUserDisplayName", adminUserDisplayName, DbType.String, ParameterDirection.Input, 151);
                parameters.Add("@remoteIpAddress", remoteIpAddress, DbType.AnsiString, ParameterDirection.Input, 39);
                parameters.Add("@concurrencyKey", request.ConcurrencyKey, DbType.Binary, ParameterDirection.Input, 4);

                parameters.Add("@endOfTheWorldUtc", Globals.EndOfTheWorldUtc, DbType.DateTime2, ParameterDirection.Input, 3);
                parameters.Add("@endOfTheWorldLocal", endOfTheWorldLocal, DbType.DateTime2, ParameterDirection.Input, 3);

                parameters.Add("@userBuildingJoinHistoryUpdateLogId", userBuildingJoinHistoryUpdateLogId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logid", logId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@logDescription", logDescription, DbType.AnsiString, ParameterDirection.Input, 100);

                using GridReader gridReader = await sqlConnection.QueryMultipleAsync(sql, parameters);

                int resultCode = await gridReader.ReadFirstOrDefaultAsync<int>();
                UserData? userData = await gridReader.ReadFirstOrDefaultAsync<UserData>();

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
                            // User did not belong to the building
                            queryResult = UserManagementResult.UserDidNotExistInBuilding;
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
