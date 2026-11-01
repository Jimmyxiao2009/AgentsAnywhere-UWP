using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AgentsAnywhere.Mobile.Models;
using AgentsAnywhere.Mobile.Services;
using Newtonsoft.Json.Linq;
using Windows.System;
using Windows.UI;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
namespace AgentsAnywhere.Mobile
{
 public sealed partial class MainPage
 {
  readonly List<JObject> attachments=new List<JObject>();
  bool sending;
  string pendingMessageId,pendingMessageText;
  async Task SendMessageAsync(bool steer=false) {
   if(sending)return;
   if(IsDraft){await CreateDraftSessionAsync();return;}
   RequireSession();
   var content=MessageBox.Text.Trim();
   if(content.Length==0 && attachments.Count==0)throw new ArgumentException("请输入任务或添加附件。");
   if(!timeline.Can(steer?"session.steer":"session.send_message"))throw new ApiException(409,"当前会话暂不支持此操作。");
   var id=selected.Id;var ct=sessionLife.Token;
   if(pendingMessageText!=content || pendingMessageId==null){pendingMessageId=Guid.NewGuid().ToString("N");pendingMessageText=content;}
   var body=new JObject {["content"]=content,["clientMessageId"]=pendingMessageId};
   if(attachments.Count>0)body["attachments"]=new JArray(attachments.Select(x=>new JObject {["fileId"]=x.Text("fileId")}));
   sending=true;SendButton.IsEnabled=false;SteerButton.IsEnabled=false;
   try {
    await api.PostAsync(SessionPath(id)+"/runtime/"+(steer?"steer":"messages"),body,true,ct);
    if(selected?.Id==id){MessageBox.Text="";attachments.Clear();UpdateAttachments();pendingMessageId=pendingMessageText=null;await RecoverAsync(id,ct);}
    StatusText.Text=steer?"指令已追加":"消息已发送";
   }finally{sending=false;UpdateCapabilities();}
  }
  async void Send_Click(object sender,RoutedEventArgs e){await ExecuteAsync(()=>SendMessageAsync());}
  async void Steer_Click(object sender,RoutedEventArgs e){await ExecuteAsync(()=>SendMessageAsync(true));}
  async void Interrupt_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=>{RequireSession();if(!timeline.Can("session.interrupt"))throw new ApiException(409,"当前会话不支持打断。");var id=selected.Id;var ct=sessionLife.Token;await api.PostAsync(SessionPath(id)+"/runtime/interrupt",new JObject(),true,ct);await RecoverAsync(id,ct);},"已请求打断");}
  void Message_KeyDown(object sender,KeyRoutedEventArgs e){if(e.Key==VirtualKey.Enter && (Window.Current.CoreWindow.GetKeyState(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0){e.Handled=true;Send_Click(sender,new RoutedEventArgs());}}
  void UpdateAttachments(){AttachmentList.ItemsSource=attachments.Select(x=>new AttachmentChip {Data=x}).ToList();AttachmentScroll.Visibility=attachments.Count>0?Visibility.Visible:Visibility.Collapsed;}
  void RemoveAttachment_Click(object sender,RoutedEventArgs e){var chip=(sender as FrameworkElement)?.Tag as AttachmentChip;if(chip==null)return;attachments.Remove(chip.Data);UpdateAttachments();}
  async void Attach_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   if(attachments.Count>=10)throw new ArgumentException("每条消息最多添加 10 个附件。");
   var picker=new Windows.Storage.Pickers.FileOpenPicker {SuggestedStartLocation=Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary};picker.FileTypeFilter.Add("*");
   var f=await picker.PickSingleFileAsync();if(f==null)return;
   var properties=await f.GetBasicPropertiesAsync();if(properties.Size==0 || properties.Size>25*1024*1024)throw new ArgumentException("请选择非空且小于 25 MB 的附件。");
   var buffer=await Windows.Storage.FileIO.ReadBufferAsync(f);byte[] bytes;Windows.Security.Cryptography.CryptographicBuffer.CopyToByteArray(buffer,out bytes);
   if(IsDraft){
    // A draft has no session id yet, so the file is kept locally and sent inline with create-and-start.
    attachments.Add(LocalAttachment(f.Name,f.ContentType,bytes));UpdateAttachments();
    return;
   }
   RequireSession();if(!timeline.Can("runtime.attachment"))throw new ApiException(409,"此 Runtime 不支持附件。");
   var id=selected.Id;var ct=sessionLife.Token;
   var response=await api.UploadAsync(SessionPath(id)+"/attachments",f.Name,bytes,f.ContentType,ct);ct.ThrowIfCancellationRequested();
   if(selected?.Id==id){attachments.AddRange(response.Objects("attachments"));UpdateAttachments();}
  },"附件已添加，发送消息后 Agent 即可使用");}
  // Inline attachment reference, the shape the web and Android clients send with create-and-start:
  // name, media type, size, SHA-256 and the base64 payload.
  static JObject LocalAttachment(string name,string mediaType,byte[] bytes){
   var hash=System.Security.Cryptography.SHA256.Create().ComputeHash(bytes);
   var hex=new System.Text.StringBuilder(hash.Length*2);
   foreach(var b in hash)hex.Append(b.ToString("x2"));
   return new JObject {["name"]=name,["mediaType"]=String.IsNullOrEmpty(mediaType)?"application/octet-stream":mediaType,["size"]=bytes.Length,["sha256"]=hex.ToString(),["contentBase64"]=Convert.ToBase64String(bytes)};
  }
  // create-and-start takes inline attachments; a plain message takes uploaded fileId references.
  static JObject InlineAttachment(JObject x){
   if(x.Text("contentBase64").Length==0)throw new InvalidOperationException("此附件还没有上传完成，请重新选择。");
   return new JObject {["fileId"]=x.Text("sha256"),["name"]=x.Text("name"),["mediaType"]=x.Text("mediaType","application/octet-stream"),["size"]=x.Number("size"),["sha256"]=x.Text("sha256"),["contentBase64"]=x.Text("contentBase64")};
  }
  async void NewProject_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   if(!authenticated)throw new InvalidOperationException("请先登录。");
   var available=devices.Where(x=>x.Online).ToList();if(available.Count==0)throw new InvalidOperationException("没有在线设备，请先在工作设备上登录官方桌面客户端。");
   var d=new ComboBox {Header="设备",DisplayMemberPath="Name",ItemsSource=available,HorizontalAlignment=HorizontalAlignment.Stretch};d.SelectedItem=available.FirstOrDefault(x=>x.Id==(DeviceBox.SelectedItem as Entity)?.Id)??available.First();
   var name=new TextBox {Header="项目名称",Margin=new Thickness(0,10,0,0),MaxLength=120};
   var root=new TextBox {Header="工作设备上的目录",PlaceholderText="例如 C:\\Projects\\MyApp 或 /home/me/myapp",Margin=new Thickness(0,10,0,0)};
   var createFolder=new CheckBox {Content="在设备上创建这个目录（含上级目录）",IsChecked=true,Margin=new Thickness(0,12,0,0)};
   var panel=new StackPanel();panel.Children.Add(d);panel.Children.Add(name);panel.Children.Add(root);panel.Children.Add(createFolder);
   var dialog=new ContentDialog {Title="新建项目",Content=new ScrollViewer {Content=panel,MaxHeight=440,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},PrimaryButtonText="创建",SecondaryButtonText="取消"};
   if(await dialog.ShowAsync()!=ContentDialogResult.Primary)return;
   if(String.IsNullOrWhiteSpace(name.Text)||String.IsNullOrWhiteSpace(root.Text))throw new ArgumentException("请填写项目名称和完整目录。");
   var connectorId=((Entity)d.SelectedItem).Id;var workspacePath=root.Text.Trim();
   if(createFolder.IsChecked==true)await CreateRemoteFolderAsync(connectorId,workspacePath);
   await api.PostAsync("/projects",new JObject {["name"]=name.Text.Trim(),["connectorId"]=connectorId,["workspacePath"]=workspacePath,["manuallyCreated"]=true});
   await LoadDashboardAsync();
  },"项目已创建");}
  // 服务端只登记项目路径，不会在设备上建目录；这里用 Connector 的 shell RPC 建目录。
  // Connector 要求 cwd 必须是已存在的目录，所以 root 传父目录、cwd 传 "."。
  async Task CreateRemoteFolderAsync(string connectorId,string workspacePath) {
   var path=workspacePath.Trim().TrimEnd('/','\\');
   var index=Math.Max(path.LastIndexOf('/'),path.LastIndexOf('\\'));
   if(index<0)throw new ArgumentException("请填写完整目录，例如 C:\\Projects\\MyApp 或 /home/me/myapp。");
   var parent=index==0?"/":path.Substring(0,index);
   var leaf=path.Substring(index+1);
   if(leaf.Length==0)throw new ArgumentException("请填写完整目录，例如 C:\\Projects\\MyApp 或 /home/me/myapp。");
   var windows=System.Text.RegularExpressions.Regex.IsMatch(path,"^[A-Za-z]:")||path.StartsWith("\\\\");
   if(windows&&System.Text.RegularExpressions.Regex.IsMatch(parent,"^[A-Za-z]:$"))parent+="\\";
   var command=windows?"if not exist \""+path+"\" mkdir \""+path+"\"":"mkdir -p \""+path+"\"";
   var payload=new JObject();payload["command"]=command;payload["cwd"]=".";payload["timeoutMs"]=30000;
   var reply=await api.PostAsync("/connectors/"+ApiClient.Escape(connectorId)+"/shell/exec?root="+ApiClient.Escape(parent),payload,true,sessionLife.Token);
   var result=reply["result"] as JObject??new JObject();
   var exit=result.Number("exitCode");
   if(exit!=0) {
    var message=result.Text("stderr").Trim();if(message.Length==0)message=result.Text("stdout").Trim();
    throw new InvalidOperationException("设备上创建目录失败："+(message.Length==0?"退出码 "+exit:message));
   }
  }
  // The new-session page picks project / agent / model first; the session itself is
  // only created when the first message is sent (see CreateDraftSessionAsync).
   // One place decides whether the new-conversation page can continue. The ComboBox selection
  // events fire more than once while their item lists are refilled, so the state is derived from
  // the current selections instead of whichever event happened to arrive last.
  void UpdateNewSessionStart() {
   NewSessionStartButton.IsEnabled=view==AppView.NewSession && NewProjectBox.SelectedItem is Entity && NewRuntimeBox.SelectedItem is Entity;
  }
  async void NewSession_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   if(!authenticated)throw new InvalidOperationException("请先登录。");
   var onlineIds=new HashSet<string>(devices.Where(x=>x.Online).Select(x=>x.Id));
   var list=projects.Where(x=>onlineIds.Contains(x.ConnectorId)).ToList();
   if(list.Count==0)throw new InvalidOperationException("请先创建项目，并确认工作设备在线。");
   NewSessionError.Text="";NewTitleBox.Text="";NewRuntimeBox.ItemsSource=null;NewModelBox.ItemsSource=null;UpdateNewSessionStart();
   NewProjectBox.ItemsSource=list;
   ShowView(AppView.NewSession);
   NewProjectBox.SelectedItem=list.FirstOrDefault(x=>x.Id==(ProjectBox.SelectedItem as Entity)?.Id)??list.First();
  });}
  async void NewProject_Changed(object sender,SelectionChangedEventArgs e){await ExecuteAsync(async()=> {
   if(view!=AppView.NewSession)return;
   NewRuntimeBox.ItemsSource=null;NewModelBox.ItemsSource=null;UpdateNewSessionStart();
   NewSessionError.Text="";NewSessionHint.Text="正在加载设备上的 Agent…";
   var project=NewProjectBox.SelectedItem as Entity;if(project==null)return;
   var reply=await api.GetAsync("/connectors/"+ApiClient.Escape(project.ConnectorId)+"/runtimes");
   if(view!=AppView.NewSession)return;
   var runtimes=reply.Objects("runtimes").Where(x=>x.Flag("present")&&x.Flag("configured")&&x.Flag("active")).Select(x=>new Entity(new JObject {["id"]=x.Text("runtimeId"),["name"]=x.Text("displayName",x.Text("runtimeId")),["runtime"]=x.Text("runtimeType",x.Text("runtimeId"))})).ToList();
   NewRuntimeBox.ItemsSource=runtimes;
   NewSessionHint.Text=runtimes.Count>0?"选择 Agent 和模型后进入对话页，发送第一条消息才会创建会话。":"此设备尚无可用 Agent，请在官方桌面客户端中完成配置。";
   NewRuntimeBox.SelectedItem=runtimes.FirstOrDefault();
   UpdateNewSessionStart();
  });}
  async void NewRuntime_Changed(object sender,SelectionChangedEventArgs e){await ExecuteAsync(async()=> {
   if(view!=AppView.NewSession)return;
   NewModelBox.ItemsSource=null;NewSessionError.Text="";
   var project=NewProjectBox.SelectedItem as Entity;var runtime=NewRuntimeBox.SelectedItem as Entity;
   if(project==null||runtime==null){UpdateNewSessionStart();return;}
   var reply=await api.GetAsync("/connectors/"+ApiClient.Escape(project.ConnectorId)+"/runtimes/"+ApiClient.Escape(runtime.Id)+"/catalogs/model");
   if(view!=AppView.NewSession)return;
   var models=(reply["catalog"]??reply).Objects("models").Select(ToOption).ToList();
   NewModelBox.ItemsSource=models;
   NewModelBox.SelectedItem=models.FirstOrDefault(x=>x.Data.Flag("default"))??models.FirstOrDefault();
   UpdateNewSessionStart();
  });}
  async void NewSessionStart_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   var project=NewProjectBox.SelectedItem as Entity;var runtime=NewRuntimeBox.SelectedItem as Entity;
   if(project==null||runtime==null)throw new ArgumentException("请选择项目和 Agent。");
   var body=new JObject {["connectorId"]=project.ConnectorId,["projectId"]=project.Id,["runtime"]=runtime.Data.Text("runtime",runtime.Id),["runtimeId"]=runtime.Id,["cwd"]=project.Workspace};
   if(!String.IsNullOrWhiteSpace(NewTitleBox.Text))body["title"]=NewTitleBox.Text.Trim();
   var model=NewModelBox.SelectedItem as Entity;if(model!=null)body["selections"]=new JObject {["model"]=model.Id};
   await DetachTerminalAsync(false);
   sessionSocket?.Dispose();sessionSocket=null;sessionLife.Cancel();sessionLife.Dispose();sessionLife=new CancellationTokenSource();
   draftBody=body;
   draft=new Entity(new JObject {["id"]="draft:"+Guid.NewGuid().ToString("N"),["title"]=body.Text("title"),["connectorId"]=project.ConnectorId,["projectId"]=project.Id,["runtime"]=body.Text("runtime"),["runtimeId"]=runtime.Id,["runtimeName"]=runtime.Name,["cwd"]=project.Workspace,["status"]="idle"});
   selected=draft;draftMessageId=draftMessageText=null;turnJustStarted=false;timeline.Snapshot(new JObject());timelineRows.Clear();projector.ClearToggles();catalogsSignature="";
   attachments.Clear();UpdateAttachments();MessageBox.Text="";ResetFiles();
   Tabs.SelectedIndex=0;ShowView(AppView.Session);RenderDraft();
   MessageBox.Focus(FocusState.Programmatic);
  });}
  async Task CreateDraftSessionAsync() {
   var content=MessageBox.Text.Trim();
   if(content.Length==0 && attachments.Count==0)throw new ArgumentException("请输入任务或添加附件。");
   var body=(JObject)draftBody.DeepClone();
   body["content"]=content;
   // A retry after a failed send must reuse the same id, or the server starts a second task.
   if(draftMessageText!=content || draftMessageId==null){draftMessageId=Guid.NewGuid().ToString("N");draftMessageText=content;}
   body["clientMessageId"]=draftMessageId;
   if(attachments.Count>0)body["attachments"]=new JArray(attachments.Select(InlineAttachment));
   sending=true;SendButton.IsEnabled=false;SteerButton.IsEnabled=false;
   try {
    var result=await api.PostAsync("/sessions/create-and-start",body,true,sessionLife.Token);
    draft=null;draftBody=null;draftMessageId=draftMessageText=null;
    // The first message starts a turn; the next snapshot can still answer with the
    // pre-turn capabilities, so hold send until events prove the turn finished.
    turnJustStarted=true;
    MessageBox.Text="";attachments.Clear();UpdateAttachments();
    await LoadDashboardAsync();
    var session=result["session"] as JObject;
    if(session!=null)await SelectSessionAsync(new Entity(session),true);
   } finally {sending=false;UpdateCapabilities();}
  }
  async void RenameSession_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var id=selected.Id;var value=await PromptAsync("重命名会话","名称",selected.Name);if(value==null)return;if(String.IsNullOrWhiteSpace(value))throw new ArgumentException("名称不能为空。");
   await api.RequestAsync("PATCH",SessionPath(id)+"/meta",new JObject {["title"]=value.Trim()});await ReloadSessionAsync(id);await LoadDashboardAsync();
  },"会话已重命名");}
  async void ArchiveSession_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var id=selected.Id;var archive=!selected.Archived;
   await api.PostAsync("/sessions/"+(archive?"archive":"unarchive"),new JArray(id));await ReloadSessionAsync(id);await LoadDashboardAsync();
  },"归档状态已更新");}
  async void Takeover_Click(object sender,RoutedEventArgs e){await ExecuteAsync(async()=> {
   RequireSession();var id=selected.Id;bool release=selected.Data.Flag("takeover");
   await api.RequestAsync(release?"DELETE":"POST",SessionPath(id)+"/takeover",release?null:new JObject());await ReloadSessionAsync(id);
  },"接管状态已更新");}
  async Task ReloadSessionAsync(string id){var ct=sessionLife.Token;var snapshot=await api.GetAsync(SessionPath(id)+"/snapshot",ct);ct.ThrowIfCancellationRequested();if(selected?.Id==id){timeline.Snapshot(snapshot);RenderSession();}}
  async Task<string> PromptAsync(string title,string label,string initial=""){var box=new TextBox {Header=label,Text=initial,TextWrapping=TextWrapping.Wrap};var dialog=new ContentDialog {Title=title,Content=box,PrimaryButtonText="确定",SecondaryButtonText="取消"};return await dialog.ShowAsync()==ContentDialogResult.Primary?box.Text:null;}
  bool noticeOpen;
  async void Notice_Click(object sender,RoutedEventArgs e){var row=(sender as FrameworkElement)?.Tag as TimelineRow;if(row?.Notice!=null)await ShowNoticeAsync(row.Notice);}
  // Back closes the dialog without answering; only an explicit action button responds.
  async Task ShowNoticeAsync(JObject notice) {
   if(noticeOpen||selected==null)return;
   var sessionId=selected.Id;var noticeId=notice.Text("noticeId");var allowed=timeline.Can("session.interaction.approval");
   var panel=new StackPanel();
   Action<string,string,bool> field=(label,value,mono)=> {
    if(String.IsNullOrWhiteSpace(value))return;
    panel.Children.Add(new TextBlock {Text=label,FontSize=13,Foreground=Ui.Res.Brush("MidBrush"),Margin=new Thickness(0,10,0,0)});
    panel.Children.Add(new TextBlock {Text=value,FontSize=mono?13:15,FontFamily=mono?new FontFamily("Consolas"):new FontFamily("Segoe UI"),TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=true});
   };
   var context=notice["context"] as JObject;
   var message=notice.Text("message");if(message.Length==0 && context!=null)message=context.Text("description");
   field("说明",message,false);
   if(context!=null) {
    field("命令",context.Text("command"),true);field("文件",context.Text("path",context.Text("filePath")),true);
    field("原因",context.Text("reason"),false);field("工作目录",context.Text("cwd"),true);
    field("工具",context.Text("toolName"),true);
    var toolInput=context["toolInput"] as JObject;
    if(toolInput!=null) {
     var questions=toolInput["questions"] as JArray;
     if(questions!=null) {
      int number=0;
      foreach(var question in questions.OfType<JObject>()) {
       number++;field("问题 "+number,question.Text("question",question.Text("header")),false);
       var options=question.Objects("options").Select(x=>x.Text("label",x.Text("value"))).Where(x=>x.Length>0).ToList();
       if(options.Count>0)field("选项 "+number,String.Join(" / ",options),false);
      }
     } else field("参数",toolInput.ToString(Newtonsoft.Json.Formatting.None),true);
    }
   }
   var error=new TextBlock {Foreground=Ui.Res.Brush("ErrTextBrush"),TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,0)};
   var dialog=new ContentDialog {Title=notice.Text("title","Agent 请求"),SecondaryButtonText="关闭"};
   var simple=new List<Button>();
   foreach(var action in notice.Objects("actions")) {
    var input=action["input"] as JObject;
    var uiSchema=input?["uiSchema"] as JObject;
    var schema=(input?["schema"] ?? input?["schema_"]) as JObject;
    InputRequestForm request=uiSchema!=null && uiSchema.Text("component")=="inputRequest"?new InputRequestForm(uiSchema):null;
    SchemaForm form=request!=null && request.HasQuestions?null:new SchemaForm(schema);
    var needsInput=input.Flag("required") || (request!=null && request.HasQuestions) || (form!=null && form.HasFields);
    var button=new Button {Content=action.Text("label",action.Text("actionId")),HorizontalAlignment=HorizontalAlignment.Stretch,IsEnabled=allowed,Margin=new Thickness(0,8,0,0)};
    var actionId=action.Text("actionId");
    button.Click+=async(s,e)=> {
     var payload=new JObject {["actionId"]=actionId};
     try{if(needsInput)payload["input"]=request!=null && request.HasQuestions?request.Read():form.Read();}catch(ArgumentException ex){error.Text=ex.Message;return;}
     dialog.Hide();
     await ExecuteAsync(async()=> {
      if(selected?.Id!=sessionId)throw new InvalidOperationException("会话已切换。");
      await api.PostAsync(SessionPath(sessionId)+"/runtime/notices/"+ApiClient.Escape(noticeId)+"/respond",payload,true,sessionLife.Token);
      await RecoverAsync(sessionId,sessionLife.Token);await ReloadSessionAsync(sessionId);
     },"回复已提交");
    };
    if(request!=null && request.HasQuestions){panel.Children.Add(request.Panel);panel.Children.Add(button);}
    else if(form!=null && form.HasFields){panel.Children.Add(form.Panel);panel.Children.Add(button);}
    else simple.Add(button);
   }
   if(!notice.Objects("actions").Any())panel.Children.Add(new TextBlock {Text="这个请求没有可操作的动作，可能已经在其他客户端处理。",Style=(Style)Application.Current.Resources["Muted"],Margin=new Thickness(0,8,0,0)});
   if(simple.Count>0) {
    var grid=new Grid {Margin=new Thickness(0,10,0,0)};
    for(int i=0;i<simple.Count;i++) {
     if(simple.Count<=2){grid.ColumnDefinitions.Add(new ColumnDefinition());Grid.SetColumn(simple[i],i);simple[i].Margin=new Thickness(i==0?0:4,8,i==0&&simple.Count>1?4:0,0);}
     else{grid.RowDefinitions.Add(new RowDefinition {Height=GridLength.Auto});Grid.SetRow(simple[i],i);}
     grid.Children.Add(simple[i]);
    }
    if(simple.Count<=2&&simple.Count>1)simple.Last().Style=(Style)Application.Current.Resources["AccentButton"];
    panel.Children.Add(grid);
   }
   if(!allowed)panel.Children.Add(new TextBlock {Text="当前权限不允许在手机上处理这个请求。",Style=(Style)Application.Current.Resources["Muted"],Margin=new Thickness(0,8,0,0)});
   panel.Children.Add(error);
   dialog.Content=new ScrollViewer {Content=panel,MaxHeight=440,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
   noticeOpen=true;
   try{await dialog.ShowAsync();}finally{noticeOpen=false;}
  }
  bool catalogUpdating;
  void RenderCatalogs() {
   var signature=timeline.Catalogs.ToString()+timeline.State["selections"]?.ToString();
   if(signature==catalogsSignature)return;catalogsSignature=signature;
   catalogUpdating=true;
   try{RenderCatalogsCore();}finally{catalogUpdating=false;}
  }
  void RenderCatalogsCore() {
   var modelItems=timeline.Catalogs["model"].Objects("models").Select(ToOption).ToList();
   var permissionItems=timeline.Catalogs["permission"].Objects("permissions").Select(ToOption).ToList();
   var selectedModel=timeline.State["selections"].Text("model");
   ModelBox.ItemsSource=modelItems;
   ModelBox.SelectedItem=modelItems.FirstOrDefault(x=>x.Id==selectedModel||x.Data.Objects("reasoningItems").Any(y=>y.Text("selectionId")==selectedModel))??modelItems.FirstOrDefault();
   ModelBox.IsEnabled=timeline.Can("catalog.model");
   PermissionBox.ItemsSource=permissionItems;PermissionBox.SelectedItem=permissionItems.FirstOrDefault(x=>x.Id==timeline.State["selections"].Text("permission"))??permissionItems.FirstOrDefault();
   PermissionBox.IsEnabled=timeline.Can("catalog.permission");
   UpdateEfforts(selectedModel);
  }
  static Entity ToOption(JObject x){var copy=(JObject)x.DeepClone();copy["name"]=x.Text("displayName",x.Text("id"));copy["id"]=x.Text("selectionId",x.Text("id"));return new Entity(copy);}
  async void Selection_Changed(object sender,SelectionChangedEventArgs e) {
   if(!ready||catalogUpdating||selected==null||e.AddedItems.Count==0)return;
   if(sender==ModelBox){catalogUpdating=true;try{UpdateEfforts();}finally{catalogUpdating=false;}}
   await ExecuteAsync(ApplySettingsAsync,"设置已应用");
   // Re-sync the pickers with the server's selections, including after a rejected change.
   catalogsSignature="";RenderCatalogs();
  }
  void UpdateEfforts(string preferred=null) {
   var model=ModelBox.SelectedItem as Entity;var list=model?.Data.Objects("reasoningItems").Select(ToOption).ToList()??new List<Entity>();
   EffortBox.ItemsSource=list;EffortBox.SelectedItem=list.FirstOrDefault(x=>x.Id==preferred)??list.FirstOrDefault(x=>x.Data.Flag("default"))??list.FirstOrDefault();
   EffortBox.IsEnabled=ModelBox.IsEnabled && list.Count>0;
  }
  async Task ApplySettingsAsync() {
   RequireSession();var selections=new JObject();
   if(ModelBox.IsEnabled){var model=EffortBox.IsEnabled?(EffortBox.SelectedItem as Entity):(ModelBox.SelectedItem as Entity);if(model!=null)selections["model"]=model.Id;}
   if(PermissionBox.IsEnabled && PermissionBox.SelectedItem is Entity)selections["permission"]=((Entity)PermissionBox.SelectedItem).Id;
   if(!selections.HasValues)throw new InvalidOperationException("当前 Runtime 不支持更改这些设置。");
   var id=selected.Id;await api.RequestAsync("PATCH",SessionPath(id)+"/runtime/selections",new JObject {["selections"]=selections},true,sessionLife.Token);await ReloadSessionAsync(id);
  }
  async Task LoadCommandsAsync(string id,System.Threading.CancellationToken ct) {
   CommandsPanel.Children.Clear();if(!timeline.Can("session.commands")){CommandsPanel.Children.Add(new TextBlock {Text="当前 Runtime 不提供会话命令。",Style=(Style)Application.Current.Resources["Muted"],Margin=new Thickness(12,0,12,0)});return;}
   var reply=await api.GetAsync(SessionPath(id)+"/runtime/commands",ct);ct.ThrowIfCancellationRequested();if(selected?.Id!=id)return;
   foreach(var command in reply.Objects("commands")){
    var label=new StackPanel();
    label.Children.Add(new TextBlock {Text=command.Text("title",command.Text("id")),FontSize=15});
    var note=command.Flag("enabled",true)?command.Text("description"):command.Text("disabledReason",command.Text("description"));
    if(note.Length>0)label.Children.Add(new TextBlock {Text=note,Style=(Style)Application.Current.Resources["Caption"],FontSize=13});
    var button=new Button {Content=label,IsEnabled=command.Flag("enabled",true),Style=(Style)Application.Current.Resources["RowButton"]};
    button.Click+=async(s,e)=> {await ExecuteAsync(async()=> {
     if(selected?.Id!=id)throw new InvalidOperationException("会话已切换。");
     var args="";if(command.Flag("acceptsArgs")){args=await PromptAsync(command.Text("title"),"参数（带空格的参数请加引号）");if(args==null)return;}
     var result=await api.PostAsync(SessionPath(id)+"/runtime/commands",new JObject {["command"]=command.Text("id"),["args"]=new JArray(CommandArguments.Parse(args))},true,sessionLife.Token);
     await ReloadSessionAsync(id);if(result.Text("message").Length>0)await ShowInfoAsync("命令结果",result.Text("message"));
    },"命令已执行");};
    CommandsPanel.Children.Add(button);
   }
  }
 }
}
