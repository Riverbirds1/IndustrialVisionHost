using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost.Tests;

public sealed class OperationModePolicyTests
{
    [Theory]
    [InlineData(OperationMode.Manual, DetectionTriggerSource.ManualButton, true)]
    [InlineData(OperationMode.Manual, DetectionTriggerSource.TextPlc, false)]
    [InlineData(OperationMode.Manual, DetectionTriggerSource.Modbus, false)]
    [InlineData(OperationMode.Automatic, DetectionTriggerSource.ManualButton, false)]
    [InlineData(OperationMode.Automatic, DetectionTriggerSource.TextPlc, true)]
    [InlineData(OperationMode.Automatic, DetectionTriggerSource.Modbus, true)]
    public void DetectionPermissionMatrix_IsEnforced(
        OperationMode mode,
        DetectionTriggerSource source,
        bool expected)
    {
        Assert.Equal(
            expected,
            OperationModePolicy.CanExecuteDetection(mode, source));
    }

    [Theory]
    [InlineData(MachineState.Idle, true)]
    [InlineData(MachineState.Ready, true)]
    [InlineData(MachineState.Completed, true)]
    [InlineData(MachineState.Running, false)]
    [InlineData(MachineState.Fault, false)]
    [InlineData(MachineState.Emergency, false)]
    public void ModeChange_DependsOnMachineState(
        MachineState state,
        bool expected)
    {
        Assert.Equal(expected, OperationModePolicy.CanChangeMode(state));
    }
}
