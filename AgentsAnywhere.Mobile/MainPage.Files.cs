using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AgentsAnywhere.Mobile.Models;
using AgentsAnywhere.Mobile.Services;
using Newtonsoft.Json.Linq;
using Windows.Security.Cryptography;
using Windows.Security.Cryptography.Core;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace AgentsAnywhere.Mobile
{
 public sealed class RemoteFileEntry {
  public JObject Data{get;}
  public string Name{get{return Data.Text("name","未命名");}}
  public string Path{get{return Data.Text("path");}}
  public bool IsDirectory{get{return Data.Text("type")=="directory"||Data.Text("type")=="dir";}}
  public string Subtitle{get{return IsDirectory?"文件夹":Size(Data.Number("size"))+(Data.Text("modifiedAt").Length>0?" · "+Data.Text("modifiedAt").Replace("T"," ").Split('.')[0].TrimEnd('Z'):"");}}
  public string Glyph{get{return IsDirectory?"\uE8B7":"\uE8A5";}}
  public Windows.UI.Xaml.Media.Brush GlyphBrush{get{return Ui.Res.Brush(IsDirectory?"AccentTextBrush":"SoftTextBrush");}}
  public static string Size(long bytes){return bytes<1024?bytes+" B":bytes<1048576?(bytes/1024.0).ToString("0.#")+" KB":(bytes/1048576.0).ToString("0.#")+" MB";}
  public RemoteFileEntry(JObject data){Data=data;}
 }
 public sealed partial class MainPage
 {
  string previewPath,previewHash,filePath=".";
  string FileRoot{get{return projects.FirstOrDefault(x=>x.Id==selected?.ProjectId)?.Workspace??selected?.Workspace??"";}}
  string ConnectorPath{get{return "/connectors/"+ApiClient.Escape(selected?.ConnectorId);}}
  string FilePath {
   get{return filePath;}
   set {
    filePath=String.IsNullOrEmpty(value)?".":value;
    var root=FileRoot.TrimEnd('/','\\');var shown=filePath;
    if(root.Length>0 && shown.StartsWith(root,StringComparison.OrdinalIgnoreCase))shown=shown.Substring(root.Length).TrimStart('/','\\');
    FileCrumb.Text=shown=="."||shown.Length==0?"工作目录":"工作目录 › "+shown.Replace("\\","/").Replace("/"," › ");
   }
  }
  bool AtFileRoot{get{var path=FilePath.TrimEnd('/','\\');return path=="."||path==FileRoot.TrimEnd('/','\\')||path.Length==0;}}
  void ResetFiles(){FilePath=".";FileList.ItemsSource=null;FileEmpty.Visibility=Visibility.Collapsed;FileEditor.Text="";FileEditor.IsReadOnly=true;FilePreviewLabel.Text="";FileViewName.Text="";previewPath=previewHash=null;SaveFileButton.IsEnabled=DownloadFileButton.IsEnabled=false;}
  async void Tab_Changed(object sender,SelectionChangedEventArgs e){
   if(!ready||e.OriginalSource!=Tabs)return;
   if(IsDraft && Tabs.SelectedIndex!=0){Tabs.SelectedIndex=0;return;}
   UpdateSessionBar();
   if(!authenticated||selected==null)return;
   if(Tabs.SelectedIndex==1 && FileList.ItemsSource==null)await ExecuteAsync(()=>BrowseFilesAsync());
  }
  async Task BrowseFilesAsync() {
   RequireSession();if(String.IsNullOrWhiteSpace(FileRoot))throw new InvalidOperationException("此会话没有工作目录。");
   var id=selected.Id;var ct=sessionLife.Token;var path=FilePath;
   var reply=await api.PostAsync(ConnectorPath+"/fs/list",new JObject {["root"]=FileRoot,["path"]=path},true,ct);
   ct.ThrowIfCancellationRequested();if(selected?.Id!=id)return;
   var result=reply["result"] as JObject??new JObject();
   var entries=result.Objects("entries").Select(x=>new RemoteFileEntry(x)).OrderByDescending(x=>x.IsDirectory).ThenBy(x=>x.Name).ToList();
   FileList.ItemsSource=entries;FileEmpty.Text="这个文件夹是空的。";FileEmpty.Visibility=entries.Count==0?Visibility.Visible:Visibility.Collapsed;
   FilePath=result.Text("path",path);
   if(result.Flag("truncated"))StatusText.Text="目录内容较多，结果已截断。";
  }
  async void GoToPath_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var value=await PromptAsync("转到路径","工作目录内的相对路径或完整路径",FilePath);if(value==null)return;
   FilePath=value.Trim();await BrowseFilesAsync();
  });}
  async void ParentFiles_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var path=FilePath.TrimEnd('/','\\');var root=FileRoot.TrimEnd('/','\\');
   if(path=="."||path==root)FilePath=".";
   else{var index=Math.Max(path.LastIndexOf('/'),path.LastIndexOf('\\'));FilePath=index<=0?".":path.Substring(0,index);}
   await BrowseFilesAsync();
  });}
  async void File_Click(object sender,ItemClickEventArgs e){await ExecuteAsync(async()=> {
   var entry=e.ClickedItem as RemoteFileEntry;if(entry==null)return;
   if(entry.IsDirectory){FilePath=entry.Path;await BrowseFilesAsync();return;}
   await PreviewFileAsync(entry);ShowView(AppView.File);
  });}
  async Task PreviewFileAsync(RemoteFileEntry entry) {
   RequireSession();var id=selected.Id;var ct=sessionLife.Token;previewPath=previewHash=null;SaveFileButton.IsEnabled=false;
   var reply=await api.PostAsync(ConnectorPath+"/fs/readText?root="+ApiClient.Escape(FileRoot),new JObject {["path"]=entry.Path,["maxBytes"]=1048576},true,ct);
   ct.ThrowIfCancellationRequested();if(selected?.Id!=id)return;
   previewPath=entry.Path;previewHash=reply.Text("sha256");
   bool binary=reply.Flag("binary"),truncated=reply.Flag("truncated");
   FileEditor.Text=binary?"这是二进制文件，请使用下载功能查看。":reply.Text("content");
   FileEditor.IsReadOnly=binary||truncated||previewHash.Length!=64;
   FileViewName.Text=entry.Name;
   FilePreviewLabel.Text=RemoteFileEntry.Size(reply.Number("size"))+(truncated?" · 预览已截断，不能编辑":"")+(binary?" · 二进制文件":"")+(!binary&&!truncated&&FileEditor.IsReadOnly?" · 只读":"");
   SaveFileButton.IsEnabled=!FileEditor.IsReadOnly;DownloadFileButton.IsEnabled=true;
  }
  async void SaveFile_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();if(previewPath==null||previewHash==null||FileEditor.IsReadOnly)throw new InvalidOperationException("请先打开可编辑的完整文本文件。");
   var id=selected.Id;var ct=sessionLife.Token;var path=previewPath;
   await api.PostAsync(ConnectorPath+"/fs/write?root="+ApiClient.Escape(FileRoot),new JObject {["path"]=path,["content"]=FileEditor.Text,["encoding"]="utf8",["ifMatch"]=previewHash},true,ct);
   if(selected?.Id==id)await PreviewFileAsync(new RemoteFileEntry(new JObject {["name"]=FileName(path),["path"]=path}));
  },"文件已保存");}
  async void DownloadFile_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var path=previewPath;
   if(String.IsNullOrEmpty(path))throw new InvalidOperationException("请先选择一个文件。");
   await DownloadAndSaveAsync(path,sessionLife.Token);
  },"文件已下载");}
  async void FileDownload_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   var entry=(sender as FrameworkElement)?.Tag as RemoteFileEntry;if(entry==null||entry.IsDirectory)return;
   RequireSession();await DownloadAndSaveAsync(entry.Path,sessionLife.Token);
  },"文件已下载");}
  async Task DownloadAndSaveAsync(string path,System.Threading.CancellationToken ct) {
   var reply=await api.PostAsync(ConnectorPath+"/fs/read?root="+ApiClient.Escape(FileRoot),new JObject {["path"]=path},true,ct);
   var result=reply["result"] as JObject??new JObject();
   var bytes=await api.DownloadAsync(result.Text("downloadUrl"),ct);VerifyBytes(bytes,result.Number("size",-1),result.Text("sha256"));
   var picker=new FileSavePicker {SuggestedFileName=FileName(path)};picker.FileTypeChoices.Add("文件",new[]{FileExtension(path)});
   var target=await picker.PickSaveFileAsync();if(target==null)return;
   CachedFileManager.DeferUpdates(target);await FileIO.WriteBytesAsync(target,bytes);
   var status=await CachedFileManager.CompleteUpdatesAsync(target);
   if(status!=Windows.Storage.Provider.FileUpdateStatus.Complete)throw new InvalidOperationException("文件未能保存到所选位置。");
  }
  static void VerifyBytes(byte[] data,long size,string sha256) {
   if(size>=0&&size!=data.Length)throw new ApiException(400,"下载文件大小不一致，请重试。");
   if(!String.IsNullOrEmpty(sha256)) {
    var provider=HashAlgorithmProvider.OpenAlgorithm(HashAlgorithmNames.Sha256);
    var actual=CryptographicBuffer.EncodeToHexString(provider.HashData(CryptographicBuffer.CreateFromByteArray(data)));
    if(!String.Equals(actual,sha256,StringComparison.OrdinalIgnoreCase))throw new ApiException(400,"下载文件校验失败，请重试。");
   }
  }
  async void UploadText_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var id=selected.Id;var ct=sessionLife.Token;var connector=ConnectorPath;var root=FileRoot;var directory=FilePath;
   var picker=new FileOpenPicker();picker.FileTypeFilter.Add("*");var file=await picker.PickSingleFileAsync();if(file==null)return;
   var properties=await file.GetBasicPropertiesAsync();if(properties.Size>4*1024*1024)throw new ArgumentException("文本上传最多支持 4 MB，其他文件可以使用会话附件。");
   var buffer=await FileIO.ReadBufferAsync(file);byte[] bytes;CryptographicBuffer.CopyToByteArray(buffer,out bytes);
   string text;try{text=new UTF8Encoding(false,true).GetString(bytes,0,bytes.Length).TrimStart('\uFEFF');}catch(DecoderFallbackException){throw new ArgumentException("请选择 UTF-8 文本文件；二进制文件请使用会话附件。");}
   if(text.Contains("\0"))throw new ArgumentException("请选择文本文件；二进制文件请使用会话附件。");
   var destination=await PromptAsync("上传文本","目标路径",directory=="."?file.Name:directory.TrimEnd('/','\\')+"/"+file.Name);if(destination==null)return;
   ct.ThrowIfCancellationRequested();if(selected?.Id!=id)return;
   // Prevent silent overwrites: read and confirm an existing target, then use its hash.
   string existingHash=null;
   try{var existing=await api.PostAsync(connector+"/fs/readText?root="+ApiClient.Escape(root),new JObject {["path"]=destination,["maxBytes"]=1},true,ct);existingHash=existing.Text("sha256");}
   // A missing target is the normal case for a new upload: the fixture answers 404, the real
   // server answers 502 with a connector "file not found" detail. Neither means the Connector is down.
   catch(ApiException ex){if(!ApiClient.IsMissingFileError(ex))throw;}
   if(existingHash!=null) {
    var choice=await new ContentDialog {Title="替换现有文件？",Content=destination,PrimaryButtonText="替换",SecondaryButtonText="取消"}.ShowAsync();
    if(choice!=ContentDialogResult.Primary)return;
    if(existingHash.Length!=64)throw new InvalidOperationException("不能安全替换此文件，请使用其他路径。");
   }
   var body=new JObject {["path"]=destination,["content"]=text,["encoding"]="utf8"};if(existingHash!=null)body["ifMatch"]=existingHash;
   await api.PostAsync(connector+"/fs/write?root="+ApiClient.Escape(root),body,true,ct);if(selected?.Id==id)await BrowseFilesAsync();
  },"文本已上传");}
  static string FileName(string path){return path.Replace('\\','/').Split('/').Last();}
  static string FileExtension(string path){var name=FileName(path);var dot=name.LastIndexOf('.');return dot<0||dot==name.Length-1?".bin":name.Substring(dot);}
 }
}
