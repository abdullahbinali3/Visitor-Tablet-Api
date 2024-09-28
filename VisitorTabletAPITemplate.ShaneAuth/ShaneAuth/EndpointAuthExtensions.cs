using VisitorTabletAPITemplate.Models;
using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Services;

namespace VisitorTabletAPITemplate.ShaneAuth
{
    public static partial class EndpointAuthExtensions
    {
        /// <summary>
        /// Returns true if the user with the given <paramref name="userId"/> has access to the organization with the given <paramref name="organizationId"/>.
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <param name="endpoint"></param>
        /// <param name="organizationId"></param>
        /// <param name="userId"></param>
        /// <param name="minimumRequiredRole"></param>
        /// <param name="authCacheService"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="allowDisabledOrganization"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<bool> ValidateUserOrganizationRoleAsync<TRequest>(this Endpoint<TRequest> endpoint, Guid? organizationId, Guid userId,
            UserOrganizationRole minimumRequiredRole, AuthCacheService authCacheService, CancellationToken cancellationToken = default, bool allowDisabledOrganization = false)
            where TRequest : notnull
        {
            // Validate OrganizationId
            if (!organizationId.HasValue)
            {
                endpoint.AddError("Organization Id is required.", "error.organizationIdIsRequired");
                return false;
            }

            UserOrganizationPermission? userOrganizationPermission = await authCacheService.GetUserOrganizationPermissionAsync(userId, organizationId!.Value, cancellationToken);

            // If null then any of these could be true:
            // - user doesn't exist
            // - organization doesn't exist / has been deleted
            // - user isn't assigned to organization
            // - user is assigned to organization, but has UserOrganizationDisabled = 1
            // - user system role = NoAccess
            if (userOrganizationPermission is null)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // If we got to this point, the User has permission to the Organization. Check if Organization is disabled.
            // If the org is disabled and we want to consider that as not having access, then return false.
            // This is useful when we want to know whether the user would have permission if the organization
            // was to be re-enabled later.
            if (userOrganizationPermission.OrganizationDisabled && !allowDisabledOrganization)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // User has permission to the organization, and either the org isn't disabled,
            // or it is but we don't mind.
            // Now check their role meets the minimum requirement.
            UserOrganizationRole userOrganizationRole = userOrganizationPermission.UserOrganizationRole;

            bool hasPermission = false;

            switch (minimumRequiredRole)
            {
                case UserOrganizationRole.Tablet:
                    if (userOrganizationRole == UserOrganizationRole.Tablet
                        || userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                case UserOrganizationRole.User:
                    if (userOrganizationRole == UserOrganizationRole.User
                        || userOrganizationRole == UserOrganizationRole.Admin
                        || userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                case UserOrganizationRole.Admin:
                    if (userOrganizationRole == UserOrganizationRole.Admin
                        || userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                case UserOrganizationRole.SuperAdmin:
                    if (userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown UserOrganizationRole: {userOrganizationRole}", nameof(minimumRequiredRole));
            }

            if (!hasPermission)
            {
                AddPermissionError(endpoint);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Returns true if the user with the given <paramref name="userId"/> has access to the organization with the given <paramref name="organizationId"/>.
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <param name="endpoint"></param>
        /// <param name="organizationId"></param>
        /// <param name="userId"></param>
        /// <param name="minimumRequiredRole"></param>
        /// <param name="authCacheService"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="allowDisabledOrganization"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<bool> ValidateMasterOrUserOrganizationRoleAsync<TRequest>(this Endpoint<TRequest> endpoint, Guid? organizationId, Guid userId,
            UserOrganizationRole minimumRequiredRole, AuthCacheService authCacheService, CancellationToken cancellationToken = default, bool allowDisabledOrganization = false)
            where TRequest : notnull
        {
            // Validate OrganizationId
            if (!organizationId.HasValue)
            {
                endpoint.AddError("Organization Id is required.", "error.organizationIdIsRequired");
                return false;
            }

            UserOrganizationPermission? userOrganizationPermission = await authCacheService.GetMasterOrUserOrganizationPermissionAsync(userId, organizationId!.Value, cancellationToken);

            // If null then any of these could be true:
            // - user doesn't exist
            // - organization doesn't exist / has been deleted
            // - user system role is not a master and isn't assigned to organization
            // - user is assigned to organization, but has UserOrganizationDisabled = 1
            // - user system role = NoAccess
            if (userOrganizationPermission is null)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // If user is a master, then the user has permission, even if the organization is disabled.
            if (userOrganizationPermission.UserSystemRole == UserSystemRole.Master)
            {
                return true;
            }

            // If we got to this point, the user is not a master, and the User has permission to the Organization. Check if Organization is disabled.
            // If the org is disabled and we want to consider that as not having access, then return false.
            // This is useful when we want to know whether the user would have permission if the organization
            // was to be re-enabled later.
            if (userOrganizationPermission.OrganizationDisabled && !allowDisabledOrganization)
            {
                AddPermissionError(endpoint);
                return false;
            }

            // User is not a master, but has permission to the organization, and either the org isn't disabled,
            // or it is but we don't mind.
            // Now check their role meets the minimum requirement.
            UserOrganizationRole userOrganizationRole = userOrganizationPermission.UserOrganizationRole;

            bool hasPermission = false;

            switch (minimumRequiredRole)
            {
                case UserOrganizationRole.Tablet:
                    if (userOrganizationRole == UserOrganizationRole.Tablet
                        || userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                case UserOrganizationRole.User:
                    if (userOrganizationRole == UserOrganizationRole.User
                        || userOrganizationRole == UserOrganizationRole.Admin
                        || userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                case UserOrganizationRole.Admin:
                    if (userOrganizationRole == UserOrganizationRole.Admin
                        || userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                case UserOrganizationRole.SuperAdmin:
                    if (userOrganizationRole == UserOrganizationRole.SuperAdmin)
                    {
                        hasPermission = true;
                    }
                    break;
                default:
                    throw new ArgumentException($"Unknown UserOrganizationRole: {userOrganizationRole}", nameof(minimumRequiredRole));
            }

            if (!hasPermission)
            {
                AddPermissionError(endpoint);
                return false;
            }

            return true;
        }

        /// <summary>
        /// <para>Returns true if the user with the given <paramref name="userId"/> has access to the building with the given <paramref name="organizationId"/>.</para>
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <param name="endpoint"></param>
        /// <param name="organizationId"></param>
        /// <param name="buildingId"></param>
        /// <param name="userId"></param>
        /// <param name="authCacheService"></param>
        /// <param name="cancellationToken"></param>
        /// <param name="allowDisabledOrganization"></param>
        /// <returns></returns>
        public static async Task<bool> ValidateUserBuildingAsync<TRequest>(this Endpoint<TRequest> endpoint, Guid? organizationId, Guid? buildingId, Guid userId,
            AuthCacheService authCacheService, CancellationToken cancellationToken = default, bool allowDisabledOrganization = false)
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
