using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace ADB_Tool_Automation_Post_FB.Helpers
{
    public static class FileHelper
    {
        private static readonly string API_BASE_URL = "https://boostgamemobile.com/service";

        public static async Task FetchPostImageSaveIntoSDCard(string deviceName, List<string> postImage)
        {
            if (postImage == null || postImage.Count == 0)
                return;

            try
            {
                string localFolderPath = Path.Combine(Directory.GetCurrentDirectory(), "Shared_images_ldplayer", deviceName);

                // Create folder if not exists
                Directory.CreateDirectory(localFolderPath);

                // Clean up old files
                try
                {
                    var files = Directory.GetFiles(localFolderPath);
                    Parallel.ForEach(files, file =>
                    {
                        try { File.Delete(file); }
                        catch (Exception ex)
                        {
                            Logger.LogError("[" + deviceName + "] Failed to delete " + file + ": " + ex.Message);
                        }
                    });
                }
                catch (Exception ex)
                {
                    Logger.LogError("[" + deviceName + "] Failed to clean folder: " + ex.Message);
                }

                using (HttpClient client = new HttpClient())
                {
                    foreach (var imageName in postImage)
                    {
                        string imageUrl = API_BASE_URL + "/daily_posts_content/image?file=" + Uri.EscapeDataString(imageName);
                        string filePath = Path.Combine(localFolderPath, imageName);

                        try
                        {
                            byte[] imageData = await client.GetByteArrayAsync(imageUrl);
                            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                            {
                                await fs.WriteAsync(imageData, 0, imageData.Length);
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.LogError("[" + deviceName + "] Failed to download/save image '" + imageName + "': " + ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError("[" + deviceName + "] Unexpected error: " + ex.Message);
            }
        }
    }
}
