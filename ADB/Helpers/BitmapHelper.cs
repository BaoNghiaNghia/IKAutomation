using System.Drawing;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    /*
     * BitmapHelper.cs
     * Helper methods to load, cache and safely clone Bitmaps using byte[]
     */
    public static class BitmapHelper
    {
        /// <summary>
        /// Loads a Bitmap from file (legacy).
        /// </summary>
        public static Bitmap Load(string path)
        {
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                return new Bitmap(fs);
            }
        }

        /// <summary>
        /// Clones a Bitmap to avoid GDI+ issues (legacy).
        /// </summary>
        public static Bitmap Clone(Bitmap src)
        {
            return src.Clone(new Rectangle(0, 0, src.Width, src.Height), src.PixelFormat);
        }

        /// <summary>
        /// Loads an image file into a byte array.
        /// Should only be called once at startup.
        /// </summary>
        public static byte[] LoadAsBytes(string path)
        {
            return File.ReadAllBytes(path);
        }

        /// <summary>
        /// Creates a new Bitmap instance from byte[] safely.
        /// This version does NOT dispose the MemoryStream immediately,
        /// because Bitmap keeps a reference to it internally.
        /// Caller must dispose the Bitmap after use.
        /// </summary>
        public static Bitmap CreateFromBytes(byte[] imageBytes)
        {
            // MemoryStream must remain alive as long as Bitmap is in use
            var ms = new MemoryStream(imageBytes); // don't dispose immediately
            return new Bitmap(ms); // Bitmap will hold reference to stream
        }

        /// <summary>
        /// Creates a fully independent Bitmap by copying from byte[].
        /// Safe for reuse even after stream is disposed.
        /// </summary>
        public static Bitmap CreateFromBytesSafe(byte[] imageBytes)
        {
            using (var ms = new MemoryStream(imageBytes))
            using (var bmp = new Bitmap(ms))
            {
                return bmp.Clone(new Rectangle(0, 0, bmp.Width, bmp.Height), bmp.PixelFormat);
            }
        }
    }
}
