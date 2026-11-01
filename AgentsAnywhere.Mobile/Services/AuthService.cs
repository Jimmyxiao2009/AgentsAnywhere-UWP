using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentsAnywhere.Mobile.Models;
using Newtonsoft.Json.Linq;
using Windows.Security.Authentication.Web;
using Windows.Security.Credentials;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
namespace AgentsAnywhere.Mobile.Services
{
 public sealed class AuthService {
  readonly ApiClient api; readonly PasswordVault vault = new PasswordVault();
  const string Resource = "AgentsAnywhere.UWP.Token.";
  public JObject User { get; private set; }
  public string SavedOrigin { get { return (string)ApplicationData.Current.LocalSettings.Values["ServerOrigin"] ?? ApiClient.OfficialOrigin; } }
  public AuthService(ApiClient client) { api = client; }
  public async Task<bool> RestoreAsync() {
   api.Configure(SavedOrigin);
   try { var c=vault.Retrieve(Resource+api.Origin,"access");c.RetrievePassword();api.Token=c.Password; }
   catch(Exception) {return false;}
   try {User=await api.GetAsync("/auth/me");return true;}
   catch(ApiException e) {if(e.Status==401){SignOut();return false;}throw;}
  }
  public async Task LoginAsync(string origin,string email,string password,bool persistCredential=true) {
   if(String.IsNullOrWhiteSpace(email)||String.IsNullOrEmpty(password))throw new ArgumentException("请输入邮箱和密码。");
   api.Configure(origin);
   DiagnosticLog.Write("auth.login begin protocol=password-verifier");
   var normalizedEmail=email.Trim().ToLowerInvariant();
   var saltReply=await api.PostAsync("/auth/password-salt",new JObject {["email"]=normalizedEmail},false);
   var salt=saltReply.Text("salt");
   if(String.IsNullOrEmpty(salt))throw new ApiException(400,"服务器未返回密码盐值，请查看调试日志。");
   var verifier=await Task.Run(()=>DerivePasswordVerifier(password,salt));
   DiagnosticLog.Write("auth.login verifier-derived");
   await AcceptAsync(await api.PostAsync("/auth/login",new JObject {["email"]=normalizedEmail,["passwordVerifier"]=verifier},false),persistCredential);
   DiagnosticLog.Write("auth.login success persisted="+persistCredential);
  }
  internal static string DerivePasswordVerifier(string password,string salt) {
   var provider=KeyDerivationAlgorithmProvider.OpenAlgorithm(KeyDerivationAlgorithmNames.Pbkdf2Sha256);
   var key=provider.CreateKey(CryptographicBuffer.ConvertStringToBinary(password,BinaryStringEncoding.Utf8));
   var parameters=KeyDerivationParameters.BuildForPbkdf2(CryptographicBuffer.ConvertStringToBinary(salt,BinaryStringEncoding.Utf8),120000);
   return UrlSafe(CryptographicBuffer.EncodeToBase64String(CryptographicEngine.DeriveKeyMaterial(key,parameters,32)));
  }
  public async Task LoginWithBrowserAsync(string origin) {
   api.Configure(origin);var verifier=RandomUrlSafe(32);var state=RandomUrlSafe(24);
   const string redirect="agents-anywhere://oauth/callback";
   var hash=HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256).HashData(CryptographicBuffer.ConvertStringToBinary(verifier,BinaryStringEncoding.Utf8));
   var challenge=UrlSafe(CryptographicBuffer.EncodeToBase64String(hash));
   var url=api.Origin+"/#/mobile-oauth?response_type=code&client_id=agents-anywhere-mobile&redirect_uri="+ApiClient.Escape(redirect)+"&code_challenge="+challenge+"&code_challenge_method=S256&scope=profile&state="+state;
   var result=await WebAuthenticationBroker.AuthenticateAsync(WebAuthenticationOptions.None,new Uri(url),new Uri(redirect));
   if(result.ResponseStatus==WebAuthenticationStatus.UserCancel)throw new OperationCanceledException("登录已取消。");
   if(result.ResponseStatus!=WebAuthenticationStatus.Success)throw new ApiException(400,"浏览器登录未完成，请使用邮箱登录。");
   var callback=new Uri(result.ResponseData);
   if(callback.Scheme!="agents-anywhere"||callback.Host!="oauth"||callback.AbsolutePath!="/callback")throw new ApiException(400,"登录回调无效。");
   var values=ParseQuery(callback.Query);string returnedState,code,error;
   if(!values.TryGetValue("state",out returnedState)||returnedState!=state)throw new ApiException(400,"登录验证失败。");
   if(values.TryGetValue("error",out error))throw new ApiException(400,"授权未完成: "+error);
   if(!values.TryGetValue("code",out code)||String.IsNullOrEmpty(code))throw new ApiException(400,"缺少授权码。");
   var token=await api.FormAsync("/oauth/token",new Dictionary<string,string>{["grant_type"]="authorization_code",["code"]=code,["client_id"]="agents-anywhere-mobile",["redirect_uri"]=redirect,["code_verifier"]=verifier});
   await AcceptAsync(new JObject {["accessToken"]=token.Text("access_token")});
  }
  async Task AcceptAsync(JObject reply,bool persistCredential=true) {
   var token=reply.Text("accessToken");if(String.IsNullOrWhiteSpace(token))throw new ApiException(400,"服务器未返回登录凭证。");
   api.Token=token;try {User=await api.GetAsync("/auth/me");}catch {api.Token=null;throw;}
   if(!persistCredential)return;
   RemoveSaved(api.Origin);vault.Add(new PasswordCredential(Resource+api.Origin,"access",token));
   ApplicationData.Current.LocalSettings.Values["ServerOrigin"]=api.Origin;
  }
  public void SignOut(){RemoveSaved(api.Origin);api.Token=null;User=null;}
  void RemoveSaved(string origin){try {foreach(var c in vault.FindAllByResource(Resource+origin))vault.Remove(c);}catch(Exception){}}
  static string UrlSafe(string value){return value.TrimEnd('=').Replace('+','-').Replace('/','_');}
  static string RandomUrlSafe(uint bytes){return UrlSafe(CryptographicBuffer.EncodeToBase64String(CryptographicBuffer.GenerateRandom(bytes)));}
  static Dictionary<string,string> ParseQuery(string query){return query.TrimStart('?').Split('&').Where(x=>x.Contains("=")).Select(x=>x.Split(new[]{'='},2)).ToDictionary(x=>Uri.UnescapeDataString(x[0]),x=>Uri.UnescapeDataString(x[1].Replace("+"," ")));}
 }
}
