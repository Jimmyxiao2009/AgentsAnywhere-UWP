using AgentsAnywhere.Mobile.Models;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace AgentsAnywhere.Mobile.Services
{
 public sealed class ApiException : Exception {
  public int Status { get; private set; }
  public string Operation { get; private set; }
  public ApiException(int status, string message, string operation=null) : base(message) { Status = status; Operation=operation; }
 }
 public sealed class ApiClient : IDisposable {
  public const string OfficialOrigin = "https://web.agents-anywhere.com";
  readonly HttpClient http;
  public string Origin { get; private set; } = OfficialOrigin;
  public string Token { get; set; }
  public event Action Unauthorized;
  public event Action<string> Trace;
  public ApiClient(HttpMessageHandler handler = null) {
   http = handler == null ? new HttpClient() : new HttpClient(handler);
   http.Timeout = TimeSpan.FromSeconds(80);
  }
  public static string NormalizeOrigin(string input) {
   Uri u; var value = (input ?? "").Trim().TrimEnd('/');
   if (!value.Contains("://")) value = "https://" + value;
   if (!Uri.TryCreate(value, UriKind.Absolute, out u) || (u.Scheme != "https" && u.Scheme != "http") || String.IsNullOrEmpty(u.Host) || !String.IsNullOrEmpty(u.UserInfo) || !String.IsNullOrEmpty(u.Query) || !String.IsNullOrEmpty(u.Fragment) || (u.AbsolutePath != "/" && u.AbsolutePath != "/api/v2"))
    throw new ArgumentException("请输入服务器地址，例如 https://web.agents-anywhere.com");
   if (u.Scheme == "http" && !IsLocalHost(u.Host)) throw new ArgumentException("公网服务器必须使用 HTTPS。");
   return Authority(u);
  }
  static string Authority(Uri uri) { return uri.Scheme + "://" + uri.Authority; }
  static bool IsLocalHost(string host) {
   if (host == "localhost" || host == "::1" || host == "[::1]" || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)) return true;
   var p = host.Split('.'); int a,b,c,d;
   return p.Length == 4 && int.TryParse(p[0], out a) && int.TryParse(p[1], out b) && int.TryParse(p[2], out c) && int.TryParse(p[3], out d)
    && a>=0 && a<=255 && b>=0 && b<=255 && c>=0 && c<=255 && d>=0 && d<=255 && (a==127 || a==10 || a==192 && b==168 || a==172 && b>=16 && b<=31);
  }
  public void Configure(string origin) { Origin = NormalizeOrigin(origin); Token = null; }
  public Uri Url(string path) {
   if (String.IsNullOrEmpty(path) || !path.StartsWith("/") || path.StartsWith("//")) throw new ArgumentException("Invalid API path.");
   return new Uri(Origin + (path.StartsWith("/api/v2/") ? path : "/api/v2" + path));
  }
  public static string Escape(string value) { return Uri.EscapeDataString(value ?? ""); }
  public Uri SocketUrl(string path) { var b = new UriBuilder(Url(path)) { Scheme = Origin.StartsWith("https:") ? "wss" : "ws" }; return b.Uri; }
  public async Task<JObject> GetAsync(string path, CancellationToken ct = default(CancellationToken)) { return await RequestAsync("GET", path, null, true, ct); }
  public async Task<JObject> PostAsync(string path, JToken body, bool auth = true, CancellationToken ct = default(CancellationToken)) { return await RequestAsync("POST", path, body, auth, ct); }
  public async Task<JObject> RequestAsync(string method, string path, JToken body = null, bool auth = true, CancellationToken ct = default(CancellationToken)) {
   using (var request = new HttpRequestMessage(new HttpMethod(method), Url(path))) {
    if (body != null) request.Content = new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
    return await SendAsync(request, auth, ct);
   }
  }
  public async Task<JObject> FormAsync(string path, System.Collections.Generic.Dictionary<string,string> values, CancellationToken ct = default(CancellationToken)) {
   using (var r = new HttpRequestMessage(HttpMethod.Post, Url(path))) { r.Content = new FormUrlEncodedContent(values); return await SendAsync(r, false, ct); }
  }
  public async Task<JObject> UploadAsync(string path, string name, byte[] data, string mediaType, CancellationToken ct = default(CancellationToken)) {
   using (var r = new HttpRequestMessage(HttpMethod.Post, Url(path))) {
    var multi = new MultipartFormDataContent(); var part = new ByteArrayContent(data);
    part.Headers.ContentType = new MediaTypeHeaderValue(String.IsNullOrEmpty(mediaType) ? "application/octet-stream" : mediaType);
    multi.Add(part, "files", name); r.Content = multi; return await SendAsync(r, true, ct);
   }
  }
  async Task<JObject> SendAsync(HttpRequestMessage r, bool auth, CancellationToken ct) {
   var sentToken = Token;
   var operation=r.RequestUri.AbsolutePath;
   var started=System.Diagnostics.Stopwatch.StartNew();
   Trace?.Invoke("http.begin "+r.Method+" "+operation+" authenticated="+(auth&&!String.IsNullOrEmpty(sentToken)));
   if (auth && !String.IsNullOrEmpty(sentToken)) r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", sentToken);
   try {
   using (var response = await http.SendAsync(r, ct)) {
    Trace?.Invoke("http.end "+r.Method+" "+operation+" status="+(int)response.StatusCode+" elapsedMs="+started.ElapsedMilliseconds);
    var text = await response.Content.ReadAsStringAsync();
    JObject obj; try { obj = String.IsNullOrWhiteSpace(text) ? new JObject() : JObject.Parse(text); } catch (JsonException) {
     throw new ApiException((int)response.StatusCode, "服务器返回了无法识别的内容，请检查地址和服务状态。",operation);
    }
    if (!response.IsSuccessStatusCode) {
     if ((int)response.StatusCode == 401 && auth && !String.IsNullOrEmpty(sentToken) && Token == sentToken) Unauthorized?.Invoke();
     var detail = obj["detail"]; var message = detail is JObject ? detail.Text("message", detail.Text("code")) : (detail is JValue ? (string)detail : null);
     if (String.IsNullOrWhiteSpace(message)) message = obj.Text("message", "请求失败 (" + (int)response.StatusCode + ")");
     throw new ApiException((int)response.StatusCode, message,operation);
    }
    EnsureOk(obj); return obj;
   }
   } catch(Exception e) {
    Trace?.Invoke("http.failure "+r.Method+" "+operation+" exception="+e.GetType().Name+" hresult=0x"+e.HResult.ToString("X8")+
     (e.InnerException==null ? "" : " inner="+e.InnerException.GetType().Name+" hresult=0x"+e.InnerException.HResult.ToString("X8")));
    throw;
   }
  }
  // The real server forwards a connector-side "file not found" as HTTP 502 with that text in the
  // detail, while the fixture uses 404. Both mean "target does not exist"; anything else (a genuine
  // timeout, permission or upstream failure) must keep its own meaning.
  public static bool IsMissingFileError(ApiException ex) {
   if (ex == null) return false;
   if (ex.Status == 404) return true;
   if (ex.Status < 500 || ex.Status > 599) return false;
   var detail = ex.Message ?? "";
   return detail.IndexOf("not found", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("no such file", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("不存在", StringComparison.OrdinalIgnoreCase) >= 0;
  }
  // The cloud rejects a message while the session's turn is still running with a 409 whose detail
  // is the bare code "session-lock" (the runtime already owns that session). Shown raw it tells
  // the user nothing; it means "wait for the current turn, or steer into it".
  public static bool IsSessionLockedError(ApiException ex) {
   if (ex == null || ex.Status != 409) return false;
   var detail = ex.Message ?? "";
   return detail.IndexOf("session-lock", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("session lock", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("session_lock", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("turn_active", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("turn_already_running", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("already has an active turn", StringComparison.OrdinalIgnoreCase) >= 0
    || detail.IndexOf("already running", StringComparison.OrdinalIgnoreCase) >= 0;
  }
  public static void EnsureOk(JObject value) { if (value["ok"] != null && !value.Flag("ok")) throw new ApiException(409, value["error"].Text("message", value.Text("message", "设备未完成此操作"))); }
  public async Task<byte[]> DownloadAsync(string relativeUrl, CancellationToken ct = default(CancellationToken)) {
   Uri url; if (String.IsNullOrWhiteSpace(relativeUrl) || !Uri.TryCreate(new Uri(Origin), relativeUrl, out url) || Authority(url) != Origin) throw new ApiException(400, "服务器返回了无效下载地址。");
   using (var request = new HttpRequestMessage(HttpMethod.Get, url)) {
    if (!String.IsNullOrEmpty(Token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
    using (var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)) {
     response.EnsureSuccessStatusCode();
     const int max = 25 * 1024 * 1024;
     if (response.Content.Headers.ContentLength > max) throw new ApiException(413, "此设备最多下载 25 MB 文件。");
     using (var input = await response.Content.ReadAsStreamAsync())
     using (var output = new System.IO.MemoryStream()) {
      var buffer = new byte[32768]; int count; while ((count = await input.ReadAsync(buffer, 0, buffer.Length, ct)) > 0) {
       if (output.Length + count > max) throw new ApiException(413,"此设备最多下载 25 MB 文件。");
       output.Write(buffer, 0, count);
      }
      return output.ToArray();
     }
    }
   }
  }
  public void Dispose() { http.Dispose(); }
 }
}
