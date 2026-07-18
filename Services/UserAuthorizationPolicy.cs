using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public static class UserAuthorizationPolicy
    {
        private const UserPermission OperatorPermissions =
            UserPermission.OperateMachine |
            UserPermission.ChangeBatch |
            UserPermission.LoadRecipe |
            UserPermission.QueryHistory |
            UserPermission.ViewAlarms;

        private const UserPermission EngineerPermissions =
            OperatorPermissions |
            UserPermission.EditVisionParameters |
            UserPermission.SaveRecipe |
            UserPermission.ExportHistory |
            UserPermission.ConfigureCommunication |
            UserPermission.ChangeOperationMode |
            UserPermission.ResetFault |
            UserPermission.AcknowledgeAlarms |
            UserPermission.ManageSystemSettings;

        private const UserPermission AdministratorPermissions =
            EngineerPermissions |
            UserPermission.ResetEmergency |
            UserPermission.ManageUsers;

        public static UserPermission GetPermissions(UserRole role)
        {
            return role switch
            {
                UserRole.Operator => OperatorPermissions,
                UserRole.Engineer => EngineerPermissions,
                UserRole.Administrator => AdministratorPermissions,
                _ => UserPermission.None
            };
        }

        public static bool HasPermission(
            UserRole role,
            UserPermission permission)
        {
            UserPermission granted = GetPermissions(role);
            return (granted & permission) == permission;
        }
    }
}
