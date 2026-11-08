using System;
using System.Net;
using System.Threading.Tasks;
using System.Linq;
using UnityEngine;
using System.IO;
using UnityEditor;

namespace UnityMcp.Editor
{
    internal sealed class ImportUiImageRequestHandler : IEditorRequestHandler
    {
        readonly EditorTaskQueue tasks;

        public ImportUiImageRequestHandler(EditorTaskQueue tasks)
        {
            this.tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        }

        public Task Handle(HttpListenerContext context) =>
            HttpRequestUtility.Handle<ImportUiImageRequest>(context, tasks, Execute, Error, Validate, bodyLimit: 12 * 1024 * 1024);

        static void Validate(ImportUiImageRequest request) => RequestProtocolValidation.Validate(request);

        [Serializable] sealed class Reply
        {
            public string id, instance_id, asset_path, guid, error, filter_mode;
            public bool ok;
            public int width, height;
            public float pixels_per_unit;
            public Vector4 border;
        }
        static string Error(string id, string message) => JsonUtility.ToJson(new Reply {
            id = id ?? "", instance_id = EditorInstanceInfo.InstanceId, error = message
        });
        static void CheckDeadline(ImportUiImageRequest request)
        {
            if (request.deadline <= DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0)
                throw new Exception("Image import expired before writing");
        }
        static string Execute(ImportUiImageRequest request)
        {
            Texture2D decoded = null;
            bool created = false;
            string fullPath = null;
            try
            {
                CheckDeadline(request);
                fullPath = ValidatePath(request.asset_path);
                if (File.Exists(fullPath) || Directory.Exists(fullPath) || File.Exists(fullPath + ".meta")
                    || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(request.asset_path)))
                    throw new Exception("Output already exists; refusing overwrite");
                if (request.border == null || request.border.Length != 4 || request.border.Any(n => n < 0))
                    throw new Exception("Border must be [left,bottom,right,top] nonnegative integers");
                if (float.IsNaN(request.pixels_per_unit) || float.IsInfinity(request.pixels_per_unit)
                    || request.pixels_per_unit <= 0 || request.pixels_per_unit > 10000)
                    throw new Exception("Invalid pixels_per_unit");
                if (request.filter_mode != "point" && request.filter_mode != "bilinear")
                    throw new Exception("Invalid filter_mode");
                byte[] bytes = Convert.FromBase64String(request.image_base64);
                if (bytes.Length == 0 || bytes.Length > 8 * 1024 * 1024) throw new Exception("Image must be at most 8 MiB");
                string format = Dimensions(bytes, out int width, out int height);
                string extension = Path.GetExtension(request.asset_path).ToLowerInvariant();
                if ((format == "png" && extension != ".png") || (format == "jpeg" && extension != ".jpg" && extension != ".jpeg"))
                    throw new Exception("Output extension does not match image contents");
                if (width < 1 || height < 1 || width > 8192 || height > 8192 || (long)width * height > 16 * 1024 * 1024)
                    throw new Exception("Image dimensions exceed 8192 per side or 16 megapixels");
                if ((long)request.border[0] + request.border[2] > width || (long)request.border[1] + request.border[3] > height)
                    throw new Exception("Border exceeds image dimensions");
                decoded = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!ImageConversion.LoadImage(decoded, bytes, true) || decoded.width != width || decoded.height != height)
                    throw new Exception("Invalid image data or mismatched dimensions");
                var border = new Vector4(request.border[0], request.border[1], request.border[2], request.border[3]);
                CheckDeadline(request);
                EnsureFolders(Path.GetDirectoryName(request.asset_path).Replace('\\', '/'));
                // Recheck after folder creation, then use exclusive creation to avoid overwriting races.
                if (File.Exists(fullPath + ".meta")) throw new Exception("Output metadata appeared during import");
                using (var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    created = true;
                    stream.Write(bytes, 0, bytes.Length);
                }
                AssetDatabase.ImportAsset(request.asset_path, ImportAssetOptions.ForceSynchronousImport);
                var importer = AssetImporter.GetAtPath(request.asset_path) as TextureImporter;
                if (importer == null) throw new Exception("Unity did not create a TextureImporter");
                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                settings.spriteMeshType = SpriteMeshType.FullRect;
                importer.SetTextureSettings(settings);
                importer.spritePixelsPerUnit = request.pixels_per_unit;
                importer.spriteBorder = border;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.isReadable = false;
                importer.npotScale = TextureImporterNPOTScale.None;
                importer.maxTextureSize = 8192;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.filterMode = request.filter_mode == "point" ? FilterMode.Point : FilterMode.Bilinear;
                importer.SaveAndReimport();
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(request.asset_path);
                if (sprite == null || sprite.rect.width != width || sprite.rect.height != height
                    || sprite.border != border || !Mathf.Approximately(sprite.pixelsPerUnit, request.pixels_per_unit))
                    throw new Exception("Imported Sprite settings differ; check project AssetPostprocessors/platform overrides");
                return JsonUtility.ToJson(new Reply {
                    id = request.id, instance_id = EditorInstanceInfo.InstanceId, ok = true,
                    asset_path = request.asset_path, guid = AssetDatabase.AssetPathToGUID(request.asset_path),
                    width = width, height = height, border = sprite.border, pixels_per_unit = sprite.pixelsPerUnit,
                    filter_mode = importer.filterMode == FilterMode.Point ? "point" : "bilinear"
                });
            }
            catch (Exception e)
            {
                string message = e.Message;
                if (created)
                {
                    try
                    {
                        if (!AssetDatabase.DeleteAsset(request.asset_path))
                        {
                            File.Delete(fullPath);
                            File.Delete(fullPath + ".meta");
                            AssetDatabase.Refresh();
                        }
                    }
                    catch { message += "; cleanup failed, inspect destination before retrying"; }
                }
                return Error(request.id, message);
            }
            finally { if (decoded != null) UnityEngine.Object.DestroyImmediate(decoded); }
        }
        static string ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal)
                || path.Contains('\\') || path.Contains(':') || path.Split('/').Any(p => p == "" || p == "." || p == ".."))
                throw new Exception("Expected an Assets/... image path");
            string current = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            foreach (string part in path.Split('/'))
            {
                current = Path.Combine(current, part);
                if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new Exception("Symlink/junction output paths are unsupported");
            }
            string extension = Path.GetExtension(path).ToLowerInvariant();
            if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") throw new Exception("Expected PNG/JPEG extension");
            return current;
        }
        static void EnsureFolders(string folder)
        {
            string parent = "Assets";
            foreach (string part in folder.Substring(6).Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string child = parent + "/" + part;
                if (!AssetDatabase.IsValidFolder(child) && string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, part)))
                    throw new Exception("Cannot create asset folder: " + child);
                parent = child;
            }
        }
        static string Dimensions(byte[] data, out int width, out int height)
        {
            if (data.Length >= 33 && data.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
                && data[12] == 73 && data[13] == 72 && data[14] == 68 && data[15] == 82)
            {
                width = data[16] << 24 | data[17] << 16 | data[18] << 8 | data[19];
                height = data[20] << 24 | data[21] << 16 | data[22] << 8 | data[23];
                return "png";
            }
            if (data.Length >= 4 && data[0] == 255 && data[1] == 216)
            {
                int pos = 2;
                while (pos < data.Length)
                {
                    if (data[pos] != 255) break;
                    while (pos < data.Length && data[pos] == 255) pos++;
                    if (pos >= data.Length) break;
                    int marker = data[pos++];
                    if (marker == 217 || marker == 218) break;
                    if (marker == 1 || marker >= 208 && marker <= 216) continue;
                    if (pos + 2 > data.Length) break;
                    int length = data[pos] << 8 | data[pos + 1];
                    if (length < 2 || pos + length > data.Length) break;
                    if (marker >= 192 && marker <= 194 && length >= 8)
                    {
                        height = data[pos + 3] << 8 | data[pos + 4];
                        width = data[pos + 5] << 8 | data[pos + 6];
                        return "jpeg";
                    }
                    pos += length;
                }
            }
            throw new Exception("Unsupported or invalid PNG/JPEG image");
        }
    }
}
