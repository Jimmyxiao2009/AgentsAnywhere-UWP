using AgentsAnywhere.Mobile.Models;
using AgentsAnywhere.Mobile.Services;
using Newtonsoft.Json.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
class Program {
 static List<object> results=new();
 static void Check(bool condition,string name) {if(!condition)throw new Exception(name);}
 static async Task Test(string name,Func<Task> test) {try{await test();results.Add(new{name,passed=true});Console.WriteLine("PASS "+name);}catch(Exception e){results.Add(new{name,passed=false,error=e.Message});Console.WriteLine("FAIL "+name+": "+e.Message);}}
 static Task Sync(Action action){action();return Task.CompletedTask;}
 static void Throws<T>(Action action) where T:Exception {try{action();throw new Exception("Expected "+typeof(T).Name);}catch(T){}}
 static JObject Item(string id,long revision,long seq,string text){return new JObject {["id"]=id,["sessionId"]="s",["revision"]=revision,["orderSeq"]=seq,["content"]=new JObject {["text"]=text}};}
 static JObject Event(long seq,string type,JObject payload){return new JObject {["sessionId"]="s",["sequence"]=seq,["cursor"]="seq:"+seq,["eventId"]=type+seq,["type"]=type,["payload"]=payload};}
 static JObject Cap(bool allowed){return new JObject {["revision"]=1,["capabilities"]=new JArray(new JObject {["capabilityId"]="session.send_message",["supported"]=true,["available"]=true,["allowed"]=allowed})};}
 static JObject Tl(string id,long order,string type,string status,string role,JObject content){var o=new JObject {["id"]=id,["sessionId"]="s",["type"]=type,["status"]=status,["orderSeq"]=order,["revision"]=1,["content"]=content,["createdAt"]="2026-10-09T10:00:"+(order%60).ToString("00")+"Z"};if(role!=null)o["role"]=role;return o;}
 static List<JObject> LoopItems(bool running)=>new List<JObject>{
  Tl("u1",1,"message","done","user",new JObject {["text"]="fix it"}),
  Tl("r1",2,"system","done",null,new JObject {["kind"]="reasoning",["rawText"]="look first"}),
  Tl("c1",3,"tool","done",null,new JObject {["kind"]="command",["command"]="dotnet test",["output"]="ok",["exitCode"]=0}),
  Tl("a1",4,"message","done","assistant",new JObject {["text"]="note between tools"}),
  Tl("r2",5,"system","done",null,new JObject {["kind"]="reasoning",["summaries"]=new JArray(new JObject {["text"]="retry"})}),
  Tl("c2",6,"tool","failed",null,new JObject {["kind"]="command",["command"]="dotnet test",["output"]="FAIL",["exitCode"]=1}),
  Tl("f1",7,"tool","done",null,new JObject {["kind"]="file_change",["changes"]=new JArray(new JObject {["path"]="src/a.cs",["kind"]="update",["diff"]="--- a\n+++ b\n-old\n+new"})}),
  Tl("a2",8,"message","done","assistant",new JObject {["text"]="完成了"})};
 static TimelineState Empty(){var state=new TimelineState();state.Snapshot(new JObject {["session"]=new JObject {["id"]="s"},["eventCursor"]="seq:4",["timeline"]=new JObject {["items"]=new JArray()},["effectiveCapabilities"]=Cap(true)});return state;}
 public static async Task<int> Main(){
  await Test("official origin and /api/v2 normalization",()=>Sync(()=>{
   Check(ApiClient.NormalizeOrigin(" web.agents-anywhere.com/api/v2/ ")==ApiClient.OfficialOrigin,"official");
   using var api=new ApiClient();Check(api.Url("/auth/me").AbsoluteUri==ApiClient.OfficialOrigin+"/api/v2/auth/me","API path");Check(api.Url("/api/v2/auth/me").AbsoluteUri==api.Url("/auth/me").AbsoluteUri,"double prefix");
   Check(api.SocketUrl("/dashboard/ws").Scheme=="wss","TLS socket");
  }));
  await Test("reject unsafe server origins",()=>Sync(()=>{
   foreach(var v in new[]{"http://example.com","https://user:pass@example.com","https://example.com/subpath","https://example.com?token=x","file:///tmp","https://example.com/#x"})Throws<ArgumentException>(()=>ApiClient.NormalizeOrigin(v));
   Check(ApiClient.NormalizeOrigin("http://127.0.0.1:18743")=="http://127.0.0.1:18743","local fixture");
   using var api=new ApiClient();Throws<ArgumentException>(()=>api.Url("//evil.example/path"));
  }));
  await Test("same-cursor capability and state projections remain usable",()=>Sync(()=>{
   var state=Empty();
   Check(state.Can("session.send_message"),"initial cap");
   state.Apply(Event(4,"runtime.capability.updated",new JObject {["capabilitySet"]=Cap(false)}));
   Check(!state.Can("session.send_message"),"same cursor capability");
   state.Apply(Event(4,"runtime.capability.updated",new JObject {["capabilitySet"]=Cap(true)}));
   Check(state.Can("session.send_message"),"A B A projection");
   state.Apply(Event(4,"runtime.state.updated",new JObject {["state"]=new JObject {["status"]="running"}}));
   Check(state.State.Text("status")=="running","state");
  }));
  await Test("timeline revisions and repeated sequences",()=>Sync(()=>{
   var state=Empty();var first=Item("a",2,1,"new");
   state.Apply(Event(5,"timeline.item.updated",new JObject {["item"]=first}));
   state.Apply(Event(5,"timeline.item.updated",new JObject {["item"]=Item("b",1,2,"second")}));
   state.Apply(Event(5,"timeline.item.updated",new JObject {["item"]=Item("a",1,1,"old")}));
   Check(state.Items.Count==2,"duplicate sequence items");Check(state.Items[0].Body=="new","stale revision");
   var other=Event(6,"timeline.item.updated",new JObject {["item"]=Item("bad",1,3,"other")});other["sessionId"]="other";
   Check(!state.Apply(other)&&state.Items.Count==2,"cross session");
  }));
  await Test("sparse recovery and metadata refresh preserve older history",()=>Sync(()=>{
   var state=Empty();state.Upsert(Item("history",1,1,"older"));
   state.Apply(Event(8,"timeline.item.updated",new JObject {["item"]=Item("latest",2,9,"latest")}),true);
   state.Apply(Event(7,"session.meta.updated",new JObject {["session"]=new JObject {["id"]="s",["title"]="current"}}),true);
   state.AdvanceCursor("seq:12");
   state.RefreshProjection(new JObject {["state"]=new JObject {["status"]="idle"},["notices"]=new JArray(),["effectiveCapabilities"]=Cap(false)});
   Check(state.Cursor=="seq:12"&&state.Items.Count==2,"cursor/history");Check(state.Session.Text("title")=="current","aggregate metadata");Check(!state.Can("session.send_message"),"projection refresh");
  }));
  await Test("terminal handles split UTF-8, CR and split ANSI",()=>Sync(()=>{
   var b=new TerminalBuffer();var bytes=Encoding.UTF8.GetBytes("中文");b.Feed(bytes[..2]);b.Feed(bytes[2..4]);b.Feed(bytes[4..]);Check(b.Text=="中文","UTF8");
   b.Reset();b.Feed(Encoding.UTF8.GetBytes("10%\r20%"));Check(b.Text=="20%","CR");
   b.Feed(Encoding.UTF8.GetBytes("\x1b[2"));b.Feed(Encoding.UTF8.GetBytes("Jdone"));Check(b.Text=="done","ANSI split");
   b.Reset();b.Feed(Encoding.UTF8.GetBytes("\x1b]0;title\aab\bX"));Check(b.Text=="aX","OSC/backspace");
  }));
  await Test("terminal parses SGR colours, truecolour and the caret",()=>Sync(()=>{
   var b=new TerminalBuffer();
   b.Feed(Encoding.UTF8.GetBytes("\x1b[31mred\x1b[32mgreen\x1b[0mplain"));
   var runs=b.LineRuns(0);
   Check(runs.Count==3&&runs[0].Text=="red"&&runs[0].Foreground==1,"basic colours "+runs.Count);
   Check(runs[1].Foreground==2&&runs[2].Foreground==-1&&runs[2].Text=="plain","reset");
   b.Reset();b.Feed(Encoding.UTF8.GetBytes("\x1b[1;44mbold on blue\x1b[22m dim"));
   runs=b.LineRuns(0);
   Check((runs[0].Flags&TerminalRun.Bold)!=0&&runs[0].Background==4,"bold and background");
   b.Reset();b.Feed(Encoding.UTF8.GetBytes("\x1b[38;5;208morange\x1b[38;2;10;20;30mrgb\x1b[39mback"));
   runs=b.LineRuns(0);
   Check(runs[0].Foreground==208&&runs[1].Foreground==(0x1000000|(10<<16)|(20<<8)|30)&&runs[2].Foreground==-1,"256 and truecolour");
   b.Reset();b.Feed(Encoding.UTF8.GetBytes("ab"));
   Check(b.CursorRow==0&&b.CursorCol==2,"caret position");
  }));
  await Test("terminal alternate screen isolates full-screen apps",()=>Sync(()=>{
   var b=new TerminalBuffer();
   b.Feed(Encoding.UTF8.GetBytes("scrollback line\r\nprompt$ "));
   b.Feed(Encoding.UTF8.GetBytes("\x1b[?1049h\x1b[2J\x1b[Hvim screen"));
   Check(b.UsingAlternateScreen&&b.Text=="vim screen","alternate screen active");
   b.Feed(Encoding.UTF8.GetBytes("\x1b[?1049l"));
   Check(!b.UsingAlternateScreen&&b.Text=="scrollback line\nprompt$","primary restored");
  }));
  await Test("missing files are told apart from a dead connector",()=>Sync(()=>{
   Check(ApiClient.IsMissingFileError(new ApiException(404,"file not found")),"404 is missing");
   Check(ApiClient.IsMissingFileError(new ApiException(502,"file not found: /workspace/x")),"502 not found is missing");
   Check(!ApiClient.IsMissingFileError(new ApiException(504,"fs.readText timed out")),"timeout is not missing");
   Check(!ApiClient.IsMissingFileError(new ApiException(502,"permission denied")),"permission is not missing");
   Check(!ApiClient.IsMissingFileError(new ApiException(409,"connector is offline")),"offline is not missing");
  }));
  await Test("session-lock rejections are recognised as a running turn",()=>Sync(()=>{
   Check(ApiClient.IsSessionLockedError(new ApiException(409,"session-lock")),"bare code");
   Check(ApiClient.IsSessionLockedError(new ApiException(409,"turn_active")),"capability reason");
   Check(ApiClient.IsSessionLockedError(new ApiException(409,"Claude runtime already has an active turn for this session")),"connector message");
   Check(!ApiClient.IsSessionLockedError(new ApiException(409,"connector is offline")),"offline is not a lock");
   Check(!ApiClient.IsSessionLockedError(new ApiException(409,"当前会话暂不支持此操作。")),"local guard is not a lock");
   Check(!ApiClient.IsSessionLockedError(new ApiException(412,"session-lock")),"other status is not a lock");
  }));
  await Test("quoted command parameters",()=>Sync(()=>{
   Check(CommandArguments.Parse("one \"two words\" ''").SequenceEqual(new[]{"one","two words",""}),"quotes");
   Throws<ArgumentException>(()=>CommandArguments.Parse("\"unfinished").ToList());
  }));
  await Test("timeline turns: interleaved reasoning and tools share one process rail",()=>Sync(()=>{
   var items=LoopItems(false).Select(x=>new TimelineItem(x)).ToList();
   var rows=new TimelineProjector{AgentName="Codex"}.Project(items,new JObject[0],false);
   Check(rows.Select(x=>x.Kind).SequenceEqual(new[]{RowKind.User,RowKind.Process,RowKind.Output}),"row kinds "+String.Join(",",rows.Select(x=>x.Kind)));
   var steps=rows[1].Steps;
   Check(steps.Select(x=>x.Kind).SequenceEqual(new[]{StepKind.Reasoning,StepKind.Tool,StepKind.Note,StepKind.Reasoning,StepKind.Tool,StepKind.Tool}),"step kinds "+String.Join(",",steps.Select(x=>x.Kind)));
   Check(steps[1].Title=="dotnet test"&&steps[1].Subtitle.StartsWith("命令"),"command title");
   Check(steps[4].State==StepState.Failed&&steps[4].Expanded,"failed tool expanded");
   Check(steps[5].Title=="src/a.cs"&&steps[5].Subtitle.Contains("+1 −1"),"file change");
   Check(rows[1].Expanded&&rows[1].Failed&&rows[1].Summary.StartsWith("思考 2 · 工具 3 · 1 失败"),"summary "+rows[1].Summary);
   Check(rows[2].Text=="完成了"&&rows[2].Footer.StartsWith("已完成")&&rows[2].Meta.StartsWith("Codex"),"output");
   Check(steps.Last().IsLast&&!steps[0].IsLast,"rail end");
  }));
  await Test("timeline turns: running turn keeps last steps and trailing reply in rail",()=>Sync(()=>{
   var list=LoopItems(true);
   for(int i=0;i<6;i++)list.Add(Tl("t"+i,20+i,"tool","done",null,new JObject {["kind"]="command",["command"]="echo "+i}));
   list.Add(Tl("r9",40,"system","running",null,new JObject {["kind"]="reasoning",["rawText"]="still thinking"}));
   var projector=new TimelineProjector();
   var rows=projector.Project(list.Select(x=>new TimelineItem(x)).ToList(),new JObject[0],true);
   var process=rows.Single(x=>x.Kind==RowKind.Process);
   Check(rows.Last()==process&&process.Running&&process.Expanded,"running process is last and open");
   Check(process.Steps.Count==TimelineProjector.RunningVisibleSteps+1&&process.Steps[0].Kind==StepKind.More,"collapsed early steps");
   Check(process.Steps.Any(x=>x.Kind==StepKind.Note&&x.Body=="完成了")||process.Steps[0].Title.Contains("显示前面"),"reply followed by tools becomes note");
   Check(process.Steps.Last().Kind==StepKind.Reasoning&&process.Steps.Last().Expanded&&process.Steps.Last().Title.StartsWith("正在思考"),"live reasoning");
   projector.Toggle(process.Steps[0].Id);
   var all=projector.Project(list.Select(x=>new TimelineItem(x)).ToList(),new JObject[0],true).Single(x=>x.Kind==RowKind.Process);
   Check(all.Steps.Count>TimelineProjector.RunningVisibleSteps+1&&all.Steps.All(x=>x.Kind!=StepKind.More),"show all");
   projector.Toggle(all.Key);
   Check(!projector.Project(list.Select(x=>new TimelineItem(x)).ToList(),new JObject[0],true).Single(x=>x.Kind==RowKind.Process).Expanded,"user collapse");
  }));
  await Test("timeline turns: markers, interruption and notices",()=>Sync(()=>{
   var list=new List<JObject>{
    Tl("u1",1,"message","done","user",new JObject {["text"]="hi"}),
    Tl("r1",2,"system","done",null,new JObject {["kind"]="reasoning",["summaries"]=new JArray(new JObject {["text"]="plan"})}),
    Tl("c1",3,"tool","interrupted",null,new JObject {["kind"]="command",["command"]=new JArray("rg","x")}),
    Tl("e1",4,"turn.end","interrupted",null,new JObject()),
    Tl("m1",5,"marker","done",null,new JObject {["kind"]="compact"}),
    Tl("h1",6,"message","hidden","assistant",new JObject {["text"]="secret"})};
   var notice=new JObject {["noticeId"]="n1",["title"]="批准操作",["context"]=new JObject {["command"]="rm x"}};
   var rows=new TimelineProjector().Project(list.Select(x=>new TimelineItem(x)).ToList(),new[]{notice},false);
   Check(rows.Select(x=>x.Kind).SequenceEqual(new[]{RowKind.User,RowKind.Process,RowKind.Error,RowKind.Marker,RowKind.Notice}),"kinds "+String.Join(",",rows.Select(x=>x.Kind)));
   Check(rows[1].Stopped&&rows[1].Expanded&&rows[1].Steps[1].Title=="rg x"&&rows[1].Steps[0].Body=="plan","interrupted rail");
   Check(rows[3].Text=="上下文已压缩"&&rows[4].Detail=="rm x"&&rows.All(x=>x.Text!="secret"),"marker/notice/hidden");
  }));
  await Test("HTTP request authentication and JSON contract",async()=>{
   var handler=new StubHandler();using var api=new ApiClient(handler);api.Token="private-test-token";
   await api.PostAsync("/auth/login",new JObject {["email"]="fixture",["password"]="fixture"},false);
   Check(handler.Auth==null && handler.Path.EndsWith("/api/v2/auth/login"),"login header");
   await api.GetAsync("/auth/me");Check(handler.Auth=="Bearer private-test-token","bearer");
   handler.Status=HttpStatusCode.Unauthorized;handler.Body="{\"detail\":\"expired\"}";int unauthorized=0;api.Unauthorized+=()=>unauthorized++;
   try{await api.GetAsync("/auth/me");throw new Exception("401 not rejected");}catch(ApiException e){Check(e.Status==401,"status");}
   Check(unauthorized==1,"expiry signal");
   try{await api.PostAsync("/auth/login",new JObject(),false);}catch(ApiException e){Check(e.Operation=="/api/v2/auth/login","login context");}
   Check(unauthorized==1,"login failure is not token expiry");
   api.Token=null;try{await api.GetAsync("/auth/me");}catch(ApiException){}
   Check(unauthorized==1,"anonymous failure is not token expiry");
   handler.Status=HttpStatusCode.OK;handler.Body="{\"ok\":false,\"error\":{\"message\":\"connector refused\"}}";
   try{await api.PostAsync("/sessions/s/runtime/messages",new JObject());throw new Exception("RPC rejected not handled");}catch(ApiException e){Check(e.Message=="connector refused","RPC message");}
   try{await api.DownloadAsync("https://evil.example/file");throw new Exception("cross origin accepted");}catch(ApiException){}
  });
  await Test("fixture integration: login, inventory, snapshot, notices, messages, files",async()=>{
   using var api=new ApiClient();api.Configure("http://127.0.0.1:18743");
   var salt=await api.PostAsync("/auth/password-salt",new JObject {["email"]="desktop-test@example.invalid"},false);
   var verifier=Convert.ToBase64String(Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes("synthetic-test-password"),Encoding.UTF8.GetBytes(salt.Text("salt")),120000,HashAlgorithmName.SHA256,32)).TrimEnd('=').Replace('+','-').Replace('/','_');
   var auth=await api.PostAsync("/auth/login",new JObject {["email"]="desktop-test@example.invalid",["passwordVerifier"]=verifier},false);api.Token=auth.Text("accessToken");
   Check((await api.GetAsync("/auth/me")).Text("userId")=="usr_demo","me");
   Check((await api.GetAsync("/connectors")).Objects("connectors").Any(),"connectors");
   Check((await api.GetAsync("/projects")).Objects("projects").Any(),"projects");
   Check((await api.GetAsync("/sessions/list")).Objects("sessions").Any(),"sessions");
   var snapshot=await api.GetAsync("/sessions/ses_demo/snapshot");var state=new TimelineState();state.Snapshot(snapshot);
   Check(state.Can("session.send_message") && state.Items.Count>0,"snapshot");
    var messageId=Guid.NewGuid().ToString("N");var marker="Protocol integration test "+messageId;var body=new JObject {["content"]=marker,["clientMessageId"]=messageId};
   await api.PostAsync("/sessions/ses_demo/runtime/messages",body);await api.PostAsync("/sessions/ses_demo/runtime/messages",body);
   var recovery=await api.GetAsync("/sessions/ses_demo/events?after="+ApiClient.Escape(state.Cursor));foreach(var evt in recovery.Objects("events"))state.Apply(evt,true);state.AdvanceCursor(recovery.Text("nextCursor"));
    Check(state.Items.Count(x=>x.Body==marker)==1,"idempotent message");
   var notice=state.Notices.FirstOrDefault(x=>x.Text("noticeId")=="notice_input");
   if(notice!=null)await api.PostAsync("/sessions/ses_demo/runtime/notices/notice_input/respond",new JObject {["actionId"]="reply",["input"]=new JObject {["answer"]="Windows"}});
   var read=await api.PostAsync("/connectors/con_demo/fs/readText?root=%2Fworkspace%2Fdemo",new JObject {["path"]="README.md"});
   var hash=read.Text("sha256");
   await api.PostAsync("/connectors/con_demo/fs/write?root=%2Fworkspace%2Fdemo",new JObject {["path"]="README.md",["content"]=read.Text("content")+"Protocol tests passed.\n",["encoding"]="utf8",["ifMatch"]=hash});
   try{await api.PostAsync("/connectors/con_demo/fs/write?root=%2Fworkspace%2Fdemo",new JObject {["path"]="README.md",["content"]="bad overwrite",["encoding"]="utf8",["ifMatch"]=hash});throw new Exception("stale file overwrite");}catch(ApiException e){Check(e.Status==412,"file conflict");}
   var download=await api.PostAsync("/connectors/con_demo/fs/read?root=%2Fworkspace%2Fdemo",new JObject {["path"]="README.md"});var metadata=download["result"];
   var bytes=await api.DownloadAsync(metadata.Text("downloadUrl"));
   Check(Convert.ToHexString(SHA256.HashData(bytes)).Equals(metadata.Text("sha256"),StringComparison.OrdinalIgnoreCase),"download integrity");
   var upload=await api.UploadAsync("/sessions/ses_demo/attachments","fixture.bin",new byte[]{0,1,2,3},"application/octet-stream");
   Check(upload.Objects("attachments").Single().Number("size")==4,"multipart upload");
  });
  Directory.CreateDirectory("TestResults");var json=JArray.FromObject(results);await File.WriteAllTextAsync("TestResults/protocol-tests.json",json.ToString());
  return json.Any(x=>!(bool)x["passed"])?1:0;
 }
 sealed class StubHandler:HttpMessageHandler {
  public string Auth,Path;public HttpStatusCode Status=HttpStatusCode.OK;public string Body="{}";
  protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r,CancellationToken ct){Auth=r.Headers.Authorization?.ToString();Path=r.RequestUri.AbsoluteUri;return Task.FromResult(new HttpResponseMessage(Status){Content=new StringContent(Body)});}
 }
}

