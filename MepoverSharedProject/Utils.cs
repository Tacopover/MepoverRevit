using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Media.Imaging;

namespace MepoverSharedProject.Utilities
{
    public static class Utils
    {


        public static System.Windows.Media.Imaging.BitmapImage LoadEmbeddedImage(Assembly assembly, string imagePath)
        {
            var img = new System.Windows.Media.Imaging.BitmapImage();
            try
            {
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(x => x.Contains(imagePath));
                System.IO.Stream stream = assembly.GetManifestResourceStream(resourceName);
                img.BeginInit();
                img.StreamSource = stream;
                img.EndInit();
            }
            catch
            {
                // Handle any exceptions or error logging here
            }
            return img;
        }
    }
}
