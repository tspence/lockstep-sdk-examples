using System;
using System.IO;
using System.Threading.Tasks;
using Scriban;
using Scriban.Runtime;
using Scriban.Syntax;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator;

public static class ScribanFunctions
{
    public static async Task ExecuteTemplate(string templateName, ProjectSchema project, ApiSchema api, string outputFile)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outputFile));
            var templateText = await File.ReadAllTextAsync(templateName);
            var template = Template.Parse(templateText);
            var scriptObject1 = new ScriptObject();
            scriptObject1.Import(typeof(Extensions));
            var context = new TemplateContext();
            context.PushGlobal(scriptObject1);
            context.SetValue(new ScriptVariableGlobal("api"), api);
            context.SetValue(new ScriptVariableGlobal("project"), project);
            var result = await template.RenderAsync(context);
            await File.WriteAllTextAsync(outputFile, result);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
}