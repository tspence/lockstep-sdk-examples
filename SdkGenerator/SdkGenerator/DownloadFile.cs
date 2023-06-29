using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using JsonDiffPatchDotNet;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Polly;
using Polly.Retry;
using SdkGenerator.Project;
using SdkGenerator.Schema;

namespace SdkGenerator;

public static class DownloadFile
{
    private static readonly HttpClient HttpClient = new();

    private static readonly AsyncRetryPolicy HttpRetryPolicy = Policy.Handle<HttpRequestException>()
        .WaitAndRetryAsync(new[]
        {
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(4)
        });

    /// <summary>
    /// Download the swagger file
    /// </summary>
    /// <param name="project"></param>
    /// <param name="semver2"></param>
    /// <returns></returns>
    private static async Task<string> DownloadSwagger(ProjectSchema project, string semver2)
    {
        // Downloads json as a string to compare
        var response = await HttpClient.GetAsync(project.SwaggerUrl);
        var json = await response.Content.ReadAsStringAsync();

        // Cleanup the JSON text
        return FixupSwagger(project, json, semver2);
    }

    /// <summary>
    /// Compare two swagger files and produce a difference
    /// </summary>
    /// <param name="oldSwagger"></param>
    /// <param name="newSwagger"></param>
    /// <returns></returns>
    public static string CompareSwagger(string oldSwagger, string newSwagger)
    {
        var jdp = new JsonDiffPatch();
        JToken diffResult = jdp.Diff(oldSwagger, newSwagger);
        return diffResult.ToString();
    }

    /// <summary>
    /// The version number is year.month.build (EX. 21.0.629)
    /// In general semver is supposed to be three digits, but some have four, so let's make all possibilities
    /// </summary>
    /// <param name="project"></param>
    /// <returns></returns>
    private static async Task<string> FindVersionNumber(ProjectSchema project)
    {
        if (string.IsNullOrWhiteSpace(project.VersionNumberUrl) ||
            string.IsNullOrWhiteSpace(project.VersionNumberRegex))
        {
            return "1.0.0.0";
        }

        // Attempt to retrieve this page and scan for the version number
        try
        {
            var contents = await HttpRetryPolicy.ExecuteAsync(async ct =>
                await HttpClient.GetStringAsync(project.VersionNumberUrl, ct), CancellationToken.None);
            var r = new Regex(project.VersionNumberRegex);
            var match = r.Match(contents);
            if (match.Success)
            {
                return match.Groups[1].Value;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load {project.VersionNumberUrl}: {ex.Message}");
        }

        return "1.0.0.0";
    }

    /// <summary>
    /// Cleanup an existing swagger JSON
    /// </summary>
    /// <param name="project"></param>
    /// <param name="swagger"></param>
    /// <param name="semver2"></param>
    /// <returns></returns>
    private static string FixupSwagger(ProjectSchema project, string swagger, string semver2)
    {
        var jObject = JObject.Parse(swagger);

        // Give us a better version number
        jObject["info"]["title"] = project.ProjectName;
        jObject["info"]["version"] = semver2;

        // Erase the list of server URLs and replace with ones from the project file
        if (project.Environments?.Length > 0)
        {
            var servers = new JArray();
            foreach (var env in project.Environments)
            {
                var server = new JObject();
                server.Add("url", env.Url);
                servers.Add(server);
            }

            jObject.Add("servers", servers);
        }

        // Remove OAuth2 security definition - it's just for Swagger UI
        jObject["components"]!["securitySchemes"]!["oauth2"]!.Parent!.Remove();

        // Add links to the document data definitions
        if (project.Readme != null)
        {
            foreach (var endpoint in jObject["paths"])
            {
                foreach (var path in endpoint.Children())
                {
                    // Does this have a "Success" response?
                    foreach (var method in path.Values())
                    {
                        if (method is JObject methodObj)
                        {
                            var token =
                                methodObj.SelectToken("responses.200.content.['application/json'].schema.$ref") ??
                                methodObj.SelectToken("responses.201.content.['application/json'].schema.$ref") ??
                                methodObj.SelectToken(
                                    "responses.200.content.['application/json'].schema.items.$ref") ??
                                methodObj.SelectToken(
                                    "responses.201.content.['application/json'].schema.items.$ref");
                            if (token != null)
                            {
                                var cleanModelName = token.ToString();
                                cleanModelName = cleanModelName.Substring(cleanModelName.LastIndexOf('/') + 1)
                                    .Replace("FetchResult", "");
                                if (methodObj.SelectToken("description") is JValue desc &&
                                    cleanModelName != "ActionResultModel")
                                {
                                    desc.Value = desc.Value + "\r\n\r\n" +
                                                 $"### Data Definition\r\n\r\nSee [{cleanModelName}]({project.Readme.Url}/docs/{cleanModelName.ToLower()}) for the complete data definition.";
                                }
                            }
                        }
                    }
                }
            }
        }

        return JsonConvert.SerializeObject(jObject, Formatting.Indented);
    }

    /// <summary>
    /// Export data definitions to their own markdown files
    /// </summary>
    /// <param name="project">The file location</param>
    /// <param name="swaggerJson">The file location</param>
    /// <param name="version2">The short version number (year.week)</param>
    /// <param name="version3">The short version number (year.week)</param>
    /// <param name="version4">The full version number (year.week.build)</param>
    private static ApiSchema GatherSchemas(ProjectSchema project, string swaggerJson, string version2, string version3,
        string version4)
    {
        // Gather schemas from the file
        using var doc = JsonDocument.Parse(swaggerJson);

        // Collect all the schemas / data models
        var schemaList = new List<SchemaItem>();
        var components = doc.RootElement.GetProperty("components");
        var schemas = components.GetProperty("schemas");
        foreach (var schema in schemas.EnumerateObject())
        {
            var item = SchemaFactory.MakeSchema(schema);
            if (item != null)
            {
                schemaList.Add(item);
            }
        }

        schemaList.Add(new()
        {
            Name = "ErrorResult",
            DescriptionMarkdown = "Represents a failed API request.",
            Fields = new()
            {
                new()
                {
                    Name = "type",
                    DescriptionMarkdown = "A description of the type of error that occurred.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "title",
                    DescriptionMarkdown = "A short title describing the error.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "status",
                    DescriptionMarkdown = "If an error code is applicable, this contains an error number.",
                    DataType = "int32",
                    Nullable = false,
                },
                new()
                {
                    Name = "detail",
                    DescriptionMarkdown = "If detailed information about this error is available, this value contains more information.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "instance",
                    DescriptionMarkdown = "If this error corresponds to a specific instance or object, this field indicates which one.",
                    DataType = "string",
                    Nullable = false,
                },
                new()
                {
                    Name = "content",
                    DescriptionMarkdown = "The full content of the HTTP response.",
                    DataType = "string",
                    Nullable = false,
                }
            },
        });

        // Collect all the APIs
        var endpointList = new List<EndpointItem>();
        var paths = doc.RootElement.GetProperty("paths");
        foreach (var endpoint in paths.EnumerateObject())
        {
            var item = SchemaFactory.MakeEndpoint(endpoint);
            if (item != null)
            {
                endpointList.AddRange(item);
            }
        }

        // Convert into an API schema
        return new ApiSchema
        {
            Semver2 = version2,
            Semver3 = version3,
            Semver4 = version4,
            Schemas = schemaList.OrderBy(s => s.Name).ToList(),
            Endpoints = endpointList,
            Categories = (from e in endpointList where !e.Deprecated orderby e.Category select e.Category).Distinct().ToList()
        };
    }

    public static async Task<ApiSchema> GenerateApi(ProjectSchema project)
    {
        var version4 = await FindVersionNumber(project);

        // If we couldn't download the version number, don't try generating anything
        if (version4 == "1.0.0.0")
        {
            return null;
        }

        var segments = version4.Split(".");
        var version2 = $"{segments[0]}.{segments[1]}";
        var version3 = $"{segments[0]}.{segments[1]}.{segments[2]}";
        var swaggerJson = await DownloadSwagger(project, version2);

        // Save to the swagger folder
        if (Directory.Exists(project.SwaggerSchemaFolder))
        {
            var swaggerFilePath = Path.Combine(project.SwaggerSchemaFolder, $"swagger-{version2}.json");
            await File.WriteAllTextAsync(swaggerFilePath, swaggerJson);
        }

        // Export data definitions to markdown files
        return GatherSchemas(project, swaggerJson, version2, version3, version4);
    }
}