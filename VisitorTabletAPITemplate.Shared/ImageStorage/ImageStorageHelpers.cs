using MimeDetective;
using MimeDetective.Engine;
using VisitorTabletAPITemplate.ImageStorage.Enums;
using VisitorTabletAPITemplate.ObjectClasses;
using VisitorTabletAPITemplate.Utilities;
using System.Collections.Immutable;
using System.Text;

namespace VisitorTabletAPITemplate.ImageStorage
{
    public static class ImageStorageHelpers
    {
        public const string ValidImageFormats = ".jpg, .jpeg, .png, .gif, .bmp, .webp";

        public static bool IsValidImageExtension(string extension)
        {
            switch (extension)
            {
                case "gif":
                case "jpg":
                case "jpeg":
                case "png":
                case "bmp":
                case "webp":
                case ".gif":
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".webp":
                    return true;
            }

            return false;
        }

        public static bool IsValidImageExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename?.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidImageExtension(extension);
        }

        public const string ValidVectorImageFormats = ".svg";

        public static bool IsValidVectorImageExtension(string extension)
        {
            switch (extension)
            {
                case "svg":
                case ".svg":
                    return true;
            }

            return false;
        }

        public static bool IsValidVectorImageExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidVectorImageExtension(extension);
        }

        public const string ValidVectorAndTransparentImageFormats = ".png, .gif, .webp, .svg";

        public static bool IsValidVectorOrTransparentImageExtension(string extension)
        {
            switch (extension)
            {
                case "gif":
                case "png":
                case "webp":
                case "svg":
                case ".gif":
                case ".png":
                case ".webp":
                case ".svg":
                    return true;
            }

            return false;
        }

        public static bool IsValidVectorOrTransparentImageExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidVectorOrTransparentImageExtension(extension);
        }

        public const string ValidAnyVectorOrImageFormats = ".jpg, .jpeg, .png, .gif, .bmp, .webp, .svg";

        public static bool IsValidAnyVectorOrImageExtension(string extension)
        {
            switch (extension)
            {
                case "gif":
                case "jpg":
                case "jpeg":
                case "png":
                case "bmp":
                case "webp":
                case "svg":
                case ".gif":
                case ".jpg":
                case ".jpeg":
                case ".png":
                case ".bmp":
                case ".webp":
                case ".svg":
                    return true;
            }

            return false;
        }

        public static bool IsValidAnyVectorOrImageExtensionFromFilename(string filename)
        {
            string? extension = Path.GetExtension(filename.ToLowerInvariant()); // includes dot (.)

            if (extension is null)
            {
                return false;
            }

            return IsValidAnyVectorOrImageExtension(extension);
        }

        public static bool IsValidImageStorageRelatedObjectType(int imageStorageRelatedObjectType)
        {
            return Enum.IsDefined(typeof(ImageStorageRelatedObjectType), imageStorageRelatedObjectType);
        }

        public static bool IsValidImageStorageRelatedObjectType(int? imageStorageRelatedObjectType)
        {
            if (!imageStorageRelatedObjectType.HasValue)
            {
                return false;
            }

            return Enum.IsDefined(typeof(ImageStorageRelatedObjectType), imageStorageRelatedObjectType.Value);
        }

        public static bool TryParseImageStorageRelatedObjectType(int imageStorageRelatedObjectType, out ImageStorageRelatedObjectType? parsedImageStorageRelatedObjectType)
        {
            parsedImageStorageRelatedObjectType = null;

            if (IsValidImageStorageRelatedObjectType(imageStorageRelatedObjectType))
            {
                parsedImageStorageRelatedObjectType = (ImageStorageRelatedObjectType)imageStorageRelatedObjectType;
                return true;
            }

            return false;
        }

        /// <summary>
        /// <para>Copies <see cref="IFormFile"/> stream to a <see cref="MemoryStream"/> and inspects the file's contents to determine the actual extension and mime type.</para>
        /// <para>After calling this function the <see cref="IFormFile"> stream will be consumed and should no longer be used.</para>
        /// <para>Returns null and doesn't consume the <see cref="IFormFile"/> stream if <paramref name="formFile"/> is null or length 0.</para>
        /// </summary>
        /// <param name="formFile">The <see cref="IFormFile"/> containing the uploaded image file.</param>
        /// <returns></returns>
        public static async Task<ContentInspectorResultWithMemoryStream?> CopyFormFileContentAndInspectImageAsync(IFormFile formFile, CancellationToken cancellationToken = default)
        {
            if (formFile == null || formFile.Length == 0)
            {
                return null;
            }

            ContentInspectorResultWithMemoryStream result = new ContentInspectorResultWithMemoryStream();

            result.FileDataStream = new MemoryStream((int)formFile.Length);
            await formFile.CopyToAsync(result.FileDataStream, cancellationToken);

            result.FileDataStream.Position = 0;

            ImmutableArray<DefinitionMatch> inspectResult = ImageContentInspector.Instance.ContentInspector.Inspect(result.FileDataStream);
            result.InspectedExtension = inspectResult.ByFileExtension().FirstOrDefault()?.Extension;
            result.InspectedMimeType = inspectResult.ByMimeType().FirstOrDefault()?.MimeType;

            if (!string.IsNullOrEmpty(formFile.FileName))
            {
                result.FileName = Path.GetFileName(formFile.FileName);
                result.OriginalExtension = Path.GetExtension(formFile.FileName);
            }

            result.FileDataStream.Position = 0;

            // If file type is SVG, sanitize the file and overwrite the FileDataStream
            if (result.InspectedExtension == "svg")
            {
                using (StreamReader sr = new StreamReader(result.FileDataStream))
                {
                    string svgContent = sr.ReadToEnd();

                    string sanitized = SvgSanitizer.Sanitize(svgContent);
                    result.FileDataStream = new MemoryStream(Encoding.UTF8.GetBytes(sanitized));
                    result.IsSanitized = true;

                    result.FileDataStream.Position = 0;
                }
            }

            return result;
        }

        /// <summary>
        /// <para>Copies the given <paramref name="imageStream"/> to a <see cref="MemoryStream"/> and inspects the file's contents to determine the actual extension and mime type.</para>
        /// <para>After calling this function, the <paramref name="imageStream"/> stream will be consumed and should no longer be used if applicable - e.g. if it came from a network.</para>
        /// <para>Returns null and doesn't consume the <paramref name="imageStream"/> stream if <paramref name="imageStream"/> is null or length 0.</para>
        /// </summary>
        /// <param name="imageStream"></param>
        /// <param name="filename"></param>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        public static async Task<ContentInspectorResultWithMemoryStream?> CopyFormStreamAndInspectImageAsync(Stream imageStream, string? filename, CancellationToken cancellationToken = default)
        {
            if (imageStream == null || imageStream.Length == 0)
            {
                return null;
            }

            ContentInspectorResultWithMemoryStream result = new ContentInspectorResultWithMemoryStream();

            result.FileDataStream = new MemoryStream((int)imageStream.Length);
            await imageStream.CopyToAsync(result.FileDataStream, cancellationToken);

            result.FileDataStream.Position = 0;

            ImmutableArray<DefinitionMatch> inspectResult = ImageContentInspector.Instance.ContentInspector.Inspect(result.FileDataStream);
            result.InspectedExtension = inspectResult.ByFileExtension().FirstOrDefault()?.Extension;
            result.InspectedMimeType = inspectResult.ByMimeType().FirstOrDefault()?.MimeType;

            if (!string.IsNullOrEmpty(filename))
            {
                result.FileName = Path.GetFileName(filename);
                result.OriginalExtension = Path.GetExtension(filename);
            }

            result.FileDataStream.Position = 0;

            // If file type is SVG, sanitize the file and overwrite the FileDataStream
            if (result.InspectedExtension == "svg")
            {
                using (StreamReader sr = new StreamReader(result.FileDataStream))
                {
                    string svgContent = sr.ReadToEnd();

                    string sanitized = SvgSanitizer.Sanitize(svgContent);
                    result.FileDataStream = new MemoryStream(Encoding.UTF8.GetBytes(sanitized));
                    result.IsSanitized = true;

                    result.FileDataStream.Position = 0;
                }
            }

            return result;
        }
    }
}
