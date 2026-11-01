using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using AgentsAnywhere.Mobile.Models;
using Newtonsoft.Json.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
namespace AgentsAnywhere.Mobile.Services
{
 public sealed class SchemaForm {
  public StackPanel Panel{get;}=new StackPanel {Margin=new Thickness(0,8,0,0)};
  readonly Func<JToken> read;
  public bool HasFields{get;}
  public SchemaForm(JObject schema){
   if(schema==null){read=()=>new JObject();return;}
   HasFields=true;read=Build(Panel,schema,schema.Text("title","回复"),true,0);
  }
  public JObject Read(){var value=read();if(value is JObject)return (JObject)value;return new JObject {["text"]=value};}
  static Func<JToken> Build(StackPanel parent,JObject schema,string label,bool required,int depth) {
   if(depth>8)throw new ArgumentException("请求表单嵌套过深。");
   var type=schema.Text("type",schema["properties"] is JObject?"object":"string");
   var choices=schema["enum"] as JArray;
   if(choices!=null) {
    var box=new ComboBox {Header=label+(required?" *":""),ItemsSource=choices.Select(x=>x.ToString()).ToList(),HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,6,0,0)};
    box.SelectedIndex=schema["default"]!=null?choices.ToList().FindIndex(x=>JToken.DeepEquals(x,schema["default"])):-1;
    parent.Children.Add(box);
    return ()=>{if(box.SelectedIndex<0){if(required)throw new ArgumentException("请选择 "+label);return JValue.CreateNull();}return choices[box.SelectedIndex].DeepClone();};
   }
   if(type=="object") {
    var fields=new Dictionary<string,Func<JToken>>();var requiredKeys=new HashSet<string>((schema["required"] as JArray??new JArray()).Select(x=>(string)x));
    var props=schema["properties"] as JObject??new JObject();
    foreach(var prop in props.Properties()) {
     var child=prop.Value as JObject;if(child==null)continue;
     fields[prop.Name]=Build(parent,child,child.Text("title",prop.Name),requiredKeys.Contains(prop.Name),depth+1);
    }
    return ()=>{var obj=new JObject();foreach(var field in fields){var v=field.Value();if(v.Type!=JTokenType.Null)obj[field.Key]=v;}return obj;};
   }
   if(type=="array") {
    var panel=new StackPanel {Margin=new Thickness(0,8,0,0)};parent.Children.Add(new TextBlock {Text=label+(required?" *":""),TextWrapping=TextWrapping.Wrap});parent.Children.Add(panel);
    var children=new List<Func<JToken>>();var item=schema["items"] as JObject??new JObject {["type"]="string"};
    var add=new Button {Content="添加一项",Margin=new Thickness(0,6,0,0)};parent.Children.Add(add);
    Action addItem=()=>{if(children.Count>=32)throw new ArgumentException("最多添加 32 项。");var row=new StackPanel();panel.Children.Add(row);var reader=Build(row,item,"第 "+(children.Count+1)+" 项",true,depth+1);children.Add(reader);var remove=new Button {Content="移除",Margin=new Thickness(0,4,0,4)};row.Children.Add(remove);remove.Click+=(s,e)=>{panel.Children.Remove(row);children.Remove(reader);};};
    add.Click+=(s,e)=>{if(children.Count<32)addItem();};
    var minimum=(int)schema.Number("minItems",required?1:0);for(int i=0;i<Math.Min(8,minimum);i++)addItem();
    return ()=>{if(children.Count<minimum)throw new ArgumentException(label+" 至少需要 "+minimum+" 项。");return new JArray(children.Select(x=>x()));};
   }
   if(type=="boolean") {
    var check=new CheckBox {Content=label,IsChecked=schema.Flag("default"),Margin=new Thickness(0,6,0,0)};parent.Children.Add(check);return ()=>new JValue(check.IsChecked==true);
   }
   var text=new TextBox {Header=label+(required?" *":""),PlaceholderText=schema.Text("description"),Text=schema["default"]?.ToString()??"",AcceptsReturn=type=="string",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,6,0,0)};
   parent.Children.Add(text);
   return ()=> {
    if(String.IsNullOrWhiteSpace(text.Text)){if(required)throw new ArgumentException("请填写 "+label);return JValue.CreateNull();}
    if(type=="integer"){long n;if(!Int64.TryParse(text.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out n))throw new ArgumentException(label+" 需要整数。");return new JValue(n);}
    if(type=="number"){double n;if(!Double.TryParse(text.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out n)||Double.IsInfinity(n)||Double.IsNaN(n))throw new ArgumentException(label+" 需要有效数字。");return new JValue(n);}
    if(text.Text.Length<schema.Number("minLength") || schema["maxLength"]!=null && text.Text.Length>schema.Number("maxLength"))throw new ArgumentException(label+" 的长度不符合要求。");
    return new JValue(text.Text);
   };
  }
 }
}
