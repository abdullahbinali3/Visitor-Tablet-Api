namespace VisitorTabletAPITemplate.ShaneAuth.Enums
{
    public enum UserForgotPasswordResult
    {
        UnknownError,
        Ok,
        UserDidNotExist,
        NoAccess,
        LocalLoginDisabled,
        ForgotPasswordTokenInvalid
    }
}
