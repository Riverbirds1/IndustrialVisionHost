namespace IndustrialVisionHost.Camera
{
    public enum FakeCameraScenario
    {
        StandardSingle,
        DoubleTarget,
        SmallTargetNg,
        MovingTarget,
        DynamicDemo,
        NoisyTarget,
        CaptureFailure
    }

    public interface ISimulationCameraControl
    {
        FakeCameraScenario Scenario { get; set; }
    }
}
