namespace IndustrialVisionHost.Models
{
    public enum UserRole
    {
        Operator = 1,
        Engineer = 2,
        Administrator = 3
    }

    public sealed class AuthenticatedUser
    {
        public AuthenticatedUser(
            long id,
            string username,
            string displayName,
            UserRole role,
            bool mustChangePassword)
        {
            Id = id;
            Username = username;
            DisplayName = displayName;
            Role = role;
            MustChangePassword = mustChangePassword;
        }

        public long Id { get; }

        public string Username { get; }

        public string DisplayName { get; }

        public UserRole Role { get; }

        public bool MustChangePassword { get; }

        public string RoleDisplayName => Role switch
        {
            UserRole.Operator => "操作员",
            UserRole.Engineer => "工程师",
            UserRole.Administrator => "管理员",
            _ => "未知角色"
        };
    }
}
