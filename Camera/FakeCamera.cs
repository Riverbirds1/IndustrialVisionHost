using System;
using OpenCvSharp;

namespace IndustrialVisionHost.Camera
{
    public sealed class FakeCamera : ICamera, ISimulationCameraControl
    {
        private readonly FakeCameraSettings settings;
        private readonly object scenarioSync = new object();
        private long frameNumber;
        private FakeCameraScenario scenario = FakeCameraScenario.StandardSingle;
        private bool simulatedFaultActive;

        public FakeCamera()
            : this(new FakeCameraSettings())
        {
        }

        public FakeCamera(FakeCameraSettings settings)
        {
            this.settings = settings
                ?? throw new ArgumentNullException(nameof(settings));

            ValidateSettings(settings);
        }

        public bool IsConnected { get; private set; }

        public FakeCameraScenario Scenario
        {
            get
            {
                lock (scenarioSync)
                {
                    return scenario;
                }
            }
            set
            {
                lock (scenarioSync)
                {
                    scenario = value;
                    frameNumber = 0;
                    simulatedFaultActive = false;
                }
            }
        }

        public bool Open()
        {
            lock (scenarioSync)
            {
                if (scenario == FakeCameraScenario.CaptureFailure &&
                    simulatedFaultActive)
                {
                    IsConnected = false;
                    return false;
                }

                frameNumber = 0;
            }

            IsConnected = true;
            return true;
        }

        public Mat Capture()
        {
            if (!IsConnected)
            {
                throw new InvalidOperationException("相机尚未打开，无法采集图像。");
            }

            FakeCameraScenario currentScenario;
            long currentFrame;
            lock (scenarioSync)
            {
                currentScenario = scenario;
                currentFrame = frameNumber++;
            }

            Mat image = new Mat(
                settings.FrameHeight,
                settings.FrameWidth,
                MatType.CV_8UC3,
                new Scalar(255, 255, 255));

            try
            {
                DrawScenario(image, currentScenario, currentFrame);
                return image;
            }
            catch
            {
                image.Dispose();
                throw;
            }
        }

        private void DrawScenario(
            Mat image,
            FakeCameraScenario currentScenario,
            long currentFrame)
        {
            double phase = currentFrame * 0.12;

            switch (currentScenario)
            {
                case FakeCameraScenario.StandardSingle:
                    DrawTargets(image, 1, settings.StandardRadius, 0);
                    break;

                case FakeCameraScenario.DoubleTarget:
                    DrawTargets(image, 2, settings.StandardRadius, 0);
                    break;

                case FakeCameraScenario.SmallTargetNg:
                    DrawTargets(image, 1, settings.SmallTargetRadius, 0);
                    break;

                case FakeCameraScenario.MovingTarget:
                    int movingOffset =
                        (int)Math.Round(Math.Sin(phase) * settings.HorizontalTravel);
                    DrawTargets(image, 1, settings.StandardRadius, movingOffset);
                    break;

                case FakeCameraScenario.DynamicDemo:
                    int radius = settings.StandardRadius +
                        (int)Math.Round(Math.Sin(phase * 0.7) * 15);
                    int targetCount = (int)(currentFrame / 20 % 3) + 1;
                    int dynamicOffset =
                        (int)Math.Round(Math.Sin(phase) * 70);
                    DrawTargets(image, targetCount, radius, dynamicOffset);
                    break;

                case FakeCameraScenario.NoisyTarget:
                    DrawTargets(image, 1, settings.StandardRadius, 0);
                    DrawNoise(image, currentFrame);
                    break;

                case FakeCameraScenario.CaptureFailure:
                    if (currentFrame >= 5)
                    {
                        lock (scenarioSync)
                        {
                            simulatedFaultActive = true;
                            IsConnected = false;
                        }

                        throw new InvalidOperationException(
                            "模拟相机连接已中断，图像采集失败。");
                    }

                    DrawTargets(image, 1, settings.StandardRadius, 0);
                    break;

                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(currentScenario),
                        currentScenario,
                        "未知的模拟相机场景。");
            }
        }

        private void DrawNoise(Mat image, long currentFrame)
        {
            var random = new Random(unchecked((int)(currentFrame * 397) + 20260717));

            for (int index = 0; index < settings.NoisePointCount; index++)
            {
                int x = random.Next(0, settings.FrameWidth);
                int y = random.Next(0, settings.FrameHeight);
                int radius = random.Next(1, 4);

                Cv2.Circle(
                    image,
                    new Point(x, y),
                    radius,
                    new Scalar(20, 20, 20),
                    -1);
            }

            int centerX = settings.FrameWidth / 2;
            int centerY = settings.FrameHeight / 2;
            int availableRadius = settings.StandardRadius - 6;

            for (int index = 0; index < settings.TargetHoleCount; index++)
            {
                double angle = random.NextDouble() * Math.PI * 2;
                double distance = Math.Sqrt(random.NextDouble()) * availableRadius;
                int x = centerX + (int)Math.Round(Math.Cos(angle) * distance);
                int y = centerY + (int)Math.Round(Math.Sin(angle) * distance);
                int radius = random.Next(1, 4);

                Cv2.Circle(
                    image,
                    new Point(x, y),
                    radius,
                    new Scalar(255, 255, 255),
                    -1);
            }

            Cv2.Line(
                image,
                new Point(centerX - availableRadius, centerY),
                new Point(centerX + availableRadius, centerY),
                new Scalar(255, 255, 255),
                1);

            Cv2.Line(
                image,
                new Point(centerX, centerY - availableRadius),
                new Point(centerX, centerY + availableRadius),
                new Scalar(255, 255, 255),
                1);
        }

        private void DrawTargets(
            Mat image,
            int targetCount,
            int radius,
            int horizontalOffset)
        {
            int spacing = radius * 2 + 25;
            double groupCenterX = settings.FrameWidth / 2.0 + horizontalOffset;
            int centerY = settings.FrameHeight / 2;

            for (int index = 0; index < targetCount; index++)
            {
                double relativeIndex = index - (targetCount - 1) / 2.0;
                int centerX = (int)Math.Round(groupCenterX + relativeIndex * spacing);
                centerX = Math.Clamp(
                    centerX,
                    radius,
                    settings.FrameWidth - radius - 1);

                Cv2.Circle(
                    image,
                    new Point(centerX, centerY),
                    radius,
                    new Scalar(0, 0, 255),
                    -1);
            }
        }

        private static void ValidateSettings(FakeCameraSettings settings)
        {
            if (settings.FrameWidth <= 0 || settings.FrameHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    "模拟图像宽度和高度必须大于 0。");
            }

            if (settings.StandardRadius <= 0 ||
                settings.StandardRadius + 15 >= settings.FrameHeight / 2)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    "标准目标半径超出模拟图像范围。");
            }

            if (settings.SmallTargetRadius <= 0 ||
                settings.SmallTargetRadius >= settings.StandardRadius)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    "小目标半径必须大于 0 且小于标准目标半径。");
            }

            if (settings.HorizontalTravel < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    "水平移动范围不能为负数。");
            }


            if (settings.NoisePointCount < 0 || settings.TargetHoleCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(settings),
                    "噪点数量和孔洞数量不能为负数。");
            }
        }

        public void Close()
        {
            IsConnected = false;
        }

        public void Dispose()
        {
            Close();
        }
    }
}
