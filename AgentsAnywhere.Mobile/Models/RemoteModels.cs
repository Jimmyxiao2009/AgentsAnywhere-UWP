using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace AgentsAnywhere.Mobile.Models
{
 public static class Wire {
  public static string Text(this JToken obj, string key, string fallback = "") { return (string)obj?[key] ?? fallback; }
  public static bool Flag(this JToken obj, string key, bool fallback = false) { return (bool?)obj?[key] ?? fallback; }
  public static long Number(this JToken obj, string key, long fallback = 0) { return (long?)obj?[key] ?? fallback; }
  public static IEnumerable<JObject> Objects(this JToken obj, string key) { return (obj?[key] as JArray ?? new JArray()).OfType<JObject>(); }
 }
 public class Entity {
  public JObject Data { get; private set; }
  public string Id { get { return Data.Text("id"); } }
  public string Name { get { return Data.Text("title", Data.Text("name", "未命名")); } }
  public string ConnectorId { get { return Data.Text("connectorId"); } }
  public string ProjectId { get { return Data.Text("projectId"); } }
  public string Status { get { return Data.Text("status"); } }
  public string Workspace { get { return Data.Text("workspacePath", Data.Text("cwd")); } }
  public string Subtitle { get { return String.Join(" · ", new[] {Data.Text("runtimeName", Data.Text("runtime")), Status, Data.Text("deviceOs"), Workspace}.Where(x => !String.IsNullOrEmpty(x))); } }
  public bool Archived { get { return Data.Flag("archived"); } }
  public bool Online { get { return Status == "online" || Data.Text("connectorStatus") == "online"; } }
  public Entity(JObject data) { Data = data ?? new JObject(); }
  public override string ToString() { return Name; }
 }
 public class TimelineItem {
  public JObject Data { get; private set; }
  public string Id { get { return Data.Text("id"); } }
  public long Order { get { return Data.Number("orderSeq"); } }
  public long Revision { get { return Data.Number("revision"); } }
  public string Heading { get { return String.Join(" · ", new[] { Data.Text("role", Data.Text("type", "消息")), Data.Text("status"), Data.Text("createdAt").Replace("T", " ").Replace("Z", "") }.Where(x => !String.IsNullOrEmpty(x))); } }
  public string Body { get {
   var c = Data["content"] as JObject ?? new JObject();
   var text = c.Text("text", c.Text("message", c.Text("description", c.Text("title"))));
   if (String.IsNullOrEmpty(text)) text = c.ToString(Newtonsoft.Json.Formatting.Indented);
   var output = c.Text("output");
   if (!String.IsNullOrEmpty(output)) text += "\n" + output;
   return text;
  }}
  public TimelineItem(JObject data) { Data = data; }
 }
 public sealed class TimelineState {
  readonly Dictionary<string, TimelineItem> items = new Dictionary<string, TimelineItem>();
  readonly Dictionary<string, JObject> notices = new Dictionary<string, JObject>();
  public string Cursor { get; private set; } = "seq:0";
  public long Sequence { get; private set; }
  public JObject Session { get; private set; } = new JObject();
  public JObject State { get; private set; } = new JObject();
  public JObject Capabilities { get; private set; } = new JObject();
  public JObject Catalogs { get; private set; } = new JObject();
  public bool HasOlder { get; set; }
  public IReadOnlyList<TimelineItem> Items { get { return items.Values.OrderBy(x => x.Order).ThenBy(x => x.Id).ToList(); } }
  public IEnumerable<JObject> Notices { get { return notices.Values.Where(x => x.Text("status", "open") == "open").OrderBy(x=>x.Text("createdAt")); } }
  public void Snapshot(JObject value) {
   items.Clear(); notices.Clear();
   Session = value["session"] as JObject ?? new JObject();
   State = value["state"] as JObject ?? new JObject();
   Capabilities = value["effectiveCapabilities"] as JObject ?? new JObject();
   Catalogs = value["catalogs"] as JObject ?? new JObject();
   foreach (var i in value["timeline"].Objects("items")) Upsert(i);
   foreach (var n in value.Objects("notices")) notices[n.Text("noticeId")] = n;
   HasOlder = value["timeline"].Flag("hasMore");
   Cursor = value.Text("eventCursor", "seq:0"); Sequence = ParseCursor(Cursor);
  }
  public void Upsert(JObject data) {
   var item = new TimelineItem(data); if (String.IsNullOrEmpty(item.Id)) return;
   TimelineItem old; if (!items.TryGetValue(item.Id, out old) || item.Revision >= old.Revision) items[item.Id] = item;
  }
  public void AdvanceCursor(string cursor) {
   var sequence=ParseCursor(cursor);
   if(sequence>=Sequence){Sequence=sequence;Cursor=cursor;}
  }
  public void RefreshProjection(JObject value) {
   if(value["session"] is JObject)Session=(JObject)value["session"];
   if(value["state"] is JObject)State=(JObject)value["state"];
   if(value["effectiveCapabilities"] is JObject)Capabilities=(JObject)value["effectiveCapabilities"];
   if(value["catalogs"] is JObject)Catalogs=(JObject)value["catalogs"];
   if(value["notices"] is JArray){notices.Clear();foreach(var n in value.Objects("notices"))notices[n.Text("noticeId")]=n;}
  }
  public bool Apply(JObject evt, bool recovery=false) {
   var seq = evt.Number("sequence");
   // The server emits multiple projections at one durable sequence and recovery
   // aggregates revisions, so sequences are neither unique nor contiguous.
   if ((!recovery && seq < Sequence) || evt.Text("sessionId") != Session.Text("id")) return false;
   var p = evt["payload"] as JObject ?? new JObject();
   var type = evt.Text("type");
   if (p["session"] is JObject) Session = (JObject)p["session"];
   if (p["state"] is JObject) State = (JObject)p["state"];
   if (p["item"] is JObject) Upsert((JObject)p["item"]);
   foreach (var i in p.Objects("items")) Upsert(i);
   if (type.Contains("removed") || type.Contains("deleted")) { var id = p.Text("itemId", p.Text("id")); if (id.Length > 0) items.Remove(id); }
   if (p["notice"] is JObject) { var n = (JObject)p["notice"]; notices[n.Text("noticeId")] = n; }
   foreach (var n in p.Objects("notices")) notices[n.Text("noticeId")] = n;
   if (p["capabilitySet"] is JObject) Capabilities = (JObject)p["capabilitySet"];
   var catalogType = p.Text("catalogType"); if (p["catalog"] is JObject && catalogType.Length > 0) Catalogs[catalogType] = p["catalog"];
   AdvanceCursor(evt.Text("cursor", "seq:" + seq)); return true;
  }
  public bool Can(string id) { return Capabilities.Objects("capabilities").Any(c=>c.Text("capabilityId") == id && c.Flag("supported",true) && c.Flag("available",true) && c.Flag("allowed",true)); }
  public static long ParseCursor(string cursor) { long value; return cursor != null && cursor.StartsWith("seq:") && long.TryParse(cursor.Substring(4), out value) ? value : 0; }
 }
}
