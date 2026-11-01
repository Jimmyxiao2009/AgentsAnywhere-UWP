using System;
using System.Collections.Generic;
using System.Text;
namespace AgentsAnywhere.Mobile.Services
{
 public static class CommandArguments {
  public static IEnumerable<string> Parse(string input) {
   var list=new List<string>();var current=new StringBuilder();char quote='\0';bool token=false,escaped=false;
   foreach(var ch in input??""){
    if(escaped){current.Append(ch);escaped=false;token=true;continue;}
    if(ch=='\\' && quote=='"'){escaped=true;continue;}
    if(quote!='\0'){if(ch==quote)quote='\0';else current.Append(ch);token=true;continue;}
    if(ch=='"'||ch=='\''){quote=ch;token=true;continue;}
    if(Char.IsWhiteSpace(ch)){if(token){list.Add(current.ToString());current.Clear();token=false;}}
    else{current.Append(ch);token=true;}
   }
   if(quote!='\0')throw new ArgumentException("参数中的引号尚未闭合。");
   if(escaped)current.Append('\\');if(token)list.Add(current.ToString());return list;
  }
 }
}
