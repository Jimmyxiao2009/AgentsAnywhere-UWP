"""Deterministic loopback-only protocol fixture. No real agent or shell is executed.
Matches the inspected upstream API and validates request fields. Test-only routes never
exist in the production client. Run: python tests/mock_server.py
"""
import base64, copy, hashlib, json, re, socket, struct, threading, time, uuid
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from urllib.parse import urlparse, parse_qs
from email.parser import BytesParser
from email.policy import default
from datetime import datetime, timezone

LOCK = threading.RLock()
TOKEN = "synthetic-loopback-token"
SOCKETS = []
TICKETS = {}
REQUESTS = []
EVENTS = {}
FILES = {"README.md": b"# Desktop fixture\n\nHello from Agents Anywhere.\n",
         "src/main.cs": "class Demo { /* 中文文件 */ }\n".encode(),
         "image.bin": bytes(range(256))}
UPLOADS = {}
CLIENT_MESSAGES = {}
SHELL_COMMANDS = []
CONNECTORS = [
 {"id":"con_demo","name":"Desktop fixture","deviceOs":"windows","status":"online"},
 {"id":"con_offline","name":"Offline fixture","deviceOs":"linux","status":"offline"}]
PROJECTS = [{"id":"prj_demo","userId":"usr_demo","connectorId":"con_demo","name":"UWP 工作区","workspacePath":"/workspace/demo","manuallyCreated":True}]
SESSIONS = [{"id":"ses_demo","connectorId":"con_demo","connectorStatus":"online","projectId":"prj_demo","runtime":"codex","runtimeType":"codex","runtimeId":"codex","runtimeName":"Codex","title":"桌面验证：继续你的工作","cwd":"/workspace/demo","status":"idle","archived":False,"takeover":False}]
ITEMS = {"ses_demo":[{"id":f"item_{i}","sessionId":"ses_demo","type":"message","status":"done","role":"assistant" if i%2 else "user","content":{"text":f"历史消息 {i}：原生客户端通过公开 API 与工作设备同步。"},"orderSeq":i,"revision":1,"updatedSeq":i,"createdAt":"2026-10-09T00:00:00Z"} for i in range(1,11)]}
def loop_items():
 """Synthetic multi-turn agentic loop (reasoning between tools) for UI inspection only."""
 rows=[
  ("message","done","user",{"text":"手机上 WebSocket 隔一会儿就断（1006），帮我查清楚是哪一边的问题，能修就修。"}),
  ("system","done",None,{"kind":"reasoning","summaries":[{"text":"先确认断线发生在哪一层：客户端主动关闭、系统网络栈，还是服务端。看诊断日志里每次断开的时间间隔。"}]}),
  ("tool","done",None,{"kind":"tool_call","toolName":"Read","input":{"file_path":"LocalState/diagnostics.log"},"output":"412 lines"}),
  ("tool","done",None,{"kind":"tool_call","toolName":"Grep","input":{"pattern":"close 1006"},"output":"9 matches"}),
  ("system","done",None,{"kind":"reasoning","rawText":"9 次断开的间隔都在 90 到 100 秒之间，非常规律。更像是中间某一层的空闲超时。看看客户端有没有发心跳。"}),
  ("message","done","assistant",{"text":"客户端没有应用层心跳，连接空闲约 90 秒就会被断开。我加一个 30 秒的 ping，再跑测试确认。"}),
  ("tool","done",None,{"kind":"file_change","changes":[{"path":"Services/RealtimeSocket.cs","kind":"update","diff":"--- a\n+++ b\n-  // no heartbeat\n+  StartHeartbeat(TimeSpan.FromSeconds(30));\n+  // pong frames are filtered"}]}),
  ("tool","failed",None,{"kind":"command","command":"dotnet run --project tests\\ProtocolTests","output":"[FAIL] socket ignores pong frames\n  expected: no timeline event\n  actual:   message \"pong\"","exitCode":1}),
  ("system","done",None,{"kind":"reasoning","rawText":"失败是因为 pong 被当成普通消息分发给了时间线。在接收循环里先过滤掉心跳回包。"}),
  ("tool","done",None,{"kind":"command","command":"dotnet run --project tests\\ProtocolTests","output":"10 / 10 passed","exitCode":0}),
  ("message","done","assistant",{"text":"## 结论\n\n找到原因了：连接**空闲约 90 秒**会被中间层断开，所以出现 `1006`。\n\n- [x] 每 30 秒发送一次 *ping*，收到的 pong 不再进入时间线\n- [ ] 手机上挂半小时确认\n\n| 项目 | 修改前 | 修改后 |\n| --- | --- | --- |\n| 心跳 | 无 | 30 秒 |\n| 测试 | 9 / 9 | 10 / 10 |\n\n> 注意：pong 帧不再进入时间线。\n\n```csharp\nStartHeartbeat(TimeSpan.FromSeconds(30));\n```\n\n详见 [RFC 6455](https://tools.ietf.org/html/rfc6455)。"}),
  ("turn.end","done",None,{}),
  ("marker","done",None,{"kind":"compact"}),
  ("message","done","user",{"text":"顺便看看退避上限是不是 30 秒。"}),
  ("system","done",None,{"kind":"reasoning","rawText":"读一下 RealtimeSocket 里的重连延迟计算。"}),
  ("tool","done",None,{"kind":"command","command":"rg \"Math.Min\\(30\" -n Services","output":"Services/RealtimeSocket.cs:52: Math.Min(30,Math.Pow(2,attempts))","exitCode":0}),
  ("system","running",None,{"kind":"reasoning","rawText":"退避是 2 的幂次，上限 30 秒，稳定 30 秒后重置。接下来确认 ticket 是否每次重新申请"})]
 out=[]
 for i,(t,status,role,content) in enumerate(rows,1):
  item={"id":f"loop_{i}","sessionId":"ses_loop","type":t,"status":status,"content":content,"orderSeq":i,"revision":1,"updatedSeq":i,"createdAt":f"2026-10-09T03:{i:02d}:00Z"}
  if role:item["role"]=role
  out.append(item)
 return out
SESSIONS.append({"id":"ses_loop","connectorId":"con_demo","connectorStatus":"online","projectId":"prj_demo","runtime":"codex","runtimeType":"codex","runtimeId":"codex","runtimeName":"Codex","title":"排查 WebSocket 1006","cwd":"/workspace/demo","status":"running","archived":False,"takeover":False})
ITEMS["ses_loop"]=loop_items()
NOTICES = {}
STATE = {}
STATS = {"sessionConnections":0,"dashboardConnections":0,"terminalConnections":0,"recoveries":0,"messagePosts":0,"uniqueMessages":0}
TERMINALS = {}
TERMINAL_CHUNKS = {}

def now():
 return datetime.now(timezone.utc).isoformat().replace("+00:00","Z")

def session(sid):
 return next((s for s in SESSIONS if s["id"]==sid),None)

def capabilities(sid):
 s=session(sid)
 ids=["session.send_message","session.interrupt","session.steer","session.commands","session.interaction.approval","runtime.attachment","catalog.model","catalog.permission","catalog.effort"]
 return {"revision":1,"capabilities":[{"capabilityId":i,"supported":True,"available":True,"allowed":True} for i in ids]}

def catalogs():
 return {"model":{"runtime":"codex","revision":1,"models":[{"id":"demo-model","displayName":"Demo model","selectionId":"demo-model","default":True,"reasoningItems":[{"id":"medium","displayName":"Medium","selectionId":"demo-model/medium","default":True},{"id":"high","displayName":"High","selectionId":"demo-model/high"}]}]},
         "permission":{"runtime":"codex","revision":1,"permissions":[{"id":"read-only","displayName":"只读","selectionId":"read-only","default":True},{"id":"workspace-write","displayName":"工作区写入","selectionId":"workspace-write"}]}}

def state(sid):
 return {"sessionId":sid,"status":session(sid)["status"],"selections":STATE.get(sid,{"model":"demo-model/medium","permission":"read-only"})}

def snapshot(sid):
 items=ITEMS.get(sid,[]);limit=5 if sid=="ses_demo" else 40
 return {"session":session(sid),"state":state(sid),"timeline":{"items":items[-limit:],"hasMore":len(items)>limit,"nextSeq":len(items)},"notices":NOTICES.get(sid,[]),"effectiveCapabilities":capabilities(sid),"runtimeCapabilities":capabilities(sid),"catalogs":catalogs(),"eventCursor":f"seq:{len(EVENTS.get(sid,[]))}","serverTime":now()}

def dashboard():
 return {"type":"dashboard.snapshot","connectors":CONNECTORS,"projects":PROJECTS,"sessions":SESSIONS,"sessionPages":{"active":{"hasMore":False,"nextCursor":None},"archived":{"hasMore":False,"nextCursor":None}},"serverTime":now()}

def frame(payload, opcode=1):
 payload=payload if isinstance(payload,bytes) else payload.encode()
 n=len(payload)
 return bytes([0x80|opcode,n]) + payload if n<126 else bytes([0x80|opcode,126])+struct.pack("!H",n)+payload if n<65536 else bytes([0x80|opcode,127])+struct.pack("!Q",n)+payload

def send_ws(client,obj):
 try:
  data=json.dumps(obj,ensure_ascii=False).encode()
  with client["lock"]: client["socket"].sendall(frame(data))
 except (OSError,ValueError):
  pass

def broadcast(scope,obj):
 for c in list(SOCKETS):
  if c["scope"]==scope: send_ws(c,obj)

def emit(sid,type,payload):
 with LOCK:
  events=EVENTS.setdefault(sid,[])
  seq=len(events)+1
  e={"protocolVersion":"1.0","eventId":f"evt_{sid}_{seq}","sequence":seq,"cursor":f"seq:{seq}","type":type,"sessionId":sid,"payload":copy.deepcopy(payload),"emittedAt":now()}
  events.append(e)
 broadcast(sid,e)
 broadcast("dashboard",dashboard())
 return e

def add_item(sid,role,text):
 with LOCK:
  items=ITEMS.setdefault(sid,[])
  item={"id":"item_"+uuid.uuid4().hex,"sessionId":sid,"type":"message","status":"done","role":role,"content":{"text":text},"orderSeq":len(items)+1,"revision":1,"updatedSeq":len(EVENTS.get(sid,[]))+1,"createdAt":now()}
  items.append(item)
 return emit(sid,"timeline.item.upserted",{"item":item})

def file_key(path):
 path=path.replace("\\","/")
 root="/workspace/demo/"
 return path[len(root):] if path.startswith(root) else path.lstrip("./")

class Handler(BaseHTTPRequestHandler):
 protocol_version="HTTP/1.1"
 def log_message(self,*args): pass
 def reply(self,value,status=200,media="application/json"):
  data=value if isinstance(value,bytes) else json.dumps(value,ensure_ascii=False).encode()
  self.send_response(status);self.send_header("Content-Type",media);self.send_header("Content-Length",str(len(data)));self.end_headers();self.wfile.write(data)
 def fail(self,status,message): self.reply({"detail":message},status)
 def do_GET(self): self.handle_route()
 def do_POST(self): self.handle_route()
 def do_PATCH(self): self.handle_route()
 def do_DELETE(self): self.handle_route()
 def handle_route(self):
  parsed=urlparse(self.path);path=parsed.path;query=parse_qs(parsed.query)
  length=int(self.headers.get("Content-Length",0))
  if length>30*1024*1024:return self.fail(413,"too large")
  raw=self.rfile.read(length) if length else b""
  body={}
  if raw and "application/json" in self.headers.get("Content-Type",""):
   try:body=json.loads(raw)
   except ValueError:return self.fail(400,"bad JSON")
  REQUESTS.append({"method":self.command,"path":path,"authorization":bool(self.headers.get("Authorization"))})
  if self.headers.get("Upgrade","").lower()=="websocket":
   return self.websocket(path,query)
  if path=="/api/v2/health":return self.reply({"status":"ok"})
  if path=="/api/v2/auth/config":return self.reply({"oauthEnabled":False,"registrationOpen":True,"serverTime":now()})
  if path=="/api/v2/auth/password-salt":return self.reply({"salt":"fixture-salt-base64url-text"})
  if path=="/api/v2/auth/login":
   verifier=base64.urlsafe_b64encode(hashlib.pbkdf2_hmac("sha256",b"synthetic-test-password",b"fixture-salt-base64url-text",120000,32)).decode().rstrip("=")
   if body.get("email")!="desktop-test@example.invalid" or body.get("passwordVerifier")!=verifier or "password" in body:return self.fail(401,"invalid credentials")
   return self.reply({"userId":"usr_demo","role":"member","accessToken":TOKEN,"tokenType":"bearer","displayName":"Desktop Test","serverTime":now()})
  if not self.headers.get("Authorization")=="Bearer "+TOKEN:return self.fail(401,"invalid user access token")
  if path=="/api/v2/auth/me":return self.reply({"userId":"usr_demo","role":"member","email":"desktop-test@example.invalid","displayName":"Desktop Test","disabled":False,"serverTime":now()})
  if path=="/api/v2/connectors":return self.reply({"connectors":CONNECTORS})
  if path=="/api/v2/projects":
   if self.command=="POST":
    if set(body)-{"name","connectorId","workspacePath","manuallyCreated"}:return self.fail(422,"extra project fields")
    project=dict(body,id="prj_"+uuid.uuid4().hex,userId="usr_demo");PROJECTS.append(project);broadcast("dashboard",dashboard());return self.reply({"project":project})
   return self.reply({"projects":PROJECTS})
  if re.fullmatch(r"/api/v2/connectors/[^/]+/runtimes",path):return self.reply({"runtimes":[{"runtimeId":"codex","runtimeType":"codex","displayName":"Codex","present":True,"configured":True,"active":True,"status":"running"}]})
  mc=re.fullmatch(r"/api/v2/connectors/[^/]+/runtimes/([^/]+)/catalogs/model",path)
  if mc:return self.reply({"catalog":{"runtime":mc[1],"revision":1,"models":[{"id":"fixture-model","displayName":"Fixture Model","selectionId":"fixture-model","default":True,"reasoningItems":[{"id":"fixture-effort","displayName":"默认","selectionId":"fixture-model","default":True}]}]},"serverTime":now()})
  if path=="/api/v2/sessions/list":return self.reply({"sessions":SESSIONS})
  if path in ["/api/v2/sessions","/api/v2/sessions/create-and-start"]:
   if set(body)-{"connectorId","projectId","runtime","runtimeId","cwd","title","selections","content","clientMessageId"}:return self.fail(422,"extra create fields")
   s={"id":"ses_"+uuid.uuid4().hex,"connectorStatus":"online","status":"idle","archived":False,**body};SESSIONS.append(s);ITEMS[s["id"]]=[]
   if body.get("content"):add_item(s["id"],"user",body["content"])
   broadcast("dashboard",dashboard());return self.reply({"session":s})
  if path in ["/api/v2/sessions/archive","/api/v2/sessions/unarchive"]:
   if not isinstance(body,list):return self.fail(422,"expected array")
   for sid in body:
    s=session(sid)
    if s:s["archived"]=path.endswith("/archive")
   broadcast("dashboard",dashboard());return self.reply({"ok":True})
  if path=="/api/v2/ws-ticket":
   scope=body.get("scope",{})
   if set(body)!={"clientId","scope"} or bool(scope.get("dashboard"))==bool(scope.get("sessionId")):return self.fail(422,"invalid scope")
   ticket=uuid.uuid4().hex;TICKETS[ticket]="dashboard" if scope.get("dashboard") else scope["sessionId"]
   return self.reply({"ticket":ticket,"expiresAt":now(),"serverTime":now()})
  if path=="/api/v2/test/stats":return self.reply({**STATS,"requests":REQUESTS,"sequence":len(EVENTS.get("ses_demo",[])),"shellCommands":SHELL_COMMANDS})
  if path=="/api/v2/test/reset":
   initial_notices()
   SHELL_COMMANDS.clear()
   return self.reply({"ok":True})
  if path=="/api/v2/test/disconnect":
   for c in list(SOCKETS):
    if c["scope"]=="ses_demo":
     try:
      with c["lock"]:c["socket"].sendall(frame(struct.pack("!H",1000),8));c["socket"].shutdown(socket.SHUT_RDWR)
     except OSError:pass
   add_item("ses_demo","assistant","断线期间的消息已通过 cursor 补齐。")
   return self.reply({"ok":True})
  match=re.fullmatch(r"/api/v2/sessions/([^/]+)/(.*)",path)
  if match:
   sid,op=match.groups()
   if not session(sid):return self.fail(404,"session not found")
   if op=="snapshot":return self.reply(snapshot(sid))
   if op=="events":
    STATS["recoveries"]+=1
    after=int(query.get("after",["seq:0"])[0].split(":")[1]);events=EVENTS.get(sid,[])
    return self.reply({"events":[e for e in events if e["sequence"]>after],"nextCursor":f"seq:{len(events)}","snapshotRequired":False})
   if op=="timeline":
    before=int(query.get("beforeOrderSeq",["999999"])[0]);items=[i for i in ITEMS.get(sid,[]) if i["orderSeq"]<before]
    return self.reply({"items":items[-100:],"hasMore":len(items)>100,"nextSeq":len(ITEMS[sid])})
   if op=="meta" and self.command=="PATCH":session(sid).update(body);emit(sid,"session.updated",{"session":session(sid)});return self.reply({"session":session(sid)})
   if op=="takeover":
    session(sid)["takeover"]=self.command!="DELETE";emit(sid,"session.updated",{"session":session(sid)});return self.reply({"session":session(sid)})
   if op=="runtime/commands":
    if self.command=="GET":return self.reply({"commands":[{"id":"compact","title":"压缩上下文","description":"Synthetic command","enabled":True,"acceptsArgs":True}]})
    if not isinstance(body.get("args"),list):return self.fail(422,"args must be a list")
    add_item(sid,"assistant","命令执行成功："+body["command"]);return self.reply({"ok":True,"command":body["command"],"message":"合成命令执行成功"})
   if op=="runtime/selections" and self.command=="PATCH":
    if set(body)!={"selections"}:return self.fail(422,"invalid selections payload")
    STATE[sid]=body["selections"];emit(sid,"runtime.state.updated",{"state":state(sid)});return self.reply({"ok":True,"state":state(sid)})
   if op in ["runtime/messages","runtime/steer"]:
    if set(body)-{"content","clientMessageId","attachments"} or not isinstance(body.get("content"),str):return self.fail(422,"invalid message payload")
    STATS["messagePosts"]+=1
    key=(sid,body.get("clientMessageId"))
    if key not in CLIENT_MESSAGES:
     CLIENT_MESSAGES[key]=body;STATS["uniqueMessages"]+=1
     add_item(sid,"user",body["content"]);add_item(sid,"assistant","已收到任务："+body["content"])
    return self.reply({"ok":True,"result":{}})
   if op=="runtime/interrupt":session(sid)["status"]="idle";emit(sid,"runtime.state.updated",{"state":state(sid)});return self.reply({"ok":True})
   if op=="runtime/notices":return self.reply({"notices":NOTICES.get(sid,[])})
   nm=re.fullmatch(r"runtime/notices/([^/]+)/respond",op)
   if nm:
    notice=next((n for n in NOTICES.get(sid,[]) if n["noticeId"]==nm[1]),None)
    if notice is None:return self.fail(404,"notice not found")
    if body.get("actionId") not in [a["actionId"] for a in notice["actions"]]:return self.fail(422,"invalid action")
    if body["actionId"]=="reply" and not body.get("input",{}).get("answer"):return self.fail(422,"answer required")
    notice["status"]="resolved";notice["revision"]+=1;emit(sid,"runtime.notice.updated",{"notice":notice});return self.reply({"ok":True})
   if op=="attachments" and self.command=="POST":
    message=BytesParser(policy=default).parsebytes(("Content-Type: "+self.headers["Content-Type"]+"\r\nMIME-Version: 1.0\r\n\r\n").encode()+raw)
    result=[]
    for part in message.iter_parts():
     if part.get_param("name",header="content-disposition")!="files":return self.fail(422,"multipart field must be files")
     data=part.get_payload(decode=True);fid="file_"+uuid.uuid4().hex
     obj={"fileId":fid,"sessionId":sid,"name":part.get_filename(),"size":len(data),"sha256":hashlib.sha256(data).hexdigest(),"mediaType":part.get_content_type()};UPLOADS[fid]=(obj,data);result.append(obj)
    return self.reply({"attachments":result})
   am=re.fullmatch(r"attachments/([^/]+)",op)
   if am and am[1] in UPLOADS:
    obj,data=UPLOADS[am[1]];return self.reply({**obj,"contentBase64":base64.b64encode(data).decode()})
  sh=re.fullmatch(r"/api/v2/connectors/([^/]+)/shell/exec",path)
  if sh:
   if not query.get("root"):return self.fail(422,"root required")
   if set(body)!={"command","cwd","timeoutMs"}:return self.fail(422,"invalid shell request")
   if not isinstance(body.get("command"),str) or not body["command"]:return self.fail(422,"command required")
   record={"connectorId":sh[1],"root":query["root"][0],"cwd":body.get("cwd","."),"command":body["command"]}
   SHELL_COMMANDS.append(record)
   if "deny" in body["command"]:
    return self.reply({"ok":True,"result":{**record,"exitCode":1,"timedOut":False,"durationMs":1,"stdout":"","stderr":"synthetic denial"}})
   return self.reply({"ok":True,"result":{**record,"exitCode":0,"timedOut":False,"durationMs":1,"stdout":"","stderr":""}})
  fm=re.fullmatch(r"/api/v2/connectors/([^/]+)/fs/(.*)",path)
  if fm:
   connector,op=fm.groups();key=file_key(body.get("path","."))
   if op=="list":
    directory="" if key in ["","."] else key.rstrip("/")+"/"
    entries=[]
    for name,data in FILES.items():
     if not name.startswith(directory):continue
     rest=name[len(directory):];part=rest.split("/")[0]
     obj={"name":part,"path":"/workspace/demo/"+directory+part,"type":"directory" if "/" in rest else "file","size":len(data)}
     if not any(e["name"]==part for e in entries):entries.append(obj)
    return self.reply({"ok":True,"result":{"path":"/workspace/demo/"+directory.rstrip("/"),"entries":entries,"truncated":False}})
   if op in ["readText","read"]:
    if key not in FILES:
     # The real server forwards the connector RPC error verbatim as 502, not 404.
     return self.fail(502,"file not found: "+str(body.get("path","")))
    data=FILES[key];meta={"path":"/workspace/demo/"+key,"name":key.split("/")[-1],"size":len(data),"sha256":hashlib.sha256(data).hexdigest()}
    if op=="read":return self.reply({"ok":True,"result":{**meta,"downloadUrl":"/api/v2/connectors/con_demo/fs/transfers/"+base64.urlsafe_b64encode(key.encode()).decode()}})
    binary=b"\x00" in data
    return self.reply({**meta,"content":"" if binary else data[:body.get("maxBytes",1048576)].decode("utf-8","replace"),"binary":binary,"truncated":len(data)>body.get("maxBytes",1048576),"encoding":"utf8","serverTime":now()})
   if op.startswith("transfers/"):
    try:key=base64.urlsafe_b64decode(op.split("/")[1]).decode()
    except Exception:return self.fail(404,"missing transfer")
    return self.reply(FILES[key],media="application/octet-stream")
   if op=="write":
    if body.get("encoding") not in ["utf8","utf-8"]:return self.fail(422,"encoding required")
    if body.get("ifMatch") and (key not in FILES or hashlib.sha256(FILES[key]).hexdigest()!=body["ifMatch"]):return self.fail(412,"file changed")
    FILES[key]=body["content"].encode();return self.reply({"ok":True,"result":{"sha256":hashlib.sha256(FILES[key]).hexdigest()}})
  tm=re.fullmatch(r"/api/v2/connectors/([^/]+)/terminals-v2(?:/([^/]+))?",path)
  if tm:
   connector,tid=tm.groups()
   if self.command=="POST":
    tid="trm_"+uuid.uuid4().hex;TERMINALS[tid]={"terminalId":tid,"cols":body["cols"],"rows":body["rows"],"status":"running"}
    TERMINAL_CHUNKS[tid]=[b"Mock terminal (no real shell)\r\n\x1b[32mgreen ok\x1b[0m \x1b[1;31mbold red\x1b[0m \x1b[38;5;208m256-orange\x1b[0m\r\n> "]
    return self.reply({"ok":True,"result":TERMINALS[tid]})
   if self.command=="DELETE":TERMINALS.pop(tid,None);return self.reply({"ok":True})
  return self.fail(404,"fixture route not found: "+path)

 def websocket(self,path,query):
  terminal=re.fullmatch(r"/api/v2/connectors/[^/]+/terminals-v2/([^/]+)/stream",path)
  if terminal:
   if query.get("token",[""])[0]!=TOKEN or terminal[1] not in TERMINALS:return self.fail(401,"bad terminal token")
   scope="terminal:"+terminal[1];STATS["terminalConnections"]+=1
  else:
   ticket=query.get("ticket",[""])[0];scope=TICKETS.pop(ticket,None)
   if scope is None:return self.fail(401,"bad ticket")
   STATS["dashboardConnections" if scope=="dashboard" else "sessionConnections"]+=1
  key=self.headers.get("Sec-WebSocket-Key","")
  self.send_response(101);self.send_header("Upgrade","websocket");self.send_header("Connection","Upgrade");self.send_header("Sec-WebSocket-Accept",base64.b64encode(hashlib.sha1((key+"258EAFA5-E914-47DA-95CA-C5AB0DC85B11").encode()).digest()).decode());self.end_headers()
  client={"scope":scope,"socket":self.connection,"lock":threading.Lock()};SOCKETS.append(client)
  try:
   if scope=="dashboard":send_ws(client,dashboard())
   if terminal:
    chunks=TERMINAL_CHUNKS[terminal[1]];start=int(query.get("fromSeq",["0"])[0]);send_ws(client,{"type":"replay","seq":len(chunks),"data":base64.b64encode(b"".join(chunks[start:])).decode()})
   while True:
    head=self.rfile.read(2)
    if len(head)<2:break
    opcode=head[0]&15;n=head[1]&127;masked=head[1]&128
    if n==126:n=struct.unpack("!H",self.rfile.read(2))[0]
    elif n==127:n=struct.unpack("!Q",self.rfile.read(8))[0]
    if n>5*1024*1024:break
    mask=self.rfile.read(4) if masked else b""
    payload=self.rfile.read(n)
    if masked:payload=bytes(b^mask[i%4] for i,b in enumerate(payload))
    if opcode==8:break
    if opcode==9:
     with client["lock"]:self.connection.sendall(frame(payload,10))
     continue
    if opcode!=1:continue
    message=json.loads(payload)
    if terminal:
     tid=terminal[1]
     if message.get("type")=="input":
      data=base64.b64decode(message["data"]);out=b"echo: "+data.replace(b"\r",b"\r\n")+b"\r\n> ";chunks=TERMINAL_CHUNKS[tid];chunks.append(out)
      send_ws(client,{"type":"output","seq":len(chunks),"data":base64.b64encode(out).decode()})
     if message.get("type")=="resize":TERMINALS[tid].update(cols=message["cols"],rows=message["rows"])
    if message.get("type")=="ping":send_ws(client,{"type":"pong"})
  except (OSError,ValueError,struct.error):pass
  finally:
   if client in SOCKETS:SOCKETS.remove(client)

def initial_notices():
 NOTICES["ses_demo"]=[
  {"noticeId":"notice_approval","sessionId":"ses_demo","type":"interaction","title":"批准操作","message":"Agent 请求读取工作区文件。","status":"open","interactionType":"approval","responseRequired":True,"revision":1,"context":{"command":"读取 README.md（合成测试）"},"actions":[{"actionId":"allow","label":"允许","input":{"required":False}},{"actionId":"deny","label":"拒绝","input":{"required":False}}]},
  {"noticeId":"notice_input","sessionId":"ses_demo","type":"interaction","title":"需要你的输入","message":"选择目标平台。","status":"open","interactionType":"input","responseRequired":True,"revision":1,"actions":[{"actionId":"reply","label":"提交回复","input":{"required":True,"schema":{"type":"object","properties":{"answer":{"type":"string","title":"目标平台","enum":["Windows","Mobile"]}},"required":["answer"]}}}]},
 {'noticeId': 'notice_question', 'sessionId': 'ses_demo', 'type': 'interaction', 'title': 'Claude needs your input', 'status': 'open', 'interactionType': 'input_request', 'responseRequired': True, 'revision': 1, 'context': {'inputStatus': 'pending', 'requestKind': 'questionnaire', 'toolName': 'AskUserQuestion', 'toolInput': {'questions': [{'question': 'Which platform?', 'header': '平台', 'options': [{'label': 'Windows', 'description': '桌面'}, {'label': 'Mobile'}]}]}}, 'actions': [{'actionId': 'submit', 'label': 'Submit', 'input': {'required': True, 'schema': {'type': 'object', 'required': ['answers'], 'properties': {'answers': {'type': 'object', 'required': ['platform'], 'properties': {'platform': {'type': 'object', 'properties': {'optionIds': {'type': 'array', 'items': {'type': 'string', 'enum': ['windows', 'mobile']}}, 'customText': {'type': 'string'}}}}}}}, 'uiSchema': {'component': 'inputRequest', 'version': 1, 'questions': [{'id': 'platform', 'prompt': 'Which platform?', 'multiple': False, 'allowCustom': True, 'options': [{'id': 'windows', 'label': 'Windows', 'description': '桌面'}, {'id': 'mobile', 'label': 'Mobile'}]}]}}}, {'actionId': 'cancel', 'label': 'Cancel'}]}]
initial_notices()
if __name__=="__main__":
 server=ThreadingHTTPServer(("127.0.0.1",18743),Handler)
 server.daemon_threads=True
 print("Fixture listening at http://127.0.0.1:18743 (synthetic data only)",flush=True)
 server.serve_forever()

