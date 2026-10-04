using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace ImpulsaExplorer.Services;

public static class ShellIconService
{
    public static async Task<ImageSource?> GetIconAsync(string path, bool isFolder, bool isDrive)
    {
        try
        {
            StorageItemThumbnail? thumbnail;

            if (isFolder || isDrive)
            {
                var folder = await StorageFolder.GetFolderFromPathAsync(path);
                thumbnail = await folder.GetThumbnailAsync(
                    ThumbnailMode.ListView,
                    32,
                    ThumbnailOptions.UseCurrentScale);
            }
            else
            {
                var file = await StorageFile.GetFileFromPathAsync(path);
                thumbnail = await file.GetThumbnailAsync(
                    ThumbnailMode.ListView,
                    32,
                    ThumbnailOptions.UseCurrentScale);
            }

            using (thumbnail)
            {
                if (thumbnail is null || thumbnail.Size == 0)
                    return null;

                var image = new BitmapImage
                {
                    DecodePixelWidth = 24,
                    DecodePixelHeight = 24
                };
                await image.SetSourceAsync(thumbnail);
                return image;
            }
        }
        catch
        {
            return null;
        }
    }
}
