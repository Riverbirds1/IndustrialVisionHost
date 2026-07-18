using System;

namespace IndustrialVisionHost.Models
{
    [Flags]
    public enum UserPermission
    {
        None = 0,
        OperateMachine = 1 << 0,
        ChangeBatch = 1 << 1,
        LoadRecipe = 1 << 2,
        QueryHistory = 1 << 3,
        EditVisionParameters = 1 << 4,
        SaveRecipe = 1 << 5,
        ExportHistory = 1 << 6,
        ConfigureCommunication = 1 << 7,
        ChangeOperationMode = 1 << 8,
        ResetFault = 1 << 9,
        ResetEmergency = 1 << 10,
        ManageUsers = 1 << 11,
        ViewAlarms = 1 << 12,
        AcknowledgeAlarms = 1 << 13,
        ManageSystemSettings = 1 << 14
    }
}
