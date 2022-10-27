namespace SdkGenerator.Project;

public class ProjectSchema
{
    public string CompanyName { get; set; }
    public string AuthorName { get; set; }
    public string AuthorEmail { get; set; }
    public string ProjectName { get; set; }
    public string CopyrightHolder { get; set; }
    public int ProjectStartYear { get; set; }
    public string SwaggerUrl { get; set; }
    public EnvironmentSchema[] Environments { get; set; }
    public string SwaggerSchemaFolder { get; set; }
    public string Keywords { get; set; }
    public string Description { get; set; }
    public string AuthenticationHelp { get; set; }
    public string ReleaseNotes { get; set; }

    /// <summary>
    /// If you use a readme site, provide this information
    /// </summary>
    public ReadmeSiteSchema Readme { get; set; }

    /// <summary>
    /// To determine the correct version number for your project, use this URL and regex
    /// </summary>
    public string VersionNumberUrl { get; set; }

    public string VersionNumberRegex { get; set; }

    /// <summary>
    /// Extra information about the various languages
    /// </summary>
    public LanguageSchema Csharp { get; set; }

    public LanguageSchema Java { get; set; }
    public LanguageSchema Python { get; set; }
    public LanguageSchema Ruby { get; set; }
    public LanguageSchema Typescript { get; set; }
}

/// <summary>
/// Represents a shortcut name you can use to identify an environment
/// </summary>
public class EnvironmentSchema
{
    public string Name { get; set; }
    public string Url { get; set; }
    public bool? Default { get; set; }
}

/// <summary>
/// If you use Readme.com, here's information about where to upload documentation
/// </summary>
public class ReadmeSiteSchema
{
    /// <summary>
    /// The customer visible URL of your developer site
    /// </summary>
    public string Url { get; set; }

    /// <summary>
    /// An API key to use to communicate with Readme
    /// </summary>
    public string ApiKey { get; set; }
}

public class LanguageSchema
{
    public string ModuleName { get; set; }
    public string ExtraCredit { get; set; }
    public string Folder { get; set; }
    public string ClassName { get; set; }
    public string ResponseClass { get; set; }
    public string Namespace { get; set; }
    public string GithubUrl { get; set; }
}