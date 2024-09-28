using VisitorTabletAPITemplate.ShaneAuth.Enums;
using VisitorTabletAPITemplate.ShaneAuth.Models;
using VisitorTabletAPITemplate.Utilities;
using System.Security.Claims;

namespace VisitorTabletAPITemplate.ShaneAuth
{
    public static class ShaneAuthHelpers
    {
        public static Guid? GetId(this ClaimsPrincipal principal)
        {
            foreach (Claim claim in principal.Claims)
            {
                switch (claim.Type)
                {
                    case "Uid":
                    case ClaimTypes.NameIdentifier:
                    case "sub":
                        return Toolbox.ParseNullableGuid(claim.Value);
                }
            }

            return null;
        }

        public static string? GetName(this ClaimsPrincipal principal)
        {
            foreach (Claim claim in principal.Claims)
            {
                switch (claim.Type)
                {
                    case "DisplayName":
                    case ClaimTypes.Name:
                        return claim.Value;
                }
            }

            return null;
        }

        public static (Guid? id, string? name) GetIdAndName(this ClaimsPrincipal principal)
        {
            Guid? id = null;
            string? name = null;

            foreach (Claim claim in principal.Claims)
            {
                switch (claim.Type)
                {
                    case "DisplayName":
                    case ClaimTypes.Name:
                        name = claim.Value;
                        break;
                    case "Uid":
                    case ClaimTypes.NameIdentifier:
                    case "sub":
                        id = Toolbox.ParseNullableGuid(claim.Value);
                        break;
                }

                if (id is not null && name is not null)
                {
                    return (id, name);
                }
            }
            
            return (id, name);
        }

        public static string? GetEmail(this ClaimsPrincipal principal)
        {
            foreach (Claim claim in principal.Claims)
            {
                switch (claim.Type)
                {
                    case "Email":
                    case ClaimTypes.Email:
                    case "sub":
                        return claim.Value;
                }
            }

            return null;
        }

        public static bool IsValidUserSystemRole(int userSystemRole)
        {
            return Enum.IsDefined(typeof(UserSystemRole), userSystemRole);
        }

        public static bool IsValidUserSystemRole(int? userSystemRole)
        {
            if (!userSystemRole.HasValue)
            {
                return false;
            }

            return Enum.IsDefined(typeof(UserSystemRole), userSystemRole.Value);
        }

        public static bool TryParseUserSystemRole(int userSystemRole, out UserSystemRole? parsedUserSystemRole)
        {
            parsedUserSystemRole = null;

            if (IsValidUserSystemRole(userSystemRole))
            {
                parsedUserSystemRole = (UserSystemRole)userSystemRole;
                return true;
            }

            return false;
        }

        public static UserSystemRole GetUserSystemRole(ClaimsPrincipal claimsPrincipal)
        {
            string? userSystemRoleString = claimsPrincipal.ClaimValue("UserSystemRole");

            if (userSystemRoleString is null)
            {
                return UserSystemRole.NoAccess;
            }

            if (int.TryParse(userSystemRoleString, out int userSystemRoleInt))
            {
                if (TryParseUserSystemRole(userSystemRoleInt, out UserSystemRole? parsedUserSystemRole))
                {
                    return parsedUserSystemRole!.Value;
                }
            }

            return UserSystemRole.NoAccess;
        }

        public static bool IsValidUserOrganizationRole(int userOrganizationRole)
        {
            return Enum.IsDefined(typeof(UserOrganizationRole), userOrganizationRole);
        }

        public static bool IsValidUserOrganizationRole(int? userOrganizationRole)
        {
            if (!userOrganizationRole.HasValue)
            {
                return false;
            }

            return Enum.IsDefined(typeof(UserOrganizationRole), userOrganizationRole.Value);
        }

        public static bool TryParseUserOrganizationRole(int userOrganizationRole, out UserOrganizationRole? parsedUserOrganizationRole)
        {
            parsedUserOrganizationRole = null;

            if (IsValidUserOrganizationRole(userOrganizationRole))
            {
                parsedUserOrganizationRole = (UserOrganizationRole)userOrganizationRole;
                return true;
            }

            return false;
        }

        public static void PopulateUserPrivileges(UserPrivileges userPrivileges, UserData userData)
        {
            userPrivileges.Claims.Add(new("Uid", userData.Uid.ToString()));

            if (userData.DisplayName is not null)
            {
                userPrivileges.Claims.Add(new("DisplayName", userData.DisplayName));
            }

            userPrivileges.Claims.Add(new("Email", userData.Email));
            userPrivileges.Roles.Add(userData.UserSystemRole?.ToString() ?? UserSystemRole.NoAccess.ToString());
        }
    }
}
