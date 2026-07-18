using OpenCvSharp;
using System.IO;
using System.Windows.Media.Imaging;


namespace IndustrialVisionHost.Utils
{


    public class OpenCvHelper
    {


        public static BitmapImage MatToBitmapImage(Mat mat)
        {


            using (var stream = new MemoryStream())
            {


                BitmapImage bitmap = new BitmapImage();


                Cv2.ImEncode(".png", mat, out byte[] bytes);


                stream.Write(bytes, 0, bytes.Length);

                stream.Position = 0;


                bitmap.BeginInit();

                bitmap.StreamSource = stream;

                bitmap.CacheOption =
                    BitmapCacheOption.OnLoad;


                bitmap.EndInit();

                // 允许图像安全地从采集线程传递给 WPF UI 线程。
                bitmap.Freeze();


                return bitmap;


            }


        }


    }


}
