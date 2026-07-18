using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost.Tests;

public sealed class UserAuthorizationPolicyTests
{
    [Fact]
    public void Operator_CanOperateAndView_ButCannotChangeSystemSettings()
    {
        UserPermission permissions =
            UserAuthorizationPolicy.GetPermissions(UserRole.Operator);

        Assert.True(permissions.HasFlag(UserPermission.OperateMachine));
        Assert.True(permissions.HasFlag(UserPermission.QueryHistory));
        Assert.True(permissions.HasFlag(UserPermission.ViewAlarms));
        Assert.False(permissions.HasFlag(UserPermission.SaveRecipe));
        Assert.False(permissions.HasFlag(UserPermission.ManageSystemSettings));
    }

    [Fact]
    public void Engineer_CanConfigureAndAcknowledge_ButCannotManageUsers()
    {
        UserPermission permissions =
            UserAuthorizationPolicy.GetPermissions(UserRole.Engineer);

        Assert.True(permissions.HasFlag(UserPermission.SaveRecipe));
        Assert.True(permissions.HasFlag(UserPermission.ConfigureCommunication));
        Assert.True(permissions.HasFlag(UserPermission.AcknowledgeAlarms));
        Assert.True(permissions.HasFlag(UserPermission.ManageSystemSettings));
        Assert.False(permissions.HasFlag(UserPermission.ManageUsers));
        Assert.False(permissions.HasFlag(UserPermission.ResetEmergency));
    }

    [Fact]
    public void Administrator_HasEveryDefinedPermission()
    {
        UserPermission permissions =
            UserAuthorizationPolicy.GetPermissions(UserRole.Administrator);

        foreach (UserPermission permission in Enum.GetValues<UserPermission>())
        {
            if (permission != UserPermission.None)
            {
                Assert.True(permissions.HasFlag(permission), permission.ToString());
            }
        }
    }
}
