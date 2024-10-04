using Dapper;
using Microsoft.Data.SqlClient;
using System.Data;
using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
//using VisitorTabletAPITemplate.Shared;
using ZiggyCreatures.Caching.Fusion;
using static Dapper.SqlMapper;

namespace VisitorTabletAPITemplate.ShaneAuth.Services
{
    public sealed class AuthCacheService
    {
        private readonly IFusionCache _cache;
        private readonly AppSettings _appSettings;
        private readonly IHttpClientFactory _httpClientFactory;

        public AuthCacheService(IFusionCache cache,
            AppSettings appSettings,
            IHttpClientFactory httpClientFactory)
        {
            _cache = cache;
            _appSettings = appSettings;
            _httpClientFactory = httpClientFactory;
        }

        /// <summary>
        /// <para>Retrieves a user's organization permission and caches it for future calls to this function. Returns null if the user does not have permission to access the organization.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public ValueTask<UserOrganizationPermission?> GetUserOrganizationPermissionAsync(Guid uid, Guid organizationId, CancellationToken cancellationToken = default)
        {
            return _cache.GetOrSetAsync<UserOrganizationPermission?>($"UserOrganizationPermission:{uid}:{organizationId}", async (ctx, ct) =>
            {
                // NOTE: "No permission" is also cached. If we want to change this in future,
                // expire the cache immediately with the following: ctx.Options.Duration = TimeSpan.FromSeconds(0)
                return await GetUserOrganizationPermissionFromDbAsync(uid, organizationId, ct);
            }, (FusionCacheEntryOptions?)null, cancellationToken);
        }

        /// <summary>
        /// <para>Retrieves a user's organization permission and caches it for future calls to this function.</para>
        /// <para>If the user does not have permission to access the organization, checks whether the user has UserSystemRole == Master and returns an empty <see cref="UserOrganizationPermission"/> instead, and this result is not cached.</para>
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public async Task<UserOrganizationPermission?> GetMasterOrUserOrganizationPermissionAsync(Guid uid, Guid organizationId, CancellationToken cancellationToken = default)
        {
            UserOrganizationPermission? userOrganizationPermission = await _cache.GetOrSetAsync<UserOrganizationPermission?>($"UserOrganizationPermission:{uid}:{organizationId}", async (ctx, ct) =>
            {
                // NOTE: "No permission" is also cached. If we want to change this in future,
                // expire the cache immediately with the following: ctx.Options.Duration = TimeSpan.FromSeconds(0)
                return await GetUserOrganizationPermissionFromDbAsync(uid, organizationId, ct);
            }, (FusionCacheEntryOptions?)null, cancellationToken);

            // If user has permission to access the organization, stop here.
            if (userOrganizationPermission is not null)
            {
                return userOrganizationPermission;
            }

            // Get user's system role and whether the organization has Disabled = 1
            (int? userSystemRole, bool? organizationDisabled) = await GetUserSystemRoleAndOrganizationDisabledFromDbAsync(uid, organizationId, cancellationToken);

            // If organization or user doesn't exist, or user is not a master, stop here.
            if (organizationDisabled is null || userSystemRole is null || userSystemRole != (int)UserSystemRole.Master)
            {
                return null;
            }

            // Return an empty response with the user's system role
            return new UserOrganizationPermission
            {
                Uid = uid,
                OrganizationId = organizationId,
                UserSystemRole = (UserSystemRole)userSystemRole.Value,
                OrganizationDisabled = organizationDisabled.Value,
                UserOrganizationRole = UserOrganizationRole.NoAccess,
                BuildingPermissions = new Dictionary<Guid, UserBuildingPermission>(),
            };
        }

        public async Task<UserBuildingPermission?> GetUserBuildingPermissionAsync(Guid uid, Guid organizationId, Guid buildingId, CancellationToken cancellationToken = default)
        {
            // Get organization permissions for user (includes building permissions)
            UserOrganizationPermission? userOrganizationPermission = await _cache.GetOrSetAsync<UserOrganizationPermission?>($"UserOrganizationPermission:{uid}:{organizationId}", async (ctx, ct) =>
            {
                // NOTE: "No permission" is also cached. If we want to change this in future,
                // expire the cache immediately with the following: ctx.Options.Duration = TimeSpan.FromSeconds(0)
                return await GetUserOrganizationPermissionFromDbAsync(uid, organizationId, ct);
            }, (FusionCacheEntryOptions?)null, cancellationToken);

            // If user does not have access to the organization, stop here.
            if (userOrganizationPermission is null)
            {
                return null;
            }

            // Return the building permission if the user has access to the specified building
            if (userOrganizationPermission.BuildingPermissions.TryGetValue(buildingId, out UserBuildingPermission? userBuildingPermission))
            {
                return userBuildingPermission;
            }

            return null;
        }

        public ValueTask<TimeZoneInfo?> GetBuildingTimeZoneInfoAsync(Guid buildingId, Guid organizationId, CancellationToken cancellationToken = default)
        {
            return _cache.GetOrSetAsync<TimeZoneInfo?>($"BuildingTimeZoneInfo:{buildingId}:{organizationId}", async (ctx, ct) =>
            {
                // Set cache expiry time to 6 hours
                ctx.Options.Duration = TimeSpan.FromHours(6);

                // NOTE: "No building" is also cached. If we want to change this in future,
                // expire the cache immediately with the following: ctx.Options.Duration = TimeSpan.FromSeconds(0)
                return await GetBuildingTimeZoneFromDbAsync(buildingId, organizationId, ct);
            }, (FusionCacheEntryOptions?)null, cancellationToken);
        }

        public Task InvalidateUserOrganizationPermissionCacheAsync(Guid uid, Guid organizationId)
        {
            string cacheKey = $"UserOrganizationPermission:{uid}:{organizationId}";

            /*
            // Publish InvalidateCacheEvent event so that the other IIS server in the server farm is notified.
            return new InvalidateCacheEvent
            {
                CacheKeys = new List<string> { cacheKey },
            }.PublishAsync(Mode.WaitForNone);
            */

            return HandleInvalidateCacheAsync(new List<string> { cacheKey });
        }

        public Task InvalidateUserOrganizationPermissionCacheAsync(Guid uid, List<Guid> organizationIds)
        {
            List<string> cacheKeys = new List<string>();

            foreach (Guid organizationId in organizationIds)
            {
                cacheKeys.Add($"UserOrganizationPermission:{uid}:{organizationId}");
            }

            /*
            // Publish InvalidateCacheEvent event so that the other IIS server in the server farm is notified.
            return new InvalidateCacheEvent
            {
                CacheKeys = cacheKeys,
            }.PublishAsync(Mode.WaitForNone);
            */

            return HandleInvalidateCacheAsync(cacheKeys);
        }

        public Task InvalidateBuildingTimeZoneInfoCacheAsync(Guid buildingId, Guid organizationId)
        {
            string cacheKey = $"BuildingTimeZoneInfo:{buildingId}:{organizationId}";

            /*
            // Publish InvalidateCacheEvent event so that the other IIS server in the server farm is notified.
            return new InvalidateCacheEvent
            {
                CacheKeys = new List<string> { cacheKey },
            }.PublishAsync(Mode.WaitForNone);
            */

            return HandleInvalidateCacheAsync(new List<string> { cacheKey });
        }

        public Task InvalidateBuildingTimeZoneInfoCacheAsync(List<Guid> buildingIds, Guid organizationId)
        {
            List<string> cacheKeys = new List<string>();

            foreach (Guid buildingId in buildingIds)
            {
                cacheKeys.Add($"BuildingTimeZoneInfo:{buildingId}:{organizationId}");
            }

            /*
            // Publish InvalidateCacheEvent event so that the other IIS server in the server farm is notified.
            return new InvalidateCacheEvent
            {
                CacheKeys = cacheKeys,
            }.PublishAsync(Mode.WaitForNone);
            */

            return HandleInvalidateCacheAsync(cacheKeys);
        }

        /// <summary>
        /// Returns the user's system role, as well as whether the organization has Disabled = 1 in tblOrganizations
        /// </summary>
        /// <param name="uid"></param>
        /// <param name="organizationId"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task<(int?, bool?)> GetUserSystemRoleAndOrganizationDisabledFromDbAsync(Guid uid, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select UserSystemRole
from tblUsers
where Uid = @uid
and Deleted = 0
and Disabled = 0

if @@ROWCOUNT = 1
begin
    select Disabled
    from tblOrganizations
    where id = @organizationid
    and Deleted = 0
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using GridReader reader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                int? userSystemRole = await reader.ReadFirstOrDefaultAsync<int?>();
                bool? organizationDisabled = null;

                if (userSystemRole is not null)
                {
                    organizationDisabled = await reader.ReadFirstOrDefaultAsync<bool?>();
                }

                return (userSystemRole, organizationDisabled);
            }
        }

        private async Task<UserOrganizationPermission?> GetUserOrganizationPermissionFromDbAsync(Guid uid, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
-- Get Organization Permission
select tblUsers.Uid
      ,tblOrganizations.id as OrganizationId
      ,tblOrganizations.Disabled as OrganizationDisabled
      ,tblUserOrganizationJoin.UserOrganizationRole
      ,tblUsers.UserSystemRole
from tblUsers
inner join tblUserOrganizationJoin
on tblUsers.Uid = tblUserOrganizationJoin.Uid
inner join tblOrganizations
on tblUserOrganizationJoin.OrganizationId = tblOrganizations.id
and tblOrganizations.Deleted = 0
where tblUsers.Uid = @uid
and tblUsers.Deleted = 0
and tblUsers.UserSystemRole > 0
and tblOrganizations.id = @organizationId
and tblUserOrganizationJoin.UserOrganizationDisabled = 0

if @@ROWCOUNT = 1
begin
    -- Get permission for buildings in the organization
    select tblUserBuildingJoin.Uid
          ,tblUserBuildingJoin.BuildingId
          ,tblBuildings.Timezone as BuildingTimezone
          ,tblBuildings.OrganizationId
          ,tblOrganizations.Disabled as OrganizationDisabled
          ,tblUserBuildingJoin.FunctionId
          ,tblUserBuildingJoin.AllowBookingDeskForVisitor
          ,tblUserBuildingJoin.AllowBookingRestrictedRooms
          ,tblUserBuildingJoin.AllowBookingAnyoneAnywhere
    from tblUsers
    inner join tblUserBuildingJoin
    on tblUsers.Uid = tblUserBuildingJoin.Uid
    inner join tblBuildings
    on tblUserBuildingJoin.BuildingId = tblBuildings.id
    and tblBuildings.Deleted = 0
    inner join tblOrganizations
    on tblBuildings.OrganizationId = tblOrganizations.id
    and tblOrganizations.Deleted = 0
    where tblUsers.Uid = @uid
    and tblUsers.Deleted = 0
    and tblUsers.UserSystemRole > 0
    and tblOrganizations.id = @organizationId
end
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@uid", uid, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                using GridReader reader = await sqlConnection.QueryMultipleAsync(commandDefinition);

                UserOrganizationPermission? userOrganizationPermission = await reader.ReadFirstOrDefaultAsync<UserOrganizationPermission>();

                if (userOrganizationPermission is not null && !reader.IsConsumed)
                {
                    List<UserBuildingPermission> userBuildingPermissions = (await reader.ReadAsync<UserBuildingPermission>()).AsList();

                    foreach (UserBuildingPermission userBuildingPermission in userBuildingPermissions)
                    {
                        // Add reference to parent UserOrganizationPermission to each UserBuildingPermission
                        userBuildingPermission.UserOrganizationPermission = userOrganizationPermission;

                        userOrganizationPermission.BuildingPermissions.Add(userBuildingPermission.BuildingId, userBuildingPermission);
                    }
                }

                return userOrganizationPermission;
            }
        }

        private async Task<TimeZoneInfo?> GetBuildingTimeZoneFromDbAsync(Guid buildingId, Guid organizationId, CancellationToken cancellationToken = default)
        {
            using (SqlConnection sqlConnection = new SqlConnection(_appSettings.ConnectionStrings.VisitorTablet))
            {
                string sql = @"
select Timezone
from tblBuildings
where Deleted = 0
and id = @buildingId
and OrganizationId = @organizationId
";
                DynamicParameters parameters = new DynamicParameters();
                parameters.Add("@buildingId", buildingId, DbType.Guid, ParameterDirection.Input);
                parameters.Add("@organizationId", organizationId, DbType.Guid, ParameterDirection.Input);

                CommandDefinition commandDefinition = new CommandDefinition(sql, parameters, cancellationToken: cancellationToken);

                string? buildingTimezone = await sqlConnection.QueryFirstOrDefaultAsync<string?>(commandDefinition);

                if (string.IsNullOrEmpty(buildingTimezone))
                {
                    return null;
                }

                if (TimeZoneInfo.TryFindSystemTimeZoneById(buildingTimezone, out TimeZoneInfo? timeZoneInfo))
                {
                    return timeZoneInfo;
                }

                return null;
            }
        }

        private Task HandleInvalidateCacheAsync(List<string> cacheKeys)
        {
            if (cacheKeys is null || cacheKeys.Count == 0)
            {
                return Task.CompletedTask;
            }

            // Remove the items from the cache on the current server
            foreach (string cacheKey in cacheKeys)
            {
                _cache.Remove(cacheKey);
            }

            return Task.CompletedTask;
        }
    }
}
