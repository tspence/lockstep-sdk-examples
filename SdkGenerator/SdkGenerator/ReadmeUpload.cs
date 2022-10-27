using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using RestSharp;
using SdkGenerator.Schema;

namespace SdkGenerator;

public static class ReadmeUpload
{
    public static async Task UploadSchemas(ApiSchema api, string readmeApiKey, string format)
    {
        var order = 1;
        if (!string.IsNullOrWhiteSpace(readmeApiKey))
        {
            foreach (var schema in api.Schemas.Where(schema => schema.Fields != null))
            {
                try
                {
                    var markdownText = format switch
                    {
                        "table" => MakeMarkdownTable(schema, api),
                        "list" => MakeMarkdownBulletList(schema, api),
                        _ => ""
                    };

                    await UploadToReadme(readmeApiKey, schema.Name, order++, markdownText);
                }
                catch (Exception e)
                {
                    Console.WriteLine($"Exception while parsing model for {schema.Name}: {e}");
                }
            }
        }
    }

    /// <summary>
    /// This is the older table-style data definition.  It was harder to read when the description of each field
    /// became more complex.  We switched away to using the bullet-list version.
    /// </summary>
    /// <param name="item"></param>
    /// <param name="api"></param>
    /// <returns></returns>
    private static string MakeMarkdownTable(SchemaItem item, ApiSchema api)
    {
        var sb = new StringBuilder();
        sb.AppendLine(item.DescriptionMarkdown);
        sb.AppendLine();

        // Link all the API endpoints that work with this model
        foreach (var endpoint in api.Endpoints.Where(endpoint => endpoint.ReturnDataType.DataType == item.Name))
        {
            sb.AppendLine($"* [{endpoint.Name}](/reference/test)");
        }

        // Provide definitions for all the fields
        sb.AppendLine("# Fields");
        sb.AppendLine("| Field | Type | Notes |");
        sb.AppendLine("|--|--|--|");
        foreach (var field in item.Fields)
        {
            var modifiers = "";
            if (field.Nullable)
            {
                modifiers += "(nullable) ";
            }

            if (field.ReadOnly)
            {
                modifiers += "(read-only) ";
            }

            if (field.Deprecated)
            {
                modifiers += "(deprecated) ";
            }

            if (field.MaxLength.HasValue && field.MinLength.HasValue)
            {
                modifiers += $"(between {field.MinLength} and {field.MaxLength} characters) ";
            }
            else if (field.MinLength.HasValue)
            {
                modifiers += $"(minimum {field.MinLength} characters) ";
            }
            else if (field.MaxLength.HasValue)
            {
                modifiers += $"(maximum {field.MaxLength} characters) ";
            }

            sb.AppendLine($"| **{field.Name}** | {field.DataType} {modifiers} | {FixupLines(field.DescriptionMarkdown)} |");
        }

        return sb.ToString();
    }

    private static string FixupLines(string str)
    {
        return (str ?? "").Replace(Environment.NewLine, "<br />");
    }

    private static string MakeMarkdownBulletList(SchemaItem item, ApiSchema api)
    {
        var sb = new StringBuilder();
        sb.AppendLine(item.DescriptionMarkdown);
        sb.AppendLine();

        // Link all the API endpoints that work with this model
        var methods = new List<string>();
        foreach (var endpoint in api.Endpoints)
        {
            var endpointDataType = endpoint.ReturnDataType.DataType.Replace("FetchResult", "");
            if (endpointDataType == item.Name)
            {
                var fixedPath = endpoint.Path.Substring(1).ToLower().Replace('/', '-').Replace("{", "").Replace("}", "");
                methods.Add($"* [{endpoint.Name}](/reference/{endpoint.Method.ToLower()}_{fixedPath})");
            }
        }

        if (methods.Count > 0)
        {
            sb.AppendLine("## Methods");
            sb.AppendLine();
            sb.AppendLine("The following API methods use this data model.");
            sb.AppendLine();
            foreach (var line in methods)
            {
                sb.AppendLine(line);
            }
        }

        // Group fields into buckets
        List<SchemaField> requiredFields = new();
        List<SchemaField> optionalFields = new();
        List<SchemaField> deprecatedFields = new();
        List<SchemaField> collectionFields = new();
        List<SchemaField> readOnlyFields = new();
        foreach (var f in item.Fields)
        {
            if (f.Deprecated)
            {
                deprecatedFields.Add(f);
            }
            else if (f.ReadOnly && f.DescriptionMarkdown.Contains("To retrieve this collection, specify"))
            {
                collectionFields.Add(f);
            }
            else if (f.ReadOnly)
            {
                readOnlyFields.Add(f);
            }
            else if (!f.Nullable)
            {
                requiredFields.Add(f);
            }
            else
            {
                optionalFields.Add(f);
            }
        }

        // Read-only fields first
        if (readOnlyFields.Count > 0)
        {
            sb.AppendLine("## Read-Only Fields");
            sb.AppendLine("These fields are assigned by the API server and cannot be changed.");
            foreach (var field in readOnlyFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Required fields first
        if (requiredFields.Count > 0)
        {
            sb.AppendLine("## Required Fields");
            foreach (var field in requiredFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Optional fields second
        if (optionalFields.Count > 0)
        {
            sb.AppendLine("## Optional Fields");
            foreach (var field in optionalFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Collections fields second
        if (collectionFields.Count > 0)
        {
            sb.AppendLine("## Included Collections");
            sb.AppendLine("These fields are available when using Retrieve or Query API calls if you specify the " +
                          "associated `Include` parameter.");
            foreach (var field in collectionFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        // Deprecated fields last, if any
        if (deprecatedFields.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("## Deprecated Fields");
            sb.AppendLine();
            sb.AppendLine("Deprecated fields are maintained for backwards compatibility with previous versions of "
                          + "the API.  Deprecated fields may be removed in a future release of the API.");
            sb.AppendLine();
            foreach (var field in deprecatedFields)
            {
                sb.AppendLine(FieldMarkdown(field));
            }
        }

        return sb.ToString();
    }

    private static string FieldMarkdown(SchemaField field)
    {
        var sb = new StringBuilder();
        var modifiers = new List<string>
        {
            string.IsNullOrWhiteSpace(field.DataTypeRef)
                ? $"[{field.DataType}]({field.DataTypeRef}){(field.IsArray ? "[]" : "")}"
                : $"{field.DataType}{(field.IsArray ? "[]" : "")}"
        };
        if (field.Nullable)
        {
            modifiers.Add("nullable");
        }

        if (field.ReadOnly)
        {
            modifiers.Add("read-only");
        }

        if (field.MaxLength.HasValue && field.MinLength.HasValue)
        {
            modifiers.Add($"{field.MinLength}-{field.MaxLength} characters");
        }
        else if (field.MinLength.HasValue)
        {
            modifiers.Add($"min {field.MinLength} characters");
        }
        else if (field.MaxLength.HasValue)
        {
            modifiers.Add($"max {field.MaxLength} characters");
        }

        // Append the field's basic information on one line
        sb.AppendLine($"### {field.Name}");
        sb.AppendLine();
        if (modifiers.Count > 0)
        {
            sb.AppendLine($"_{string.Join(", ", modifiers)}_");
            sb.AppendLine();
        }

        sb.AppendLine(field.DescriptionMarkdown);
        sb.AppendLine();
        return sb.ToString();
    }

    private static async Task<RestResponse> CallReadme(string readmeApiKey, string resource, Method method, string body = null)
    {
        var client = new RestClient("https://dash.readme.com");
        var request = new RestRequest(resource, method);
        request.AddHeader("Accept", "application/json");
        request.AddHeader("Authorization", $"Basic {readmeApiKey}");
        if (body != null)
        {
            request.AddHeader("Content-Type", "application/json");
            request.AddParameter("application/json", body, ParameterType.RequestBody);
        }

        return await client.ExecuteAsync(request);
    }

    private class ReadmeDocModel
    {
        [JsonProperty("hidden")]
        public bool Hidden { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("body")]
        public string Body { get; set; }

        [JsonProperty("category")]
        public string Category { get; set; }

        [JsonProperty("order")]
        public int Order { get; set; }
    }

    private static async Task UploadToReadme(string readmeApiKey, string schemaName, int order, string markdown)
    {
        var docName = $"/api/v1/docs/{schemaName.ToLower()}";
        var doc = new ReadmeDocModel
        {
            Hidden = false,
            Order = order,
            Title = schemaName,
            Body = markdown,
            Category = "6169fb9ea16b55001fa1f4cf"
        };

        // Check to see if the model exists
        var modelExists = await CallReadme(readmeApiKey, docName, Method.Get);
        if (modelExists.IsSuccessful)
        {
            var result = await CallReadme(readmeApiKey, docName, Method.Put, JsonConvert.SerializeObject(doc));
            Console.WriteLine($"Updated {schemaName}: {result.StatusCode}");
        }
        else
        {
            var result = await CallReadme(readmeApiKey, "/api/v1/docs", Method.Post, JsonConvert.SerializeObject(doc));
            Console.WriteLine($"Created {schemaName}: {result.StatusCode}");
        }
    }
}