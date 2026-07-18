using System;
using OpenCvSharp;

namespace IndustrialVisionHost.Camera
{
    public interface ICamera : IDisposable
    {
        bool IsConnected { get; }

        bool Open();

        Mat Capture();

        void Close();
    }
}
