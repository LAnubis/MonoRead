using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using MonoRead.Core.Entities;
using MonoRead.Core.Interfaces;
using MonoRead.Infrastructure.Logging;
using System.Text;
using System.Web;

namespace MonoRead.Infrastructure.Services
{
    public class HtmlAgilityPackSearchEngine : IBookSearchEngine
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<HtmlAgilityPackSearchEngine> _logger;

        public HtmlAgilityPackSearchEngine(ILogger<HtmlAgilityPackSearchEngine> logger)
        {
            _logger = logger;

            var handler = new HttpClientHandler
            {
                AllowAutoRedirect = false,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate | System.Net.DecompressionMethods.Brotli
            };
            _httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Upgrade-Insecure-Requests", "1");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Dest", "document");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Mode", "navigate");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-Site", "none");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Sec-Fetch-User", "?1");
            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation("Connection", "keep-alive");

            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        }

        public async Task<List<Book>> SearchBooksAsync(BookSourceRuleModel rule, string keyword)
        {
            var resultList = new List<Book>();
            string targetUrl = string.Empty;

            try
            {
                string charset = string.IsNullOrWhiteSpace(rule.Charset) ? "utf-8" : rule.Charset.ToLower();
                Encoding encoding = Encoding.GetEncoding(charset == "gbk" ? "gbk" : "utf-8");

                string encodedKeyword = HttpUtility.UrlEncode(keyword, encoding);
                string fullSearchStr = rule.SearchUrl.Replace("{key}", encodedKeyword);

                HttpResponseMessage response;
                string postBody = string.Empty;
                bool isPost = false;

                // =========================================================
                // 第一阶段：发送初始请求
                // =========================================================
                if (fullSearchStr.Contains('|'))
                {
                    var parts = fullSearchStr.Split('|');
                    targetUrl = parts[0];
                    postBody = parts[1];
                    isPost = true;

                    var request = new HttpRequestMessage(HttpMethod.Post, targetUrl);
                    request.Content = new StringContent(postBody, encoding, "application/x-www-form-urlencoded");
                    request.Headers.Add("Referer", rule.BaseUrl.TrimEnd('/') + "/");
                    request.Headers.TryAddWithoutValidation("Origin", rule.BaseUrl.TrimEnd('/'));

                    response = await _httpClient.SendAsync(request);
                }
                else
                {
                    targetUrl = fullSearchStr;
                    var request = new HttpRequestMessage(HttpMethod.Get, targetUrl);
                    request.Headers.Add("Referer", rule.BaseUrl.TrimEnd('/') + "/");
                    response = await _httpClient.SendAsync(request);
                }

                // =========================================================
                // 第二阶段：【架构师终极防御】统一智能重定向拦截
                // =========================================================
                int statusCode = (int)response.StatusCode;
                if (statusCode == 301 || statusCode == 302 || statusCode == 303 || statusCode == 307 || statusCode == 308)
                {
                    var redirectUrl = response.Headers.Location?.ToString();
                    if (!string.IsNullOrEmpty(redirectUrl))
                    {
                        if (!redirectUrl.StartsWith("http"))
                            redirectUrl = new Uri(new Uri(targetUrl), redirectUrl).ToString();

                        LocalLogger.LogInfo($"[引擎侦测] 检测到智能重定向！目标转移至: {redirectUrl}");
                        targetUrl = redirectUrl;

                        // 【核心智能换挡】：结合了国际规范和爬虫实战经验
                        if ((statusCode == 307 || statusCode == 308) && isPost)
                        {
                            // 307/308 且原本是 POST：强制保持 POST 阵型追杀 (对付 69书吧)
                            var redirectReq = new HttpRequestMessage(HttpMethod.Post, redirectUrl);
                            redirectReq.Content = new StringContent(postBody, encoding, "application/x-www-form-urlencoded");
                            redirectReq.Headers.Add("Referer", rule.BaseUrl.TrimEnd('/') + "/");
                            redirectReq.Headers.TryAddWithoutValidation("Origin", rule.BaseUrl.TrimEnd('/'));
                            response = await _httpClient.SendAsync(redirectReq);
                        }
                        else
                        {
                            // 301/302/303：无论之前是啥，立刻降级为纯净的 GET 请求 (对付 呱树阁 PRG 模式)
                            var redirectReq = new HttpRequestMessage(HttpMethod.Get, redirectUrl);
                            redirectReq.Headers.Add("Referer", rule.BaseUrl.TrimEnd('/') + "/");
                            response = await _httpClient.SendAsync(redirectReq);
                        }
                    }
                }

                // 第三阶段：校验最终状态并获取数据
                response.EnsureSuccessStatusCode();

                var htmlBytes = await response.Content.ReadAsByteArrayAsync();
                string html = encoding.GetString(htmlBytes).Trim();
                LocalLogger.LogInfo("抓取到的源码 (前2000字): \n" + (html.Length > 2000 ? html.Substring(0, 2000) : html));

                // =========================================================
                // 【双模态引擎】：JSON 格式拦截解析
                // =========================================================
                if (html.StartsWith("[") || html.StartsWith("{"))
                {
                    try
                    {
                        using var jsonDoc = System.Text.Json.JsonDocument.Parse(html);
                        var root = jsonDoc.RootElement;

                        System.Text.Json.JsonElement listElement = root;
                        if (root.ValueKind == System.Text.Json.JsonValueKind.Object)
                        {
                            if (root.TryGetProperty("data", out var dataProp)) listElement = dataProp;
                            else if (root.TryGetProperty("list", out var listProp)) listElement = listProp;
                        }

                        if (listElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            foreach (var item in listElement.EnumerateArray())
                            {
                                var book = new Book
                                {
                                    Id = Guid.NewGuid(),
                                    Title = GetJsonValue(item, rule.RuleSearch.Name),
                                    Author = GetJsonValue(item, rule.RuleSearch.Author),
                                    CoverUrl = FormatUrl(rule.BaseUrl, GetJsonValue(item, rule.RuleSearch.CoverUrl)),
                                    FileHash = FormatUrl(rule.BaseUrl, GetJsonValue(item, rule.RuleSearch.DetailUrl)),
                                    Description = GetJsonValue(item, rule.RuleSearch.Description),
                                    CreatedAt = DateTime.UtcNow
                                };
                                if (!string.IsNullOrWhiteSpace(book.Title)) resultList.Add(book);
                            }
                        }
                        return resultList;
                    }
                    catch (Exception jsonEx)
                    {
                        LocalLogger.LogError("JSON 引擎解析失败", jsonEx);
                    }
                }

                // =========================================================
                // HTML 格式传统解析
                // =========================================================
                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // 【新增防御机制】：如果网站返回了乱码或 "1" 导致跌落到 HTML 解析，
                // 但我们的规则是纯 JSON 规则（XPath为空），直接拦截，防止 XPathException 崩溃！
                if (string.IsNullOrWhiteSpace(rule.RuleSearch.BookList))
                {
                    LocalLogger.LogError($"网站未返回有效 JSON，且当前规则未配置 HTML XPath，判定为无搜索结果 | 返回内容: {html}");
                    return resultList;
                }

                var nodes = doc.DocumentNode.SelectNodes(rule.RuleSearch.BookList);

                if (nodes == null || nodes.Count == 0)
                {
                    LocalLogger.LogError($"引擎未找到节点 | 书源:{rule.SourceName} | XPath: {rule.RuleSearch.BookList} | 可能是网站改版或无结果");
                    return resultList;
                }

                foreach (var node in nodes)
                {
                    string name = ExtractText(node, rule.RuleSearch.Name);
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var book = new Book
                    {
                        Id = Guid.NewGuid(),
                        Title = name,
                        Author = ExtractText(node, rule.RuleSearch.Author),
                        CoverUrl = FormatUrl(rule.BaseUrl, ExtractAttribute(node, rule.RuleSearch.CoverUrl)),
                        FileHash = FormatUrl(rule.BaseUrl, ExtractAttribute(node, rule.RuleSearch.DetailUrl)),
                        Description = ExtractText(node, rule.RuleSearch.Description),
                        CreatedAt = DateTime.UtcNow
                    };

                    resultList.Add(book);
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"【搜书引擎底盘崩溃】 | 书源:{rule.SourceName} | XPath: {rule.RuleSearch.BookList} | 关键词: {keyword} | 尝试访问: {targetUrl} |", ex);

                //resultList.Add(new Book
                //{
                //    Id = Guid.NewGuid(),
                //    Title = "⚠️ 引擎底层报错 (已写入日志)",
                //    Author = "请导出日志查看",
                //    Description = $"【死因】：{ex.Message}",
                //    CoverUrl = ""
                //});
            }

            return resultList;
        }

        private string GetJsonValue(System.Text.Json.JsonElement element, string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return "";

            string cleanKey = key.Replace(".//", "").Replace("//", "").Replace("/text()", "").Replace("/@src", "").Replace("/@href", "").Trim();

            if (element.TryGetProperty(cleanKey, out var prop))
            {
                return prop.ValueKind == System.Text.Json.JsonValueKind.String ? prop.GetString() : prop.GetRawText();
            }
            return "";
        }

        // =========================================================
        // 引擎能力扩展 3：从详情页提取真实的 TXT 下载地址 (支持 JS 动态链接拦截)
        // =========================================================
        public async Task<string> GetDownloadUrlAsync(RuleDetail ruleDetail, string detailUrl)
        {
            try
            {
                // 1. 防爬虫伪装请求详情页
                var request = new HttpRequestMessage(HttpMethod.Get, detailUrl);
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var htmlBytes = await response.Content.ReadAsByteArrayAsync();
                string html = System.Text.Encoding.UTF8.GetString(htmlBytes);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                string xpath = ruleDetail?.TxtDownloadUrl;
                if (string.IsNullOrWhiteSpace(xpath))
                {
                    xpath = "//a[contains(text(), 'Txt格式下载') or contains(text(), 'TXT下载')]/@href";
                }

                string rawUrl = string.Empty;
                var node = doc.DocumentNode.SelectSingleNode(xpath);
                if (node != null)
                {
                    rawUrl = node.GetAttributeValue("href", "");
                }

                // =========================================================
                // 【核心究极进化】：如果 XPath 找不到，说明遭遇了 JS 动态渲染防御！
                // 直接启动正则引擎，全页面扫描拦截类似 get_down_url(..., 'http://...txt') 的代码
                // =========================================================
                if (string.IsNullOrWhiteSpace(rawUrl))
                {
                    // 匹配 get_down_url(..., 'URL.txt', ...) 中的 URL
                    var regex = new System.Text.RegularExpressions.Regex(@"get_down_url\([^,]+,\s*['""]([^'""]+\.txt)['""]");
                    var match = regex.Match(html);
                    if (match.Success)
                    {
                        rawUrl = match.Groups[1].Value;
                        LocalLogger.LogInfo($"触发正则引擎：成功拦截 JS 动态生成的 TXT 链接: {rawUrl}");
                    }
                }

                if (!string.IsNullOrWhiteSpace(rawUrl))
                {
                    // 处理可能存在的相对路径问题
                    string absoluteUrl = rawUrl.StartsWith("http") ? rawUrl : new Uri(new Uri(detailUrl), rawUrl).ToString();

                    // 【核心修复】：解决中文文件名下载崩溃的问题！
                    return Uri.EscapeUriString(absoluteUrl);
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"抓取 TXT 下载地址失败: {detailUrl}", ex);
            }
            return string.Empty;
        }

        private string ExtractText(HtmlNode contextNode, string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return "未知";
            var node = contextNode.SelectSingleNode(xpath);
            return node != null ? HttpUtility.HtmlDecode(node.InnerText.Trim()) : "未知";
        }

        private string ExtractAttribute(HtmlNode contextNode, string xpath)
        {
            if (string.IsNullOrWhiteSpace(xpath)) return string.Empty;
            if (xpath.Contains("/@"))
            {
                var parts = xpath.Split("/@");
                var nodeXpath = parts[0];
                var attrName = parts[1];

                var node = contextNode.SelectSingleNode(nodeXpath);
                return node != null ? node.GetAttributeValue(attrName, string.Empty).Trim() : string.Empty;
            }
            return string.Empty;
        }

        private string FormatUrl(string baseUrl, string relativeUrl)
        {
            if (string.IsNullOrWhiteSpace(relativeUrl)) return string.Empty;

            if (relativeUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                return relativeUrl;

            if (relativeUrl.StartsWith("//"))
                return "https:" + relativeUrl;

            return baseUrl.TrimEnd('/') + "/" + relativeUrl.TrimStart('/');
        }

        // =========================================================
        // 引擎能力扩展 1：拉取小说全本目录 (自带智能跃迁 + 顶级伪装防封)
        // =========================================================
        public async Task<List<ChapterNode>> GetTocAsync(BookSourceRuleModel rule, string tocUrl)
        {
            var chapters = new List<ChapterNode>();
            try
            {
                // 【终极防御】：强制在此处注册 GBK 解码器，彻底斩杀 Parameter 'provider' 闪退 Bug！
                System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);

                string charset = string.IsNullOrWhiteSpace(rule.Charset) ? "utf-8" : rule.Charset.ToLower();
                Encoding encoding = Encoding.GetEncoding(charset == "gbk" ? "gbk" : "utf-8");

                // 定义一套极其逼真的浏览器指纹
                string fakeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

                var request = new HttpRequestMessage(HttpMethod.Get, tocUrl);
                request.Headers.Add("Referer", rule.BaseUrl.TrimEnd('/') + "/");
                request.Headers.Add("User-Agent", fakeUserAgent);

                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var htmlBytes = await response.Content.ReadAsByteArrayAsync();
                string html = encoding.GetString(htmlBytes);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                // =========================================================
                // 智能目录跃迁 (Smart Hop)
                // =========================================================
                var readOnlineNode = doc.DocumentNode.SelectSingleNode("//a[contains(text(), '在线阅读') or contains(text(), '点击阅读') or contains(text(), '开始阅读')]");
                if (readOnlineNode != null)
                {
                    string nextHref = readOnlineNode.GetAttributeValue("href", "");
                    if (!string.IsNullOrWhiteSpace(nextHref) && nextHref != "#" && !nextHref.ToLower().StartsWith("javascript"))
                    {
                        string realTocUrl = nextHref.StartsWith("http") ? nextHref : new Uri(new Uri(tocUrl), nextHref).ToString();
                        LocalLogger.LogInfo($"触发智能跃迁：从详情页跳转至真实目录页 {realTocUrl}");

                        // 【核心修复】：为第二次跃迁请求穿上完整的顶级伪装服，骗过 502 防火墙！
                        var nextRequest = new HttpRequestMessage(HttpMethod.Get, realTocUrl);
                        nextRequest.Headers.Add("Referer", tocUrl);
                        nextRequest.Headers.Add("User-Agent", fakeUserAgent);

                        var nextResponse = await _httpClient.SendAsync(nextRequest);
                        nextResponse.EnsureSuccessStatusCode();

                        var nextHtmlBytes = await nextResponse.Content.ReadAsByteArrayAsync();
                        html = encoding.GetString(nextHtmlBytes);
                        doc.LoadHtml(html);

                        tocUrl = realTocUrl;
                    }
                }

                // =========================================================
                // 提取目录链接
                // =========================================================
                var nodes = doc.DocumentNode.SelectNodes(rule.RuleToc.ChapterList);
                if (nodes == null || nodes.Count == 0)
                {
                    LocalLogger.LogError($"引擎未能抓取到目录节点，XPath: {rule.RuleToc.ChapterList}");
                    return chapters;
                }

                foreach (var node in nodes)
                {
                    string name = ExtractText(node, rule.RuleToc.ChapterName);
                    string rawUrl = ExtractAttribute(node, rule.RuleToc.ChapterUrl);

                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(rawUrl)) continue;

                    string absoluteUrl = rawUrl.StartsWith("http") ? rawUrl : new Uri(new Uri(tocUrl), rawUrl).ToString();

                    chapters.Add(new ChapterNode
                    {
                        Title = name,
                        Url = absoluteUrl
                    });
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"拉取目录失败: {tocUrl}", ex);
            }
            return chapters;
        }
        // =========================================================
        // 引擎能力扩展 2：拉取并净化小说单章正文
        // =========================================================
        public async Task<string> GetChapterContentAsync(BookSourceRuleModel rule, string chapterUrl)
        {
            try
            {
                string charset = string.IsNullOrWhiteSpace(rule.Charset) ? "utf-8" : rule.Charset.ToLower();
                Encoding encoding = Encoding.GetEncoding(charset == "gbk" ? "gbk" : "utf-8");

                var request = new HttpRequestMessage(HttpMethod.Get, chapterUrl);
                request.Headers.Add("Referer", rule.BaseUrl.TrimEnd('/') + "/");
                var response = await _httpClient.SendAsync(request);
                response.EnsureSuccessStatusCode();

                var htmlBytes = await response.Content.ReadAsByteArrayAsync();
                string html = encoding.GetString(htmlBytes);

                var doc = new HtmlDocument();
                doc.LoadHtml(html);

                var node = doc.DocumentNode.SelectSingleNode(rule.RuleContent.Content);
                if (node != null)
                {
                    // 【极速排版引擎】：将无序的 HTML 标签 (<br>, <p>) 转化为纯文本换行
                    string htmlContent = node.InnerHtml;
                    htmlContent = htmlContent.Replace("<br>", "\n")
                                           .Replace("<br/>", "\n")
                                           .Replace("<br />", "\n")
                                           .Replace("</p>", "\n")
                                           .Replace("<p>", "");

                    // 用替身 Document 进行剥离，防止污染原树
                    var tempDoc = new HtmlDocument();
                    tempDoc.LoadHtml(htmlContent);

                    // 获取纯文本并解码 (将 &nbsp; 变回空格)
                    string rawText = System.Net.WebUtility.HtmlDecode(tempDoc.DocumentNode.InnerText);

                    // 清洗脏数据：剔除多余的连续换行和行首尾空白
                    var lines = rawText.Split('\n')
                                       .Select(l => l.Trim())
                                       .Where(l => !string.IsNullOrWhiteSpace(l));

                    return string.Join("\n\n", lines);
                }
            }
            catch (Exception ex)
            {
                LocalLogger.LogError($"拉取正文失败: {chapterUrl}", ex);
            }
            return "【正文拉取失败，请检查网络或源规则】";
        }
    }
}