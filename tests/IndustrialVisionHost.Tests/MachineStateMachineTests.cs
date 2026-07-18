using IndustrialVisionHost.Models;
using IndustrialVisionHost.Services;

namespace IndustrialVisionHost.Tests;

public sealed class MachineStateMachineTests
{
    [Fact]
    public void NormalInspectionCycle_ReachesCompleted()
    {
        var machine = new MachineStateMachine();

        AssertTransition(machine, MachineState.Ready, "相机就绪");
        AssertTransition(machine, MachineState.Running, "开始检测");
        AssertTransition(machine, MachineState.Completed, "检测完成");

        Assert.Equal(MachineState.Completed, machine.CurrentState);
    }

    [Fact]
    public void IllegalTransition_IsRejectedWithoutChangingState()
    {
        var machine = new MachineStateMachine();

        bool result = machine.TryTransition(
            MachineState.Running,
            "跳过就绪",
            out string? error);

        Assert.False(result);
        Assert.Equal(MachineState.Idle, machine.CurrentState);
        Assert.Contains("不允许", error);
    }

    [Theory]
    [InlineData(MachineState.Idle)]
    [InlineData(MachineState.Ready)]
    [InlineData(MachineState.Running)]
    [InlineData(MachineState.Completed)]
    [InlineData(MachineState.Fault)]
    public void Emergency_CanInterruptEveryNonEmergencyState(
        MachineState startingState)
    {
        var machine = BuildState(startingState);

        AssertTransition(machine, MachineState.Emergency, "急停触发");
        Assert.Equal(MachineState.Emergency, machine.CurrentState);
    }

    [Fact]
    public void Emergency_CanOnlyResetToIdle()
    {
        var machine = new MachineStateMachine();
        AssertTransition(machine, MachineState.Emergency, "急停触发");

        Assert.False(machine.TryTransition(
            MachineState.Ready,
            "危险复位",
            out _));
        AssertTransition(machine, MachineState.Idle, "安全条件确认");
    }

    [Fact]
    public void StateChangedEvent_ContainsTransitionContext()
    {
        var machine = new MachineStateMachine();
        MachineStateChangedEventArgs? received = null;
        machine.StateChanged += (_, args) => received = args;

        AssertTransition(machine, MachineState.Ready, " 相机就绪 ");

        Assert.NotNull(received);
        Assert.Equal(MachineState.Idle, received!.PreviousState);
        Assert.Equal(MachineState.Ready, received.CurrentState);
        Assert.Equal("相机就绪", received.Reason);
    }

    private static MachineStateMachine BuildState(MachineState target)
    {
        var machine = new MachineStateMachine();
        switch (target)
        {
            case MachineState.Idle:
                break;
            case MachineState.Ready:
                AssertTransition(machine, MachineState.Ready, "准备");
                break;
            case MachineState.Running:
                AssertTransition(machine, MachineState.Ready, "准备");
                AssertTransition(machine, MachineState.Running, "运行");
                break;
            case MachineState.Completed:
                AssertTransition(machine, MachineState.Ready, "准备");
                AssertTransition(machine, MachineState.Running, "运行");
                AssertTransition(machine, MachineState.Completed, "完成");
                break;
            case MachineState.Fault:
                AssertTransition(machine, MachineState.Fault, "故障");
                break;
        }

        return machine;
    }

    private static void AssertTransition(
        MachineStateMachine machine,
        MachineState state,
        string reason)
    {
        Assert.True(machine.TryTransition(state, reason, out string? error), error);
    }
}
