using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace ADB_Tool_Automation_Post_FB.Infrastructure.ResourcePopup
{
    public static class ResourcePopupTitleTemplateCropper
    {
        public static byte[] TryCreateStableTitle(byte[] templateBytes)
        {
            try
            {
                using (var input = new MemoryStream(templateBytes, writable: false))
                using (var source = new Bitmap(input))
                {
                    // Resource popup templates share the same layout. The node icon
                    // occupies the first ~125 px; the title text to its right is stable
                    // across node levels and map lighting.
                    int left = Math.Min(125, source.Width - 1);
                    int height = Math.Min(45, source.Height);
                    int width = source.Width - left;
                    if (width <= 0 || height <= 0) return null;

                    using (Bitmap stable = source.Clone(
                        new Rectangle(left, 0, width, height),
                        PixelFormat.Format32bppArgb))
                    using (var output = new MemoryStream())
                    {
                        stable.Save(output, ImageFormat.Png);
                        return output.ToArray();
                    }
                }
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }
}
