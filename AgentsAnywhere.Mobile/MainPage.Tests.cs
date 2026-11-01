using System;
using System.Linq;
using System.Threading.Tasks;
using AgentsAnywhere.Mobile.Services;
using AgentsAnywhere.Mobile.Models;
using Newtonsoft.Json.Linq;
using Windows.Storage;
using Windows.UI.Xaml.Controls;
namespace AgentsAnywhere.Mobile
{
 public sealed partial class MainPage
 {
#if DEBUG
  readonly JArray nativeTestResults=new JArray();
  static void TestCheck(bool value,string label){if(!value)throw new Exception(label);}
  async Task NativeTestAsync(string name,Func<Task> test) {
   try {await test();nativeTestResults.Add(new JObject {["name"]=name,["passed"]=true});DiagnosticLog.Write("test.pass "+name);}
   catch(Exception e){nativeTestResults.Add(new JObject {["name"]=name,["passed"]=false,["error"]=e.GetType().Name+": "+e.Message});DiagnosticLog.Exception("test.fail "+name,e);}
  }
  static async Task UntilAsync(Func<bool> check){var until=DateTime.UtcNow.AddSeconds(20);while(!check()){if(DateTime.UtcNow>until)throw new TimeoutException("Condition timed out");await Task.Delay(150);}}
  async Task StartTestWorkspaceAsync(bool runTests,bool loop=false)
  {
   await auth.LoginAsync("http://127.0.0.1:18743","desktop-test@example.invalid","synthetic-test-password",false);
   await api.PostAsync("/test/reset",new JObject());
   await EnterWorkspaceAsync();
   if(sessions.Count>0)await SelectSessionAsync(sessions.First(x=>x.Id==(loop?"ses_loop":"ses_demo")));
   StatusText.Text="测试环境 · 仅使用合成数据";
   if(!runTests)return;
   await NativeTestAsync("Windows PBKDF2 matches independent UTF-8 vector",()=>{
    TestCheck(AuthService.DerivePasswordVerifier("测试 password 🔐","c2FsdC10ZXh0")=="vj9qa4WsItE40TVcVUitNuJv6QiAQX9t74n3-YCJzM4","PBKDF2 mismatch");return Task.CompletedTask;
   });
   await NativeTestAsync("Official auth flow and invalid login classification",async()=>{
    TestCheck(auth.User.Text("userId")=="usr_demo","auth/me");
    using(var rejected=new ApiClient()) {
     int expired=0;rejected.Unauthorized+=()=>expired++;
     var service=new AuthService(rejected);
     try {await service.LoginAsync("http://127.0.0.1:18743","desktop-test@example.invalid","incorrect-synthetic-password",false);throw new Exception("Wrong password accepted");}
     catch(ApiException e){TestCheck(e.Operation=="/api/v2/auth/login"&&FriendlyError(e).Contains("登录失败")&&expired==0,"Incorrect classification");}
    }
   });
   await NativeTestAsync("Inventory, native controls and real UWP WebSockets",async()=>{
    TestCheck(devices.Count==2&&projects.Count>0&&timeline.Items.Count>0&&timelineRows.Count>0&&SendButton.IsEnabled&&view==AppView.Session,"Native workspace projection");
    await UntilAsync(()=>sessionSocket.IsConnected&&dashboardSocket.IsConnected);
   });
   await NativeTestAsync("Live messages and idempotent submission",async()=>{
    var marker="Native UWP test "+Guid.NewGuid().ToString("N");
    var body=new JObject {["content"]=marker,["clientMessageId"]=Guid.NewGuid().ToString("N")};
    await api.PostAsync("/sessions/ses_demo/runtime/messages",body);await api.PostAsync("/sessions/ses_demo/runtime/messages",body);
    await UntilAsync(()=>timelineRows.Count(x=>x.Kind==RowKind.User&&x.Text==marker)==1);
   });
   await NativeTestAsync("Disconnect reconnect and missed-event recovery",async()=>{
    var before=(await api.GetAsync("/test/stats")).Number("sessionConnections");
    var missed=timelineRows.Count(x=>x.Text.Contains("断线期间"));
    await api.PostAsync("/test/disconnect",new JObject());
    await UntilAsync(()=>timelineRows.Count(x=>x.Text.Contains("断线期间"))>missed);
    TestCheck((await api.GetAsync("/test/stats")).Number("sessionConnections")>before,"No reconnect");
   });
   await NativeTestAsync("Native files preview and binary download integrity",async()=>{
    await BrowseFilesAsync();TestCheck(FileList.Items.Count>=2,"File list");
    await PreviewFileAsync(new RemoteFileEntry(new JObject {["name"]="README.md",["path"]="README.md"}));
    TestCheck(!FileEditor.IsReadOnly&&FileEditor.Text.Contains("Desktop fixture"),"Text preview");
    var reply=await api.PostAsync(ConnectorPath+"/fs/read?root="+ApiClient.Escape(FileRoot),new JObject {["path"]="image.bin"});
    var data=reply["result"];var bytes=await api.DownloadAsync(data.Text("downloadUrl"));VerifyBytes(bytes,data.Number("size"),data.Text("sha256"));
    TestCheck(bytes.Length==256,"Binary transfer");
   });
   await NativeTestAsync("Remote project folder creation uses the connector shell RPC",async()=>{
    await api.PostAsync("/test/reset",new JObject());
    await CreateRemoteFolderAsync("con_demo","/workspace/demo/new-project");
    var posix=(await api.GetAsync("/test/stats")).Objects("shellCommands").Last();
    TestCheck(posix.Text("root")=="/workspace/demo"&&posix.Text("cwd")==".","POSIX shell root/cwd");
    TestCheck(posix.Text("command")=="mkdir -p \"/workspace/demo/new-project\"","POSIX mkdir command");
    await api.PostAsync("/test/reset",new JObject());
    await CreateRemoteFolderAsync("con_demo","D:\\Work\\Demo");
    var windows=(await api.GetAsync("/test/stats")).Objects("shellCommands").Last();
    TestCheck(windows.Text("root")=="D:\\Work"&&windows.Text("cwd")==".","Windows shell root/cwd");
    TestCheck(windows.Text("command")=="if not exist \"D:\\Work\\Demo\" mkdir \"D:\\Work\\Demo\"","Windows mkdir command");
    await api.PostAsync("/test/reset",new JObject());
    var rejected=false;
    try{await CreateRemoteFolderAsync("con_demo","/workspace/deny");}catch(InvalidOperationException){rejected=true;}
    TestCheck(rejected,"Non-zero exit code surfaces as a failure");
   });
  await NativeTestAsync("Native terminal replay and UTF-8 transport",async()=>{
    await OpenTerminalAsync();await UntilAsync(()=>terminalSocket.IsConnected&&terminalBuffer.Text.Contains("Mock terminal"));
    await UntilAsync(()=>terminalBuffer.Text.Contains("256-orange"));
    var colored=terminalBuffer.LineRuns(1);
    TestCheck(colored.Any(x=>x.Text=="green ok"&&x.Foreground==2),"SGR green");
    TestCheck(colored.Any(x=>x.Text=="bold red"&&x.Foreground==1&&(x.Flags&TerminalRun.Bold)!=0),"SGR bold red");
    TestCheck(colored.Any(x=>x.Text=="256-orange"&&x.Foreground==208),"SGR 256 colour");
    TestCheck(terminalLines.Count==terminalBuffer.LineCount,"line collection synced");
    await TerminalSendAsync("中文 native transport\r");await UntilAsync(()=>terminalBuffer.Text.Contains("中文 native transport"));
    await DetachTerminalAsync(true);
   });
   await NativeTestAsync("PasswordVault isolated credential round-trip",()=>{
    var vault=new Windows.Security.Credentials.PasswordVault();var item=new Windows.Security.Credentials.PasswordCredential("AgentsAnywhere.UWP.SelfTest","synthetic-test","synthetic-token");
    try{vault.Add(item);var loaded=vault.Retrieve(item.Resource,item.UserName);loaded.RetrievePassword();TestCheck(loaded.Password==item.Password,"Vault round-trip");}finally{vault.Remove(item);}return Task.CompletedTask;
   });
   await NativeTestAsync("Persistent diagnostics exclude synthetic secrets",async()=>{
    var log=await DiagnosticLog.ReadAsync();TestCheck(log.Contains("/api/v2/auth/password-salt")&&log.Contains("status=200"),"Missing HTTP diagnostics");
    foreach(var secret in new[]{"synthetic-test-password","synthetic-loopback-token","desktop-test@example.invalid","passwordVerifier\":","incorrect-synthetic-password"})TestCheck(!log.Contains(secret),"Secret leaked");
   });
   await NativeTestAsync("New session drafts locally and is created on first send",async()=>{
    await api.PostAsync("/test/reset",new JObject());
    var marker="Native draft "+Guid.NewGuid().ToString("N");
    draftBody=new JObject {["connectorId"]="con_demo",["projectId"]="prj_demo",["runtime"]="codex",["runtimeId"]="codex",["cwd"]="/workspace/demo",["selections"]=new JObject {["model"]="fixture-model"}};
    draft=new Entity(new JObject {["id"]="draft:test",["title"]="草稿会话",["connectorId"]="con_demo",["projectId"]="prj_demo",["runtime"]="codex",["runtimeId"]="codex",["cwd"]="/workspace/demo",["status"]="idle"});
    selected=draft;timeline.Snapshot(new JObject());timelineRows.Clear();
    TestCheck(IsDraft,"draft state");
    RenderDraft();
    TestCheck(SendButton.IsEnabled,"send enabled while drafting");
    MessageBox.Text=marker;
    await CreateDraftSessionAsync();
    TestCheck(draft==null&&!IsDraft,"draft cleared after the first send");
    TestCheck(selected!=null&&selected.Id.StartsWith("ses_"),"real session bound");
    var listed=(await api.GetAsync("/sessions/list")).Objects("sessions").FirstOrDefault(x=>x.Text("id")==selected.Id);
    TestCheck(listed!=null&&listed["selections"].Text("model")=="fixture-model","model selection forwarded");
    // The first message started a turn. The cloud can still answer the binding snapshot with
    // the pre-turn capabilities, which used to leave send enabled and the server answered the
    // next message with a bare "session-lock" 409. Hold send until the turn is proven finished.
    TestCheck(SessionBusy,"just-started session is busy");
    TestCheck(!SendButton.IsEnabled,"send is held while the turn is running");
    await api.PostAsync(SessionPath(selected.Id)+"/runtime/messages",new JObject {["content"]="turn finished",["clientMessageId"]=Guid.NewGuid().ToString("N")});
    await UntilAsync(()=>!SessionBusy&&SendButton.IsEnabled);
    TestCheck(SendButton.IsEnabled,"send returns once the turn is proven finished");
   });
   await NativeTestAsync("New conversation page keeps its continue button usable",async()=>{
    var online=projects.Where(x=>devices.Any(d=>d.Id==x.ConnectorId&&d.Online)).ToList();
    TestCheck(online.Count>0,"online projects");
    NewSessionError.Text="";NewTitleBox.Text="";NewRuntimeBox.ItemsSource=null;NewModelBox.ItemsSource=null;
    NewProjectBox.ItemsSource=online;
    ShowView(AppView.NewSession);
    NewProjectBox.SelectedItem=online.First();
    await UntilAsync(()=>NewRuntimeBox.SelectedItem!=null&&NewModelBox.SelectedItem!=null);
    await UntilAsync(()=>NewSessionStartButton.IsEnabled);
    TestCheck(view==AppView.NewSession&&NewSessionStartButton.IsEnabled,"continue button usable");
    ShowView(AppView.Home);
   });
   await NativeTestAsync("AskUserQuestion notices render their questions and answers",async()=>{
    var snapshot=await api.GetAsync("/sessions/ses_demo/snapshot");
    var notice=snapshot.Objects("notices").FirstOrDefault(x=>x.Text("noticeId")=="notice_question");
    TestCheck(notice!=null,"question notice fixture");
    TestCheck(notice.Text("message").Length==0,"fixture carries no message, like the real runtime");
    var action=notice.Objects("actions").FirstOrDefault(x=>x.Text("actionId")=="submit");
    TestCheck(action!=null,"submit action");
    var ui=action["input"]?["uiSchema"] as JObject;
    var form=new InputRequestForm(ui);
    TestCheck(form.HasQuestions,"input request form built");
    TestCheck(form.Panel.Children.OfType<TextBlock>().Any(x=>x.Text.Contains("Which platform")),"question prompt rendered");
    var options=form.Panel.Children.OfType<RadioButton>().ToList();
    TestCheck(options.Count==2,"option buttons rendered");
    var rejected=false;
    try{form.Read();}catch(ArgumentException){rejected=true;}
    TestCheck(rejected,"unanswered question rejected");
    options[1].IsChecked=true;
    var answers=form.Read()["answers"] as JObject;
    var platform=answers?["platform"] as JObject;
    var ids=platform?["optionIds"] as JArray;
    TestCheck(ids!=null&&ids.Count==1&&(string)ids[0]=="mobile","answer shape");
   });   await FileIO.WriteTextAsync(await ApplicationData.Current.LocalFolder.CreateFileAsync("native-test-results.json",CreationCollisionOption.ReplaceExisting),nativeTestResults.ToString());
   StatusText.Text="原生自测完成："+nativeTestResults.Count(x=>x.Value<bool>("passed"))+" / "+nativeTestResults.Count+" 通过";
  }
#endif
 }
}
