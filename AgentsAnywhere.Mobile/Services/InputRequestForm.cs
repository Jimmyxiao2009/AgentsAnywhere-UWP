using System;
using System.Collections.Generic;
using System.Linq;
using AgentsAnywhere.Mobile.Models;
using Newtonsoft.Json.Linq;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
namespace AgentsAnywhere.Mobile.Services
{
 /// <summary>
 /// Renders the AskUserQuestion style request that runtimes describe with
 /// uiSchema.component == "inputRequest", and reads back
 /// {"answers": {questionId: {"optionIds": [...], "customText": "..."}}}.
 /// The generic SchemaForm cannot render this shape: every question is an object
 /// whose optionIds field is an array of enum strings, so it degrades into nested
 /// labels plus one "add item" button per question and never shows the question.
 /// </summary>
 public sealed class InputRequestForm {
  public StackPanel Panel { get; } = new StackPanel();
  readonly List<KeyValuePair<string,Func<JObject>>> readers = new List<KeyValuePair<string,Func<JObject>>>();
  public bool HasQuestions { get { return readers.Count > 0; } }

  public InputRequestForm(JObject uiSchema) {
   var questions = uiSchema == null ? null : uiSchema["questions"] as JArray;
   if (questions == null) return;
   int index = 0;
   foreach (var question in questions.OfType<JObject>()) {
    var id = question.Text("id");
    if (id.Length == 0) continue;
    index++;
    var number = index;
    var multiple = question.Flag("multiple");
    var allowCustom = question.Flag("allowCustom", true);
    var options = question.Objects("options").ToList();

    var header = question.Text("header");
    if (header.Length > 0) Panel.Children.Add(new TextBlock { Text = header, FontSize = 13, Foreground = Ui.Res.Brush("MidBrush"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 0) });
    Panel.Children.Add(new TextBlock { Text = number + ". " + question.Text("prompt", id), FontSize = 15, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Margin = new Thickness(0, 8, 0, 0) });

    var group = "q" + number + "_" + Guid.NewGuid().ToString("N");
    var toggles = new List<ToggleButton>();
    foreach (var option in options) {
     var optionId = option.Text("id");
     if (optionId.Length == 0) continue;
     var label = option.Text("label", optionId);
     var description = option.Text("description");
     ToggleButton toggle = multiple ? (ToggleButton)new CheckBox() : new RadioButton { GroupName = group };
     toggle.Content = label;
     toggle.Tag = optionId;
     toggle.Margin = new Thickness(0, 6, 0, 0);
     if (description.Length > 0) {
      var stack = new StackPanel();
      stack.Children.Add(new TextBlock { Text = label, TextWrapping = TextWrapping.Wrap });
      stack.Children.Add(new TextBlock { Text = description, FontSize = 12, Foreground = Ui.Res.Brush("SoftTextBrush"), TextWrapping = TextWrapping.Wrap });
      toggle.Content = stack;
     }
     Panel.Children.Add(toggle);
     toggles.Add(toggle);
    }
    TextBox custom = null;
    if (allowCustom) {
     custom = new TextBox { PlaceholderText = multiple ? "其他（可选，自行填写）" : "其他（可选，自行填写）", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0) };
     Panel.Children.Add(custom);
    }
    var box = custom;
    readers.Add(new KeyValuePair<string,Func<JObject>>(id, () => {
     var picked = toggles.Where(x => x.IsChecked == true).Select(x => (string)x.Tag).Where(x => !String.IsNullOrEmpty(x)).ToList();
     var text = box == null ? "" : box.Text.Trim();
     if (picked.Count == 0 && text.Length == 0) throw new ArgumentException("请回答问题 " + number + "。");
     var answer = new JObject();
     if (picked.Count > 0) answer["optionIds"] = new JArray(picked);
     if (text.Length > 0) answer["customText"] = text;
     return answer;
    }));
   }
  }

  public JObject Read() {
   var answers = new JObject();
   foreach (var reader in readers) answers[reader.Key] = reader.Value();
   return new JObject { ["answers"] = answers };
  }
 }
}