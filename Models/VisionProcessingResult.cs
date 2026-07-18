using System;
using OpenCvSharp;

namespace IndustrialVisionHost.Models
{
    public sealed class VisionProcessingResult : IDisposable
    {
        public VisionProcessingResult(
            InspectionResult inspection,
            Mat annotatedImage,
            Mat displayImage)
        {
            Inspection = inspection
                ?? throw new ArgumentNullException(nameof(inspection));
            AnnotatedImage = annotatedImage
                ?? throw new ArgumentNullException(nameof(annotatedImage));
            DisplayImage = displayImage
                ?? throw new ArgumentNullException(nameof(displayImage));
        }

        public InspectionResult Inspection { get; }

        public Mat AnnotatedImage { get; }

        public Mat DisplayImage { get; }

        public void Dispose()
        {
            if (!ReferenceEquals(DisplayImage, AnnotatedImage))
            {
                DisplayImage.Dispose();
            }

            AnnotatedImage.Dispose();
        }
    }
}
