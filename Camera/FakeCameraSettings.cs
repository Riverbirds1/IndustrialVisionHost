namespace IndustrialVisionHost.Camera
{
    public sealed class FakeCameraSettings
    {
        public int FrameWidth { get; init; } = 640;

        public int FrameHeight { get; init; } = 480;

        public int StandardRadius { get; init; } = 65;

        public int SmallTargetRadius { get; init; } = 12;

        public int HorizontalTravel { get; init; } = 100;

        public int NoisePointCount { get; init; } = 120;

        public int TargetHoleCount { get; init; } = 25;
    }
}
