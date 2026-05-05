using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace MonoRead.Infrastructure.Services
{
    public class WebDavStorageService : ICloudStorageService
    {
        private readonly HttpClient _httpClient;

        public WebDavStorageService()
        {
            var handler = new SocketsHttpHandler();
            _httpClient = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        private void SetBasicAuth(string username, string password)
        {
            var authString = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authString);
        }

        // ====================================================================
        // 【核心修复】：健壮的 URL 拼接器
        // 完美解决 /dav 路径重复叠加导致的 409 错误，并强约束目录的 "/" 结尾
        // ====================================================================
        private string BuildFullUrl(string serverUrl, string remotePath, bool isDirectory)
        {
            if (string.IsNullOrWhiteSpace(remotePath) || remotePath == "/")
                return serverUrl.TrimEnd('/') + "/";

            if (remotePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                remotePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return remotePath;

            string fullUrl;
            var baseUri = new Uri(serverUrl);

            if (remotePath.StartsWith("/"))
            {
                // WebDAV 的 Href 返回的通常是域名后的绝对路径，比如 "/dav/我的坚果云/"
                // 使用 Authority (如 https://dav.jianguoyun.com) 直接拼接，避免变成 .../dav/dav/...
                fullUrl = baseUri.GetLeftPart(UriPartial.Authority) + remotePath;
            }
            else
            {
                fullUrl = serverUrl.TrimEnd('/') + "/" + remotePath;
            }

            // 强制目录以 "/" 结尾，防止坚果云报 409 冲突 或 301 重定向失败
            if (isDirectory && !fullUrl.EndsWith("/"))
            {
                fullUrl += "/";
            }

            return fullUrl;
        }

        public async Task<bool> TestConnectionAsync(string serverUrl, string username, string password)
        {
            try
            {
                SetBasicAuth(username, password);
                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), serverUrl.TrimEnd('/') + "/");
                request.Headers.Add("Depth", "0");

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MultiStatus;
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"WebDAV 连接测试失败: {ex.Message}");
                return false;
            }
        }

        public async Task<List<WebDavFileNode>> ListFilesAsync(string serverUrl, string username, string password, string directoryPath)
        {
            var resultList = new List<WebDavFileNode>();
            try
            {
                SetBasicAuth(username, password);

                // 使用修复后的 URL 拼接
                string fullUrl = BuildFullUrl(serverUrl, directoryPath, isDirectory: true);

                var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), fullUrl);
                request.Headers.Add("Depth", "1");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                string xmlContent = await response.Content.ReadAsStringAsync();

                XNamespace d = "DAV:";
                var xdoc = XDocument.Parse(xmlContent);
                string currentAbsolutePath = new Uri(fullUrl).AbsolutePath.TrimEnd('/');

                foreach (var responseNode in xdoc.Descendants(d + "response"))
                {
                    var href = responseNode.Element(d + "href")?.Value;
                    var propstat = responseNode.Element(d + "propstat");
                    var prop = propstat?.Element(d + "prop");

                    if (string.IsNullOrEmpty(href) || prop == null) continue;

                    // 排除自己（只拿子文件）：加入中文 URL 编码解码对比，防止误判
                    if (Uri.UnescapeDataString(href).TrimEnd('/') == Uri.UnescapeDataString(currentAbsolutePath)) continue;

                    var isCol = prop.Element(d + "resourcetype")?.Element(d + "collection") != null;
                    var nameNode = prop.Element(d + "displayname");
                    string displayName = nameNode != null ? nameNode.Value : Uri.UnescapeDataString(href.TrimEnd('/').Split('/').Last());

                    long.TryParse(prop.Element(d + "getcontentlength")?.Value, out long contentLength);

                    resultList.Add(new WebDavFileNode
                    {
                        Href = href,
                        DisplayName = displayName,
                        IsDirectory = isCol,
                        ContentLength = contentLength
                    });
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"获取 WebDAV 列表失败: {ex.Message}");
                throw;
            }
            return resultList;
        }

        public async Task<bool> DownloadFileAsync(string serverUrl, string username, string password, string remoteFilePath, string localSavePath, IProgress<double>? progress = null)
        {
            try
            {
                SetBasicAuth(username, password);

                // 使用修复后的 URL 拼接 (下载的文件本身不需要 / 结尾)
                string fullUrl = BuildFullUrl(serverUrl, remoteFilePath, isDirectory: false);

                using var response = await _httpClient.GetAsync(fullUrl, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength;

                using var contentStream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(localSavePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                var buffer = new byte[8192];
                var isMoreToRead = true;
                long totalRead = 0;

                do
                {
                    var read = await contentStream.ReadAsync(buffer, 0, buffer.Length);
                    if (read == 0)
                    {
                        isMoreToRead = false;
                    }
                    else
                    {
                        await fileStream.WriteAsync(buffer, 0, read);
                        totalRead += read;

                        if (totalBytes.HasValue && progress != null)
                        {
                            progress.Report((double)totalRead / totalBytes.Value);
                        }
                    }
                } while (isMoreToRead);

                return true;
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"WebDAV 下载失败: {ex.Message}");
                if (File.Exists(localSavePath)) File.Delete(localSavePath);
                return false;
            }
        }
    }
}