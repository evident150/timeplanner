using System;
using System.Windows.Media.Imaging;

namespace TimePlanner.Core
{
    /// <summary>内嵌图片资源。全部以 /resource 打进 exe，运行时不需要外带文件。</summary>
    public static class Art
    {
        static BitmapImage minister;

        /// <summary>左下角说贺辞的小人（透明底 PNG）。</summary>
        public static BitmapImage Minister()
        {
            if (minister == null) minister = Load("minister.png");
            return minister;
        }

        static BitmapImage Load(string fileName)
        {
            BitmapImage found = null;
            try
            {
                System.Reflection.Assembly asm = System.Reflection.Assembly.GetExecutingAssembly();
                string[] names = asm.GetManifestResourceNames();
                for (int i = 0; i < names.Length && found == null; i++)
                {
                    if (!names[i].EndsWith(fileName, StringComparison.OrdinalIgnoreCase)) continue;
                    System.IO.Stream s = asm.GetManifestResourceStream(names[i]);
                    if (s == null) continue;
                    BitmapImage b = new BitmapImage();
                    b.BeginInit();
                    b.StreamSource = s;
                    b.CacheOption = BitmapCacheOption.OnLoad;
                    b.EndInit();
                    b.Freeze();
                    s.Close();
                    found = b;
                }
            }
            catch (Exception)
            {
                found = null;
            }
            return found;
        }
    }
}
