using OpenCvSharp;
using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace IndustrialVisionHost.Utils
{
    public class BitmapConverter
    {

        public static BitmapSource MatToBitmapSource(Mat mat)
        {

            byte[] buffer;

            Cv2.ImEncode(
                ".png",
                mat,
                out buffer
            );


            using MemoryStream ms =
                new MemoryStream(buffer);


            BitmapImage bitmap =
                new BitmapImage();


            bitmap.BeginInit();

            bitmap.StreamSource = ms;

            bitmap.CacheOption =
                BitmapCacheOption.OnLoad;

            bitmap.EndInit();

            bitmap.Freeze();


            return bitmap;

        }

    }
}