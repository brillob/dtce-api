using System.CommandLine;
using System.CommandLine.Invocation;
using System.Text.Json;
using Dtce.AnalysisEngine.Services;
using Dtce.Common;
using Dtce.Common.Models;
using Dtce.Infrastructure.Local;
using Dtce.ParsingEngine.Handlers;
using Dtce.ParsingEngine.Rendering;
using Dtce.Persistence;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

var rootCommand = BuildRootCommand();
return await rootCommand.InvokeAsync(args);

static RootCommand BuildRootCommand()
{
    var verboseOption = new Option<bool>("--verbose", "Enable verbose logging output.");

    var root = new RootCommand("DTCE template extraction and rendering utilities");
    root.AddGlobalOption(verboseOption);
    root.AddCommand(BuildExtractCommand(verboseOption));
    root.AddCommand(BuildRenderTemplateCommand(verboseOption));
    root.AddCommand(BuildRenderDocumentCommand(verboseOption));

    return root;
}

static Command BuildExtractCommand(Option<bool> verboseOption)
{
    var inputOption = new Option<FileInfo>("--input", "Path to the source document (DOCX).")
    {
        IsRequired = true
    };

    var templateOutputOption = new Option<FileInfo?>(
        "--output-template",
        () => null,
        "Destination path for the extracted template JSON.");

    var contextOutputOption = new Option<FileInfo?>(
        "--output-context",
        () => null,
        "Destination path for the extracted context JSON.");

    var templateDocOption = new Option<FileInfo?>(
        "--output-template-doc",
        () => null,
        "Optional path for a placeholder DOCX generated from the template.");

    var storageRootOption = new Option<DirectoryInfo?>(
        "--local-storage-root",
        () => null,
        "Root folder for local file storage (defaults to ./cli-storage).");

    var azureConnectionOption = new Option<string?>(
        "--azure-connection-string",
        description: "Azure Storage connection string for blob-backed storage.");

    var azureContainerOption = new Option<string>(
        "--azure-container",
        () => "dtce-documents",
        "Azure Storage container name when using blob storage.");

    var includeAnalysisOption = new Option<bool>(
        "--include-analysis",
        description: "If set, runs lightweight NLP & vision analyzers to enrich context and classify logos.");

    var command = new Command("extract", "Extract template JSON, context JSON, and (optionally) a placeholder DOCX from a source document.")
    {
        inputOption,
        templateOutputOption,
        contextOutputOption,
        templateDocOption,
        storageRootOption,
        azureConnectionOption,
        azureContainerOption,
        includeAnalysisOption
    };

    command.SetHandler(async (InvocationContext ctx) =>
    {
        var input = ctx.ParseResult.GetValueForOption(inputOption)!;
        var templateOutput = ctx.ParseResult.GetValueForOption(templateOutputOption);
        var contextOutput = ctx.ParseResult.GetValueForOption(contextOutputOption);
        var templateDocOutput = ctx.ParseResult.GetValueForOption(templateDocOption);
        var storageRoot = ctx.ParseResult.GetValueForOption(storageRootOption);
        var azureConnection = ctx.ParseResult.GetValueForOption(azureConnectionOption);
        var azureContainer = ctx.ParseResult.GetValueForOption(azureContainerOption);
        var includeAnalysis = ctx.ParseResult.GetValueForOption(includeAnalysisOption);
        var verbose = ctx.ParseResult.GetValueForOption(verboseOption);

        await ExecuteWithGuardAsync(async () =>
        {
            if (!input.Exists)
            {
                throw new FileNotFoundException("Input document not found.", input.FullName);
            }

            templateOutput ??= new FileInfo(Path.Combine(
                input.DirectoryName ?? Directory.GetCurrentDirectory(),
                $"{Path.GetFileNameWithoutExtension(input.Name)}.template.json"));

            contextOutput ??= new FileInfo(Path.Combine(
                input.DirectoryName ?? Directory.GetCurrentDirectory(),
                $"{Path.GetFileNameWithoutExtension(input.Name)}.context.json"));

            EnsureParentDirectory(templateOutput);
            EnsureParentDirectory(contextOutput);
            if (templateDocOutput != null)
            {
                EnsureParentDirectory(templateDocOutput);
            }

            using var loggerFactory = CreateLoggerFactory(verbose);
            var storage = CreateObjectStorage(loggerFactory, storageRoot, azureConnection, azureContainer ?? "dtce-documents");

            var jobId = $"extract-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
            var storagePath = $"documents/{jobId}/{input.Name}";

            await using (var sourceStream = input.OpenRead())
            {
                await storage.UploadFileAsync(
                    storagePath,
                    sourceStream,
                    GetContentType(input),
                    CancellationToken.None);
            }

            var handler = new DocxHandler(storage, loggerFactory.CreateLogger<DocxHandler>());
            var parseResult = await handler.ParseAsync(new JobRequest
            {
                JobId = jobId,
                DocumentType = DocumentType.Docx,
                FilePath = storagePath
            });

            var contextJson = await BuildContextAsync(parseResult, includeAnalysis, storage, loggerFactory);

            await WriteJsonAsync(templateOutput.FullName, parseResult.TemplateJson);
            await WriteJsonAsync(contextOutput.FullName, contextJson);

            if (templateDocOutput != null)
            {
                var renderer = new TemplateDocumentRenderer(storage, loggerFactory.CreateLogger<TemplateDocumentRenderer>());
                var placeholderBytes = await renderer.RenderAsync(
                    parseResult.TemplateJson,
                    new ContextJson(),
                    new TemplateRenderOptions
                    {
                        IncludeLogos = true,
                        IncludeTemplateLogosFromStorage = true,
                        EmitPlaceholderForMissingContent = true
                    });

                await File.WriteAllBytesAsync(templateDocOutput.FullName, placeholderBytes);
            }

            Console.WriteLine("Extraction complete:");
            Console.WriteLine($"  Template JSON: {templateOutput.FullName}");
            Console.WriteLine($"  Context JSON:  {contextOutput.FullName}");
            if (templateDocOutput != null)
            {
                Console.WriteLine($"  Template DOCX: {templateDocOutput.FullName}");
            }
        });
    });

    return command;
}

static Command BuildRenderTemplateCommand(Option<bool> verboseOption)
{
    var templateOption = new Option<FileInfo>("--template-json", "Template JSON produced by the extractor.")
    {
        IsRequired = true
    };

    var contextOption = new Option<FileInfo?>(
        "--context-json",
        () => null,
        "Optional context JSON to seed sections with sample content.");

    var outputOption = new Option<FileInfo>("--output", "Destination DOCX path.")
    {
        IsRequired = true
    };

    var storageRootOption = new Option<DirectoryInfo?>(
        "--local-storage-root",
        () => null,
        "Root folder for local file storage (defaults to ./cli-storage).");

    var azureConnectionOption = new Option<string?>(
        "--azure-connection-string",
        description: "Azure Storage connection string for blob-backed storage.");

    var azureContainerOption = new Option<string>(
        "--azure-container",
        () => "dtce-documents",
        "Azure Storage container name when using blob storage.");

    var includeLogosOption = new Option<bool>(
        "--include-logos",
        () => true,
        "Include template logos when rendering (requires access to storage).");

    var emitPlaceholdersOption = new Option<bool>(
        "--emit-placeholders",
        () => false,
        "Emit {{placeholder}} tokens when context is missing.");

    var command = new Command("render-template", "Render a DOCX template from template/context JSON.")
    {
        templateOption,
        contextOption,
        outputOption,
        storageRootOption,
        azureConnectionOption,
        azureContainerOption,
        includeLogosOption,
        emitPlaceholdersOption
    };

    command.SetHandler(async (InvocationContext ctx) =>
    {
        var templateJson = ctx.ParseResult.GetValueForOption(templateOption)!;
        var contextJson = ctx.ParseResult.GetValueForOption(contextOption);
        var output = ctx.ParseResult.GetValueForOption(outputOption)!;
        var storageRoot = ctx.ParseResult.GetValueForOption(storageRootOption);
        var azureConnection = ctx.ParseResult.GetValueForOption(azureConnectionOption);
        var azureContainer = ctx.ParseResult.GetValueForOption(azureContainerOption);
        var includeLogos = ctx.ParseResult.GetValueForOption(includeLogosOption);
        var emitPlaceholders = ctx.ParseResult.GetValueForOption(emitPlaceholdersOption);
        var verbose = ctx.ParseResult.GetValueForOption(verboseOption);

        await ExecuteWithGuardAsync(async () =>
        {
            if (!templateJson.Exists)
            {
                throw new FileNotFoundException("Template JSON not found.", templateJson.FullName);
            }

            if (contextJson is not null && !contextJson.Exists)
            {
                throw new FileNotFoundException("Context JSON not found.", contextJson.FullName);
            }

            EnsureParentDirectory(output);

            using var loggerFactory = CreateLoggerFactory(verbose);
            var storage = CreateObjectStorage(loggerFactory, storageRoot, azureConnection, azureContainer ?? "dtce-documents");

            var template = await ReadJsonAsync<TemplateJson>(templateJson.FullName)
                           ?? throw new InvalidDataException("Template JSON could not be deserialized.");

            ContextJson context;
            if (contextJson != null)
            {
                context = await ReadJsonAsync<ContextJson>(contextJson.FullName)
                           ?? new ContextJson();
            }
            else
            {
                context = new ContextJson();
            }

            var renderer = new TemplateDocumentRenderer(storage, loggerFactory.CreateLogger<TemplateDocumentRenderer>());
            var documentBytes = await renderer.RenderAsync(
                template,
                context,
                new TemplateRenderOptions
                {
                    IncludeLogos = includeLogos,
                    IncludeTemplateLogosFromStorage = includeLogos,
                    EmitPlaceholderForMissingContent = emitPlaceholders
                });

            await File.WriteAllBytesAsync(output.FullName, documentBytes);
            Console.WriteLine($"Template document written to {output.FullName}");
        });
    });

    return command;
}

static Command BuildRenderDocumentCommand(Option<bool> verboseOption)
{
    var templateOption = new Option<FileInfo>("--template-json", "Template JSON describing the document structure.")
    {
        IsRequired = true
    };

    var dataOption = new Option<FileInfo>("--data-json", "Context JSON containing the data to merge.")
    {
        IsRequired = true
    };

    var outputOption = new Option<FileInfo>("--output", "Destination DOCX path.")
    {
        IsRequired = true
    };

    var contentMapOption = new Option<FileInfo?>(
        "--content-map",
        () => null,
        "Optional JSON file with additional placeholder overrides (placeholderId -> text).");

    var contentOverrideOption = new Option<string[]?>(
        "--content",
        description: "Inline placeholder override in the form PlaceholderId=Value. Specify multiple times as needed.");

    var logoOverrideOption = new Option<string[]?>(
        "--logo",
        description: "Logo replacement mapping in the form AssetId=PathToImage. Specify multiple times as needed.");

    var emitPlaceholdersOption = new Option<bool>(
        "--emit-placeholders",
        () => false,
        "Emit {{placeholder}} tokens when data is missing.");

    var includeLogosOption = new Option<bool>(
        "--include-logos",
        () => true,
        "Include template logos when rendering (requires access to storage or logo overrides).");

    var storageRootOption = new Option<DirectoryInfo?>(
        "--local-storage-root",
        () => null,
        "Root folder for local file storage (defaults to ./cli-storage).");

    var azureConnectionOption = new Option<string?>(
        "--azure-connection-string",
        description: "Azure Storage connection string for blob-backed storage.");

    var azureContainerOption = new Option<string>(
        "--azure-container",
        () => "dtce-documents",
        "Azure Storage container name when using blob storage.");

    var command = new Command("render-document", "Render a new document using template/context JSON and optional overrides.")
    {
        templateOption,
        dataOption,
        outputOption,
        contentMapOption,
        contentOverrideOption,
        logoOverrideOption,
        emitPlaceholdersOption,
        includeLogosOption,
        storageRootOption,
        azureConnectionOption,
        azureContainerOption
    };

    command.SetHandler(async (InvocationContext ctx) =>
    {
        var templateJson = ctx.ParseResult.GetValueForOption(templateOption)!;
        var dataJson = ctx.ParseResult.GetValueForOption(dataOption)!;
        var output = ctx.ParseResult.GetValueForOption(outputOption)!;
        var contentMap = ctx.ParseResult.GetValueForOption(contentMapOption);
        var contentOverrides = ctx.ParseResult.GetValueForOption(contentOverrideOption);
        var logoOverrides = ctx.ParseResult.GetValueForOption(logoOverrideOption);
        var emitPlaceholders = ctx.ParseResult.GetValueForOption(emitPlaceholdersOption);
        var includeLogos = ctx.ParseResult.GetValueForOption(includeLogosOption);
        var storageRoot = ctx.ParseResult.GetValueForOption(storageRootOption);
        var azureConnection = ctx.ParseResult.GetValueForOption(azureConnectionOption);
        var azureContainer = ctx.ParseResult.GetValueForOption(azureContainerOption);
        var verbose = ctx.ParseResult.GetValueForOption(verboseOption);

        await ExecuteWithGuardAsync(async () =>
        {
            if (!templateJson.Exists)
            {
                throw new FileNotFoundException("Template JSON not found.", templateJson.FullName);
            }

            if (!dataJson.Exists)
            {
                throw new FileNotFoundException("Data JSON not found.", dataJson.FullName);
            }

            if (contentMap is not null && !contentMap.Exists)
            {
                throw new FileNotFoundException("Content map JSON not found.", contentMap.FullName);
            }

            EnsureParentDirectory(output);

            using var loggerFactory = CreateLoggerFactory(verbose);
            var storage = CreateObjectStorage(loggerFactory, storageRoot, azureConnection, azureContainer ?? "dtce-documents");

            var template = await ReadJsonAsync<TemplateJson>(templateJson.FullName)
                           ?? throw new InvalidDataException("Template JSON could not be deserialized.");

            var context = await ReadJsonAsync<ContextJson>(dataJson.FullName)
                          ?? throw new InvalidDataException("Data JSON could not be deserialized.");

            var options = new TemplateRenderOptions
            {
                IncludeLogos = includeLogos,
                IncludeTemplateLogosFromStorage = includeLogos,
                EmitPlaceholderForMissingContent = emitPlaceholders
            };

            ApplyContentOverrides(options, contentMap, contentOverrides);
            await ApplyLogoOverridesAsync(options, logoOverrides);

            var renderer = new TemplateDocumentRenderer(storage, loggerFactory.CreateLogger<TemplateDocumentRenderer>());
            var documentBytes = await renderer.RenderAsync(template, context, options);

            await File.WriteAllBytesAsync(output.FullName, documentBytes);
            Console.WriteLine($"Rendered document written to {output.FullName}");
        });
    });

    return command;
}

static async Task ExecuteWithGuardAsync(Func<Task> action)
{
    try
    {
        await action();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        Environment.ExitCode = -1;
    }
}

static async Task<ContextJson> BuildContextAsync(
    ParseResult parseResult,
    bool includeAnalysis,
    IObjectStorage storage,
    ILoggerFactory loggerFactory)
{
    var context = new ContextJson
{
    LinguisticStyle = new LinguisticStyleAttributes
    {
            OverallFormality = "unspecified",
            FormalityConfidenceScore = 0.0,
            DominantTone = "unspecified",
            ToneConfidenceScore = 0.0,
            WritingStyleVector = new List<double>()
    },
    ContentBlocks = parseResult.ContentSections
        .Select(section => new ContentBlock
        {
            PlaceholderId = section.PlaceholderId,
            SectionSampleText = section.SampleText,
            WordCount = section.WordCount
        })
        .ToList()
};

    if (!includeAnalysis)
    {
        return context;
    }

    var nlpAnalyzer = new NlpAnalyzer(loggerFactory.CreateLogger<NlpAnalyzer>());
    var combinedText = string.Join(Environment.NewLine, parseResult.ContentSections.Select(s => s.SampleText));
    var nlpResult = await nlpAnalyzer.AnalyzeAsync(combinedText);

    context.LinguisticStyle = new LinguisticStyleAttributes
    {
        OverallFormality = nlpResult.Formality,
        FormalityConfidenceScore = nlpResult.FormalityConfidence,
        DominantTone = nlpResult.Tone,
        ToneConfidenceScore = nlpResult.ToneConfidence,
        WritingStyleVector = nlpResult.StyleVector
    };

    var cvAnalyzer = new ComputerVisionAnalyzer(storage, loggerFactory.CreateLogger<ComputerVisionAnalyzer>());
    parseResult.TemplateJson.LogoMap = await cvAnalyzer.DetectLogosAsync(parseResult);

    return context;
}

static ILoggerFactory CreateLoggerFactory(bool verbose)
{
    return LoggerFactory.Create(builder =>
    {
        builder.AddSimpleConsole(options =>
        {
            options.IncludeScopes = false;
            options.SingleLine = true;
            options.TimestampFormat = "HH:mm:ss ";
        });
        builder.SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information);
    });
}

static IObjectStorage CreateObjectStorage(
    ILoggerFactory loggerFactory,
    DirectoryInfo? localRoot,
    string? azureConnectionString,
    string azureContainer)
{
    if (!string.IsNullOrWhiteSpace(azureConnectionString))
    {
        return new AzureBlobStorage(
            azureConnectionString,
            azureContainer,
            loggerFactory.CreateLogger<AzureBlobStorage>());
    }

    var rootPath = localRoot?.FullName ?? Path.Combine(AppContext.BaseDirectory, "cli-storage");
    var options = Options.Create(new FileSystemStorageOptions { RootPath = rootPath });
    return new FileSystemObjectStorage(
        loggerFactory.CreateLogger<FileSystemObjectStorage>(),
        options);
}

static async Task WriteJsonAsync<T>(string path, T value)
{
    await using var stream = File.Create(path);
    await JsonSerializer.SerializeAsync(stream, value, new JsonSerializerOptions
    {
        WriteIndented = true
    });
}

static async Task<T?> ReadJsonAsync<T>(string path)
{
    await using var stream = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<T>(stream, new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true
    });
}

static void ApplyContentOverrides(
    TemplateRenderOptions options,
    FileInfo? overridesFile,
    IEnumerable<string>? inlineOverrides)
{
    if (overridesFile != null)
    {
        var json = File.ReadAllText(overridesFile.FullName);
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        if (map != null)
        {
            foreach (var kvp in map)
            {
                options.ContentOverrides[kvp.Key] = kvp.Value;
            }
        }
    }

    if (inlineOverrides == null)
    {
        return;
    }

    foreach (var entry in inlineOverrides)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            continue;
        }

        var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 2)
        {
            options.ContentOverrides[parts[0]] = parts[1];
        }
    }
}

static async Task ApplyLogoOverridesAsync(TemplateRenderOptions options, IEnumerable<string>? overrides)
{
    if (overrides == null)
    {
        return;
    }

    foreach (var entry in overrides)
    {
        if (string.IsNullOrWhiteSpace(entry))
        {
            continue;
        }

        var parts = entry.Split('=', 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            continue;
        }

        var assetId = parts[0];
        var path = parts[1];
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("Logo override file not found.", path);
        }

        var bytes = await File.ReadAllBytesAsync(path);
        options.LogoOverrides[assetId] = bytes;
    }
}

static void EnsureParentDirectory(FileInfo file)
{
    var directory = file.Directory;
    if (directory != null && !directory.Exists)
    {
        directory.Create();
    }
}

static string GetContentType(FileInfo fileInfo)
{
    return Path.GetExtension(fileInfo.Name).ToLowerInvariant() switch
    {
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        _ => "application/octet-stream"
    };
}
