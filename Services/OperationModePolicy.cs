using IndustrialVisionHost.Models;

namespace IndustrialVisionHost.Services
{
    public static class OperationModePolicy
    {
        public static bool CanExecuteDetection(
            OperationMode mode,
            DetectionTriggerSource triggerSource)
        {
            return mode switch
            {
                OperationMode.Manual =>
                    triggerSource == DetectionTriggerSource.ManualButton,
                OperationMode.Automatic =>
                    triggerSource == DetectionTriggerSource.TextPlc ||
                    triggerSource == DetectionTriggerSource.Modbus,
                _ => false
            };
        }

        public static bool CanChangeMode(MachineState machineState)
        {
            return machineState == MachineState.Idle ||
                machineState == MachineState.Ready ||
                machineState == MachineState.Completed;
        }
    }
}
