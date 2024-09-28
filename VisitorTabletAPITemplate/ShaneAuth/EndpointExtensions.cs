using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.Repositories;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth
{
    public static partial class EndpointExtensions
    {
        /// <summary>
        /// Returns true if the user with the given <paramref name="userId"/> has access to the organization with the given <paramref name="organizationId"/> and is a Super Admin,
        /// ignoring their building permission, OR if the user is not a Super Admin but has access to the building.
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <param name="endpoint"></param>
        /// <param name="organizationId"></param>
        /// <param name="buildingId"></param>
        /// <param name="userId"></param>
        /// <param name="authCacheService"></param>
        /// <param name="buildingsRepository"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="allowDisabledOrganization"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        public static async Task<bool> ValidateUserBuildingOrSuperAdminAsync<TRequest>(this Endpoint<TRequest> endpoint, Guid? organizationId, Guid? buildingId, Guid userId,
            AuthCacheService authCacheService, BuildingsRepository buildingsRepository, CancellationToken cancellationToken = default, bool allowDisabledOrganization = false)
            where TRequest : notnull
        {
            // Validate OrganizationId
            if (!organizationId.HasValue)
            {
                endpoint.AddError("Organization Id is required.", "error.organizationIdIsRequired");
            }

            // Validate BuildingId
            if (!buildingId.HasValue)
            {
                endpoint.AddError("Building Id is required.", "error.buildingIdIsRequired");
            }

            // Stop here if any errors
            if (endpoint.ValidationFailed)
            {
                return false;
            }

            // Check user's Organization Permission
            UserOrganizationPermission? userOrganizationPermission = await authCacheService.GetUserOrganizationPermissionAsync(userId, organizationId!.Value, cancellationToken);

            // If null then any of these could be true:
            // - user doesn't exist
            // - organization doesn't exist
            // - user doesn't have permission to organization
            // - organization deleted
            if (userOrganizationPermission is null)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // Check if User has permission to the Organization, but Organization is disabled.
            // If the org is disabled and we want to consider that as not having access, then return false.
            // This is useful when we want to know whether the user would have permission if the organization
            // was to be re-enabled later.
            if (userOrganizationPermission.OrganizationDisabled && !allowDisabledOrganization)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // If user is a super admin, make sure the building exists, we don't care whether
            // the user has access to it or not.
            if (userOrganizationPermission.UserOrganizationRole == UserOrganizationRole.SuperAdmin)
            {
                if (!await buildingsRepository.IsBuildingExistsAsync(buildingId!.Value, organizationId!.Value, cancellationToken))
                {
                    AddPermissionError(endpoint);
                    return false;
                }

                return true;
            }

            // Stop on unhandled user organization roles
            if (userOrganizationPermission.UserOrganizationRole != UserOrganizationRole.User
                && userOrganizationPermission.UserOrganizationRole != UserOrganizationRole.Admin)
            {
                throw new Exception($"Unknown UserOrganizationRole: {userOrganizationPermission.UserOrganizationRole}");
            }

            // As the user is not a Super Admin, then the user must be either a User or an Admin - proceed to check Building Permission.
            UserBuildingPermission? userBuildingPermission = await authCacheService.GetUserBuildingPermissionAsync(userId, organizationId!.Value, buildingId!.Value, cancellationToken);

            // If null then any of these could be true:
            // - user doesn't exist
            // - building doesn't exist/deleted
            // - user doesn't have permission to building
            // - organization doesn't exist/deleted
            // - user doesn't have permission to organization
            if (userBuildingPermission is null)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // Check if User has permission to the building, but Organization is disabled.
            // If the org is disabled and we want to consider that as not having access, then return false.
            // This is useful when we want to know whether the user would have permission if the organization
            // was to be re-enabled later.
            if (userBuildingPermission.OrganizationDisabled && !allowDisabledOrganization)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // If User has permission to the building, and either the org isn't disabled,
            // or it is but we don't mind, then return true.
            return true;
        }

        private static void AddPermissionError<TRequest>(this Endpoint<TRequest> endpoint)
            where TRequest : notnull
        {
            endpoint.HttpContext.Items.Add("FatalError", true);
            endpoint.AddError("You do not have permission to perform this action.", "error.doNotHavePermission");
        }
    }
}
