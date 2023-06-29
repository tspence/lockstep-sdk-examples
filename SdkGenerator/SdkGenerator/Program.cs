using System;
using System.IO;
using System.Threading.Tasks;
using CommandLine;
using Newtonsoft.Json;
using SdkGenerator.Project;

namespace SdkGenerator;

public static class Program
{
    private class Options
    {
        [Option('p', "Project", Required = true, HelpText = "Specify a project file")]
        public string ProjectFile { get; set; }
    }

    public static async Task Main(string[] args)
    {
        await Parser.Default.ParseArguments<Options>(args)
            .WithParsedAsync(async o =>
            {
                // Retrieve project
                if (!File.Exists(o.ProjectFile))
                {
                    Console.WriteLine($"Project file could not be found: {o.ProjectFile}");
                    return;
                }

                var text = await File.ReadAllTextAsync(o.ProjectFile);
                var project = JsonConvert.DeserializeObject<ProjectSchema>(text);

                if (project is null)
                {
                    Console.WriteLine("Could not parse project file");
                    return;
                }

                // Fetch the environment and version number
                Console.WriteLine($"Retrieving swagger file from {project.SwaggerUrl}");
                var api = await DownloadFile.GenerateApi(project);
                if (api == null)
                {
                    Console.WriteLine("Unable to retrieve API and version number successfully.");
                    return;
                }

                Console.WriteLine($"Retrieved swagger file. Version: {api.Semver2}");

                // Let's do some software development kits!
                await TypescriptSdk.Export(project, api);
                await CSharpSdk.Export(project, api);
                await JavaSdk.Export(project, api);
                await RubySdk.Export(project, api);
                await PythonSdk.Export(project, api);
                Console.WriteLine("Exported SDKs.");

                // Do we want to upload data models to readme?
                if (project.Readme != null)
                {
                    Console.WriteLine("Uploading data models to Readme...");
                    await ReadmeUpload.UploadSchemas(api, project.Readme.ApiKey, "list");
                    Console.WriteLine("Uploaded data models to Readme.");
                }

                Console.WriteLine("Done!");
            });
    }
}