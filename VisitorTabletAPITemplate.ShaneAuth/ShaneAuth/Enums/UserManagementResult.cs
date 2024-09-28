namespace VisitorTabletAPITemplate.ShaneAuth.Enums
{
    public enum UserManagementResult
    {
        UnknownError,
        Ok,
        UserDidNotExist,
        NewUserCreated,
        ExistingUserAddedToOganization,
        ExistingUserAddedToBuilding,
        UserAlreadyExists,
        UserAlreadyExistsInOrganization,
        UserAlreadyExistsInBuilding,
        UserDidNotExistInOrganization,
        UserDidNotExistInBuilding,
        NewUserCreatedButStoreAvatarImageFailed,
        UserAssetTypesInvalid,
        UserAdminFunctionsInvalid,
        UserAdminAssetTypesInvalid,
        ConcurrencyKeyInvalid
    }
}
