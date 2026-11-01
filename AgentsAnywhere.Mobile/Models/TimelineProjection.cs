using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;
namespace AgentsAnywhere.Mobile.Models
{
 public enum RowKind { User, Process, Output, Marker, Error, Notice }
 public enum StepKind { Reasoning, Tool, Note, Approval, System, Error, More }
 public enum StepState { Done, Running, Failed, Stopped }

 public sealed class ProcessStep : INotifyPropertyChanged {
  public string Id { get; set; }
  public StepKind Kind { get; set; }
  public StepState State { get; set; }
  public string Glyph { get; set; } = "";
  public string Title { get; set; } = "";
  public string Subtitle { get; set; } = "";
  public string Body { get; set; } = "";
  public bool Expanded { get; set; }
  public bool IsLast { get; set; }
  public bool IsReasoning { get { return Kind == StepKind.Reasoning; } }
  public bool IsNote { get { return Kind == StepKind.Note; } }
  public bool IsTool { get { return !IsReasoning && !IsNote; } }
  public bool CanExpand { get { return Kind == StepKind.More || Body.Length > 0 && Kind != StepKind.Note; } }
  public bool ShowOutput { get { return Expanded && IsTool && Body.Length > 0; } }
  public string Signature { get { return Id + "|" + Kind + "|" + State + "|" + Title + "|" + Subtitle + "|" + Body.Length + "|" + Body.GetHashCode() + "|" + Expanded + "|" + IsLast; } }
  public event PropertyChangedEventHandler PropertyChanged;
  void Raise(string name) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
  // Streaming deltas arrive many times per second, so rows are mutated in place:
  // replacing the item would make the virtualized list rebuild its containers.
  public void CopyFrom(ProcessStep other) {
   if (Kind != other.Kind) { Kind = other.Kind; Raise("Kind"); Raise("IsReasoning"); Raise("IsNote"); Raise("IsTool"); Raise("CanExpand"); Raise("ShowOutput"); }
   if (State != other.State) { State = other.State; Raise("State"); }
   if (Glyph != other.Glyph) { Glyph = other.Glyph; Raise("Glyph"); }
   if (Title != other.Title) { Title = other.Title; Raise("Title"); }
   if (Subtitle != other.Subtitle) { Subtitle = other.Subtitle; Raise("Subtitle"); }
   if (Body != other.Body) { Body = other.Body; Raise("Body"); Raise("CanExpand"); Raise("ShowOutput"); }
   if (Expanded != other.Expanded) { Expanded = other.Expanded; Raise("Expanded"); Raise("ShowOutput"); }
   if (IsLast != other.IsLast) { IsLast = other.IsLast; Raise("IsLast"); }
  }
 }

 public sealed class TimelineRow : INotifyPropertyChanged {
  public string Key { get; set; }
  public RowKind Kind { get; set; }
  public string Text { get; set; } = "";
  public string Meta { get; set; } = "";
  public string Footer { get; set; } = "";
  public IReadOnlyList<string> Attachments { get; set; } = new string[0];
  public IReadOnlyList<ProcessStep> Steps { get; set; } = new ProcessStep[0];
  public string Summary { get; set; } = "";
  public bool Running { get; set; }
  public bool Expanded { get; set; }
  public bool Failed { get; set; }
  public bool Stopped { get; set; }
  public JObject Notice { get; set; }
  public string Detail { get; set; } = "";
  public bool HasAttachments { get { return Attachments.Count > 0; } }
  public bool HasFooter { get { return Footer.Length > 0; } }
  public bool HasDetail { get { return Detail.Length > 0; } }
  public bool HasMeta { get { return Meta.Length > 0; } }
  public string Signature { get {
   var b = new StringBuilder();
   b.Append(Kind).Append('|').Append(Text.Length).Append(':').Append(Text.GetHashCode()).Append('|').Append(Meta).Append('|').Append(Footer).Append('|')
    .Append(String.Join(",", Attachments)).Append('|').Append(Summary).Append('|').Append(Running).Append(Expanded).Append(Failed).Append(Stopped).Append('|').Append(Detail)
    .Append('|').Append(Notice == null ? "" : Notice.ToString(Newtonsoft.Json.Formatting.None));
   foreach (var s in Steps) b.Append('#').Append(s.Signature);
   return b.ToString();
  }}
  public event PropertyChangedEventHandler PropertyChanged;
  void Raise(string name) { var handler = PropertyChanged; if (handler != null) handler(this, new PropertyChangedEventArgs(name)); }
  /// <summary>Applies a re-projected row in place. Replacing the item instead would
  /// rebuild its container on every streaming delta, which is what made the list flicker.</summary>
  public void CopyFrom(TimelineRow other) {
   if (Text != other.Text) { Text = other.Text; Raise("Text"); }
   if (Meta != other.Meta) { Meta = other.Meta; Raise("Meta"); Raise("HasMeta"); }
   if (Footer != other.Footer) { Footer = other.Footer; Raise("Footer"); Raise("HasFooter"); }
   if (Summary != other.Summary) { Summary = other.Summary; Raise("Summary"); }
   if (Running != other.Running) { Running = other.Running; Raise("Running"); }
   if (Expanded != other.Expanded) { Expanded = other.Expanded; Raise("Expanded"); }
   if (Failed != other.Failed) { Failed = other.Failed; Raise("Failed"); }
   if (Stopped != other.Stopped) { Stopped = other.Stopped; Raise("Stopped"); }
   if (Detail != other.Detail) { Detail = other.Detail; Raise("Detail"); Raise("HasDetail"); }
   if (!JToken.DeepEquals(Notice, other.Notice)) { Notice = other.Notice; Raise("Notice"); }
   if (!SameAttachments(Attachments, other.Attachments)) { Attachments = other.Attachments; Raise("Attachments"); Raise("HasAttachments"); }
   if (!SameSteps(Steps, other.Steps)) {
    if (Steps.Count == other.Steps.Count && Steps.Select(x => x.Id).SequenceEqual(other.Steps.Select(x => x.Id))) {
     for (int i = 0; i < Steps.Count; i++) Steps[i].CopyFrom(other.Steps[i]);
    } else {
     Steps = other.Steps; Raise("Steps");
    }
   }
  }
  static bool SameAttachments(IReadOnlyList<string> left, IReadOnlyList<string> right) {
   if (ReferenceEquals(left, right)) return true;
   if (left == null || right == null || left.Count != right.Count) return false;
   for (int i = 0; i < left.Count; i++) if (left[i] != right[i]) return false;
   return true;
  }
  static bool SameSteps(IReadOnlyList<ProcessStep> left, IReadOnlyList<ProcessStep> right) {
   if (ReferenceEquals(left, right)) return true;
   if (left == null || right == null || left.Count != right.Count) return false;
   for (int i = 0; i < left.Count; i++) if (left[i].Signature != right[i].Signature) return false;
   return true;
  }
 }

 /// <summary>Groups timeline items into turns: user message, one process rail, final output.</summary>
 public sealed class TimelineProjector {
  public const int RunningVisibleSteps = 5;
  readonly HashSet<string> toggled = new HashSet<string>();
  public string AgentName { get; set; } = "Agent";

  public void Toggle(string key) { if (!toggled.Remove(key)) toggled.Add(key); }
  public void ClearToggles() { toggled.Clear(); }
  bool Flip(string key, bool fallback) { return toggled.Contains(key) ? !fallback : fallback; }

  enum EntryKind { Step, Assistant, Marker, Error }
  sealed class Entry { public EntryKind Kind; public TimelineItem Item; public ProcessStep Step; public string Text; public IReadOnlyList<string> Attachments; }
  sealed class Turn { public List<Entry> Entries = new List<Entry>(); public TimelineItem Start; public TimelineItem End; }

  public List<TimelineRow> Project(IReadOnlyList<TimelineItem> items, IEnumerable<JObject> notices, bool sessionBusy) {
   var rows = new List<TimelineRow>();
   var turn = new Turn();
   var visible = items.Where(x => x.Data.Text("status") != "hidden").ToList();
   for (int i = 0; i < visible.Count; i++) {
    var item = visible[i];
    var type = item.Data.Text("type");
    if (type == "turn.start") { Flush(rows, turn, false, sessionBusy); turn = new Turn { Start = item }; continue; }
    if (type == "turn.end") { turn.End = item; Flush(rows, turn, false, sessionBusy); turn = new Turn(); continue; }
    if (type == "message" && item.Data.Text("role") == "user") {
     Flush(rows, turn, false, sessionBusy); turn = new Turn { Start = item };
     rows.Add(new TimelineRow { Key = "u:" + item.Id, Kind = RowKind.User, Text = MessageText(item.Data), Meta = Clock(item), Attachments = AttachmentNames(item.Data) });
     continue;
    }
    var entry = Classify(item);
    if (entry != null) turn.Entries.Add(entry);
   }
   Flush(rows, turn, true, sessionBusy);
   foreach (var n in notices) {
    var context = n["context"] as JObject;
    var detail = context?.Text("command", context?.Text("path", context?.Text("description"))) ?? "";
    rows.Add(new TimelineRow { Key = "n:" + n.Text("noticeId"), Kind = RowKind.Notice, Text = n.Text("title", "Agent 请求"), Meta = n.Text("message"), Detail = detail, Notice = n });
   }
   return rows;
  }

  void Flush(List<TimelineRow> rows, Turn turn, bool last, bool busy) {
   if (turn.Entries.Count == 0) return;
   int lastStep = turn.Entries.FindLastIndex(x => x.Kind == EntryKind.Step);
   var steps = new List<ProcessStep>();
   for (int i = 0; i < lastStep; i++) {
    var e = turn.Entries[i];
    if (e.Kind == EntryKind.Step) steps.Add(e.Step);
    else if (e.Kind == EntryKind.Assistant) steps.Add(new ProcessStep { Id = e.Item.Id, Kind = StepKind.Note, Body = e.Text, Title = e.Text });
    else steps.Add(new ProcessStep { Id = e.Item.Id, Kind = e.Kind == EntryKind.Error ? StepKind.Error : StepKind.System, Glyph = e.Kind == EntryKind.Error ? "\uE783" : "\uE946", Title = e.Text, Subtitle = e.Kind == EntryKind.Error ? "错误" : "系统", State = e.Kind == EntryKind.Error ? StepState.Failed : StepState.Done });
   }
   if (lastStep >= 0) steps.Add(turn.Entries[lastStep].Step);
   var trailing = turn.Entries.Skip(lastStep + 1).ToList();
   bool hasOutput = trailing.Any(x => x.Kind == EntryKind.Assistant);
   string endStatus = turn.End?.Data.Text("status") ?? "";
   bool stopped = endStatus == "interrupted" || endStatus == "cancelled" || steps.Any(x => x.State == StepState.Stopped);
   bool running = steps.Any(x => x.State == StepState.Running) || last && busy && turn.End == null;
   string duration = Duration(turn);
   if (steps.Count > 0) {
    var key = "p:" + steps[0].Id;
    int failed = steps.Count(x => x.State == StepState.Failed);
    bool expanded = Flip(key, running || failed > 0 || stopped);
    var shown = steps;
    if (running && expanded && steps.Count > RunningVisibleSteps + 1 && !toggled.Contains("all:" + key)) {
     var hidden = steps.Take(steps.Count - RunningVisibleSteps).ToList();
     shown = new List<ProcessStep> { new ProcessStep { Id = "all:" + key, Kind = StepKind.More, Glyph = "\uE712", Title = "显示前面 " + hidden.Count + " 步", Subtitle = Counts(hidden, false) } };
     shown.AddRange(steps.Skip(hidden.Count));
    }
    foreach (var s in shown) if (s.Kind != StepKind.More) s.Expanded = Flip(s.Id, s.State == StepState.Running || s.State == StepState.Failed && s.Kind == StepKind.Tool);
    for (int i = 0; i < shown.Count; i++) shown[i].IsLast = i == shown.Count - 1;
    var summary = running ? "第 " + steps.Count + " 步 · " + Counts(steps, true) : Counts(steps, true);
    if (stopped) summary += " · 已打断";
    else if (!running && duration.Length > 0) summary += " · " + duration;
    rows.Add(new TimelineRow { Key = key, Kind = RowKind.Process, Steps = shown, Summary = summary, Running = running, Expanded = expanded, Failed = failed > 0, Stopped = stopped });
   }
   bool finished = !running && (turn.End != null || !last || !busy);
   var lastOutput = trailing.LastOrDefault(x => x.Kind == EntryKind.Assistant);
   foreach (var e in trailing) {
    if (e.Kind == EntryKind.Assistant) {
     var footer = "";
     if (e == lastOutput && finished && (steps.Count > 0 || turn.End != null)) footer = stopped ? "已打断" : duration.Length > 0 ? "已完成 · 用时 " + duration : "已完成";
     rows.Add(new TimelineRow { Key = "o:" + e.Item.Id, Kind = RowKind.Output, Text = e.Text, Meta = AgentName + " · " + Clock(e.Item), Footer = footer, Attachments = e.Attachments });
    } else rows.Add(new TimelineRow { Key = "m:" + e.Item.Id, Kind = e.Kind == EntryKind.Error ? RowKind.Error : RowKind.Marker, Text = e.Text });
   }
   if (stopped && !hasOutput) rows.Add(new TimelineRow { Key = "x:" + (turn.End?.Id ?? steps.LastOrDefault()?.Id ?? turn.Entries[0].Item.Id), Kind = RowKind.Error, Text = "本轮已被打断。可以直接发送新消息继续。" });
  }

  static string Counts(IEnumerable<ProcessStep> steps, bool failures) {
   var list = steps.ToList();
   var parts = new List<string>();
   int think = list.Count(x => x.Kind == StepKind.Reasoning), tools = list.Count(x => x.Kind == StepKind.Tool || x.Kind == StepKind.Approval), failed = list.Count(x => x.State == StepState.Failed);
   if (think > 0) parts.Add("思考 " + think);
   if (tools > 0) parts.Add("工具 " + tools);
   if (failures && failed > 0) parts.Add(failed + " 失败");
   if (parts.Count == 0) parts.Add(list.Count + " 步");
   return String.Join(" · ", parts);
  }

  Entry Classify(TimelineItem item) {
   var d = item.Data; var c = d["content"] as JObject ?? new JObject();
   var type = d.Text("type"); var kind = c.Text("kind");
   bool failed = d.Text("status") == "failed";
   switch (type) {
    case "message": {
     var text = MessageText(d); var files = AttachmentNames(d);
     if (d.Text("role") == "assistant") return text.Length == 0 && files.Count == 0 ? null : new Entry { Kind = EntryKind.Assistant, Item = item, Text = text, Attachments = files };
     return text.Length == 0 ? null : new Entry { Kind = failed ? EntryKind.Error : EntryKind.Marker, Item = item, Text = text };
    }
    case "system":
     if (kind == "reasoning") return StepEntry(item, Reasoning(item, c));
     if (kind == "compact") return Compact(item, c);
     return new Entry { Kind = kind == "error" || failed ? EntryKind.Error : EntryKind.Marker, Item = item, Text = First(c, "message", "text", "rawText") ?? kind };
    case "marker":
     if (kind == "compact") return Compact(item, c);
     return new Entry { Kind = kind == "error" || failed ? EntryKind.Error : EntryKind.Marker, Item = item, Text = First(c, "label", "title", "text", "message") ?? kind };
    case "tool": return StepEntry(item, Tool(item, c));
    case "artifact": return kind == "diff" ? null : StepEntry(item, Tool(item, c));
    default: return new Entry { Kind = EntryKind.Marker, Item = item, Text = (type.Length > 0 ? type : "unknown") + " / " + (kind.Length > 0 ? kind : "unknown") };
   }
  }
  static Entry StepEntry(TimelineItem item, ProcessStep step) { return new Entry { Kind = EntryKind.Step, Item = item, Step = step }; }
  static Entry Compact(TimelineItem item, JObject c) {
   var state = c.Text("state"); var status = item.Data.Text("status");
   if (state == "failed" || status == "failed") return new Entry { Kind = EntryKind.Error, Item = item, Text = "上下文压缩失败" };
   bool active = state == "started" || state == "running" || state == "inProgress" || status == "pending" || status == "running";
   return new Entry { Kind = EntryKind.Marker, Item = item, Text = active ? "正在压缩上下文…" : "上下文已压缩" };
  }

  static StepState State(string status) {
   switch (status) {
    case "pending": case "running": case "inProgress": case "waiting_approval": return StepState.Running;
    case "failed": return StepState.Failed;
    case "interrupted": case "cancelled": return StepState.Stopped;
    default: return StepState.Done;
   }
  }

  static ProcessStep Reasoning(TimelineItem item, JObject c) {
   var summaries = c.Objects("summaries").Select(x => x.Text("text")).Where(x => x.Length > 0).ToList();
   var body = summaries.Count > 0 ? String.Join("\n\n", summaries) : First(c, "rawText", "text") ?? "";
   var state = State(item.Data.Text("status"));
   var seconds = Seconds(c);
   var title = (state == StepState.Running ? "正在思考" : "思考") + (seconds.Length > 0 ? " · " + seconds : "");
   return new ProcessStep { Id = item.Id, Kind = StepKind.Reasoning, State = state, Glyph = "\uE82F", Title = title, Body = Clip(body) };
  }

  static ProcessStep Tool(TimelineItem item, JObject c) {
   var status = item.Data.Text("status");
   var step = new ProcessStep { Id = item.Id, Kind = status == "waiting_approval" ? StepKind.Approval : StepKind.Tool, State = State(status) };
   var kind = c.Text("kind"); var input = c["input"] as JObject;
   string label;
   switch (kind) {
    case "command": {
     var command = CommandText(c["command"]); if (command.Length == 0) command = CommandText(input?["command"]); if (command.Length == 0) command = CommandText(input?["cmd"]);
     step.Glyph = "\uE756"; step.Title = command.Length > 0 ? command : c.Text("description", "命令"); label = "命令";
     var exit = c.Text("exitCode"); if (exit.Length > 0 && exit != "0") label += " · 退出码 " + exit;
     step.Body = Clip(String.Join("\n", new[] { First(c, "output", "outputPreview", "outputText") ?? "", Summary(c["error"]) }.Where(x => x.Length > 0)));
     break;
    }
    case "file_change": {
     var changes = c.Objects("changes").ToList(); var first = changes.FirstOrDefault() ?? new JObject();
     step.Glyph = "\uE70F"; step.Title = First(first, "path", "filePath", "file", "uri") ?? First(c, "path", "filePath") ?? "文件";
     label = ChangeLabel(first); if (changes.Count > 1) label += " · 共 " + changes.Count + " 个文件";
     var stats = DiffStats(changes); if (stats.Length > 0) label += " · " + stats;
     step.Body = Clip(String.Join("\n", changes.Select(x => x.Text("diff")).Where(x => x.Length > 0)));
     break;
    }
    case "web_search": {
     var query = c.Text("query", input?.Text("query") ?? "");
     step.Glyph = "\uE774"; step.Title = query.Length > 0 ? query : "网页搜索"; label = "网页搜索"; step.Body = Clip(Summary(c["output"]));
     break;
    }
    case "mcp": {
     step.Glyph = "\uE943"; step.Title = c.Text("server", input?.Text("server") ?? "mcp") + " / " + c.Text("tool", input?.Text("tool") ?? "tool"); label = "MCP 工具";
     step.Body = Clip(Join(Summary(c["output"]), Summary(c["error"])));
     break;
    }
    case "agent_call": {
     step.Glyph = "\uE716"; step.Title = First(c, "description", "title") ?? "子 Agent"; label = "子 Agent" + (c.Text("action").Length > 0 ? " · " + c.Text("action") : "");
     step.Body = Clip(Join(Summary(c["output"]), Summary(c["error"])));
     break;
    }
    default: {
     var name = First(c, "toolName", "name", "tool", "title", "function") ?? (kind.Length > 0 ? kind : "工具");
     var target = Target(c);
     string glyph, verb; ToolVerb(name, out glyph, out verb);
     step.Glyph = item.Data.Text("type") == "artifact" && kind != "file_change" ? "\uE8A5" : glyph;
     step.Title = target.Length > 0 ? target : name; label = verb ?? name;
     step.Body = Clip(Join(Summary(c["output"]), Summary(c["error"])));
     break;
    }
   }
   if (step.Kind == StepKind.Approval) { step.Glyph = "\uE83D"; label = "等待你批准 · " + label; }
   else if (step.State == StepState.Failed && !label.Contains("退出码")) label += " · 失败";
   else if (step.State == StepState.Stopped) label += " · 已打断";
   else if (step.State == StepState.Running) label += " · 运行中";
   step.Subtitle = label;
   return step;
  }

  static void ToolVerb(string name, out string glyph, out string verb) {
   switch (name.ToLowerInvariant()) {
    case "read": case "read_file": case "view": glyph = "\uE8A5"; verb = "读取文件"; return;
    case "grep": case "glob": case "search": case "find": glyph = "\uE721"; verb = "搜索"; return;
    case "edit": case "multiedit": case "write": case "apply_patch": glyph = "\uE70F"; verb = "修改文件"; return;
    case "bash": case "shell": case "exec": glyph = "\uE756"; verb = "命令"; return;
    case "webfetch": case "websearch": glyph = "\uE774"; verb = "网页"; return;
    case "task": case "agent": glyph = "\uE716"; verb = "子 Agent"; return;
    default: glyph = "\uE90F"; verb = null; return;
   }
  }
  static string ChangeLabel(JObject change) {
   var k = change["kind"] as JObject;
   var type = (k?.Text("type") ?? change.Text("kind", First(change, "action", "type", "status") ?? "")).ToLowerInvariant();
   switch (type) {
    case "add": case "added": case "create": case "created": return "新建文件";
    case "delete": case "deleted": case "remove": case "removed": return "删除文件";
    case "rename": case "renamed": case "move": case "moved": return "重命名文件";
    default: return "修改文件";
   }
  }
  static string DiffStats(IEnumerable<JObject> changes) {
   int add = 0, del = 0;
   foreach (var line in changes.SelectMany(x => x.Text("diff").Split('\n'))) {
    if (line.StartsWith("+++") || line.StartsWith("---")) continue;
    if (line.StartsWith("+")) add++; else if (line.StartsWith("-")) del++;
   }
   return add + del == 0 ? "" : "+" + add + " −" + del;
  }
  static string Target(JObject c) {
   var t = First(c, "path", "filePath", "file", "uri", "query", "url"); if (t != null) return t;
   var input = c["input"] as JObject;
   if (input != null) { t = First(input, "file_path", "filePath", "notebook_path", "path", "file", "uri", "pattern", "query", "url"); if (t != null) return t; }
   var command = CommandText(c["command"]); if (command.Length == 0) command = CommandText(input?["command"]); if (command.Length == 0) command = CommandText(input?["cmd"]);
   return command;
  }

  public static string MessageText(JObject item) {
   var c = item["content"] as JObject ?? new JObject();
   var t = First(c, "text", "message", "description", "rawText"); if (t != null) return t.Trim();
   var nested = c["content"];
   if (nested?.Type == JTokenType.String) return ((string)nested).Trim();
   if (nested is JObject) return (First((JObject)nested, "text", "message", "description", "rawText") ?? "").Trim();
   if (nested is JArray) return String.Join("\n", ((JArray)nested).Select(p => p.Type == JTokenType.String ? (string)p : p is JObject ? First((JObject)p, "text", "message", "description", "rawText") : null).Where(x => !String.IsNullOrWhiteSpace(x))).Trim();
   return "";
  }
  static IReadOnlyList<string> AttachmentNames(JObject item) {
   var c = item["content"] as JObject ?? new JObject();
   var nested = c["content"] as JObject;
   return c.Objects("attachments").Concat(nested?.Objects("attachments") ?? Enumerable.Empty<JObject>())
    .Select(x => First(x, "name", "fileName", "filename", "fileId") ?? "附件").Distinct().ToList();
  }
  static string First(JObject o, params string[] keys) {
   foreach (var k in keys) {
    var v = o?[k];
    if (v == null || v.Type == JTokenType.Null || v is JContainer) continue;
    var s = v.ToString(); if (!String.IsNullOrWhiteSpace(s)) return s;
   }
   return null;
  }
  static string CommandText(JToken v) {
   if (v == null) return "";
   if (v.Type == JTokenType.String) return (string)v;
   if (v is JArray) return String.Join(" ", v.Select(x => x.ToString()));
   return "";
  }
  static string Join(string a, string b) { return String.Join("\n", new[] { a, b }.Where(x => x.Length > 0)); }
  static string Summary(JToken v) {
   if (v == null || v.Type == JTokenType.Null) return "";
   if (v.Type == JTokenType.String) return (string)v;
   if (v is JObject) {
    var text = First((JObject)v, "text", "output", "message", "content"); if (text != null) return text;
    return String.Join(", ", ((JObject)v).Properties().OrderBy(p => p.Name).Select(p => p.Name + "=" + (p.Value is JArray ? "[" + ((JArray)p.Value).Count + " 项]" : p.Value is JObject ? "{…}" : p.Value.ToString())));
   }
   if (v is JArray) return "[" + ((JArray)v).Count + " 项]";
   return v.ToString();
  }
  static string Clip(string text) {
   if (String.IsNullOrEmpty(text)) return "";
   var lines = text.Replace("\r\n", "\n").Split('\n');
   var result = lines.Length > 40 ? String.Join("\n", lines.Take(40)) + "\n… 还有 " + (lines.Length - 40) + " 行" : text;
   return result.Length > 6000 ? result.Substring(0, 6000) + "…" : result;
  }
  static string Seconds(JObject c) {
   var ms = c["durationMs"]; if (ms == null || ms.Type == JTokenType.Null) return "";
   double value; if (!Double.TryParse(ms.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value <= 0) return "";
   return Format(TimeSpan.FromMilliseconds(value));
  }
  static DateTimeOffset? Time(TimelineItem item, bool end) {
   DateTimeOffset value;
   var text = end ? item.Data.Text("completedAt", item.Data.Text("updatedAt", item.Data.Text("createdAt"))) : item.Data.Text("createdAt");
   return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out value) ? value : (DateTimeOffset?)null;
  }
  static string Duration(Turn turn) {
   var start = turn.Start ?? turn.Entries.FirstOrDefault()?.Item;
   var stop = turn.End ?? turn.Entries.LastOrDefault()?.Item;
   if (start == null || stop == null) return "";
   var a = Time(start, false); var b = Time(stop, true);
   if (a == null || b == null || b < a) return "";
   return Format(b.Value - a.Value);
  }
  public static string Format(TimeSpan span) {
   if (span.TotalSeconds < 1) return "";
   if (span.TotalMinutes < 1) return (int)span.TotalSeconds + " 秒";
   if (span.TotalHours < 1) return (int)span.TotalMinutes + " 分 " + span.Seconds.ToString("00") + " 秒";
   return (int)span.TotalHours + " 小时 " + span.Minutes.ToString("00") + " 分";
  }
  public static string Clock(TimelineItem item) {
   var t = Time(item, false); if (t == null) return "";
   var local = t.Value.ToLocalTime();
   return local.Date == DateTimeOffset.Now.Date ? local.ToString("HH:mm") : local.ToString("M月d日 HH:mm");
  }
 }
}
