using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Json;

var result = new SmokeResult
{
    StartedAt = DateTimeOffset.Now,
    MachineName = Environment.MachineName,
    UserName = Environment.UserName,
    ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    OsDescription = RuntimeInformation.OSDescription,
    DotNetVersion = Environment.Version.ToString()
};

var jsonLogPath = GetArgValue(args, "--json-log");
var modelPath = GetArgValue(args, "--model-path");
result.ModelPath = modelPath;

try
{
    Console.OutputEncoding = System.Text.Encoding.UTF8;
    Console.WriteLine("CAD Assist KOMPAS-3D smoke test");
    Console.WriteLine($"Machine: {result.MachineName}");
    Console.WriteLine($"User: {result.UserName}");
    Console.WriteLine($"Model path: {modelPath ?? "<not provided>"}");

    if (!string.IsNullOrWhiteSpace(modelPath))
    {
        result.ModelFileExists = File.Exists(modelPath);
        Console.WriteLine($"Model file exists: {result.ModelFileExists}");
    }

    result.ProcessesBefore = GetInterestingProcesses();
    Console.WriteLine("CAD-like processes before: " + FormatList(result.ProcessesBefore));

    var app = CreateOrConnectKompas(result);
    if (app is null)
    {
        result.Success = false;
        Console.WriteLine("KOMPAS application object was not created.");
        return 1;
    }

    result.Success = true;
    result.ApplicationType = app.GetType().FullName;

    SetProperty(app, "Visible", true, result.SetPropertyResults);
    SetProperty(app, "HideMessage", 1, result.SetPropertyResults);

    ReadProperty(app, "Visible", result.ApplicationProperties);
    ReadProperty(app, "Caption", result.ApplicationProperties);
    ReadProperty(app, "Version", result.ApplicationProperties);
    ReadProperty(app, "Name", result.ApplicationProperties);

    object? openedDocument = null;
    if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
    {
        openedDocument = OpenDocument(app, modelPath, result);
    }

    Thread.Sleep(1500);
    result.ProcessesAfter = GetInterestingProcesses();
    Console.WriteLine("CAD-like processes after: " + FormatList(result.ProcessesAfter));

    var activeDocument = openedDocument ?? GetProperty(app, "ActiveDocument") ?? InvokeMethod(app, "ActiveDocument");
    if (activeDocument is null)
    {
        Console.WriteLine("Active document: not found.");
        result.ActiveDocumentFound = false;
        result.Success = false;
    }
    else
    {
        result.ActiveDocumentFound = true;
        result.ActiveDocumentType = activeDocument.GetType().FullName;
        Console.WriteLine("Active document found: " + result.ActiveDocumentType);
        ReadDocumentInfo(activeDocument, result.ActiveDocumentProperties);

        if (!string.IsNullOrWhiteSpace(modelPath))
        {
            var contextPath = CreateProjectContext(modelPath, result);
            result.ProjectContextPath = contextPath;
            Console.WriteLine("CAD Assist project context written: " + contextPath);
        }
    }
}
catch (Exception ex)
{
    result.Success = false;
    result.FatalError = DescribeException(ex);
    Console.WriteLine(ex);
}
finally
{
    result.FinishedAt = DateTimeOffset.Now;
    if (!string.IsNullOrWhiteSpace(jsonLogPath))
    {
        var directory = Path.GetDirectoryName(jsonLogPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(jsonLogPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
}

return result.Success ? 0 : 1;

static object? CreateOrConnectKompas(SmokeResult result)
{
    var progIds = new[]
    {
        "KOMPAS.Application.7",
        "Kompas.Application.7",
        "KOMPAS.Application.5",
        "Kompas.Application.5",
        "KOMPAS.Application",
        "Kompas.Application"
    };

    foreach (var progId in progIds)
    {
        Console.WriteLine($"Trying COM ProgID: {progId}");
        result.TriedProgIds.Add(progId);

        var type = Type.GetTypeFromProgID(progId);
        if (type is null)
        {
            Console.WriteLine("  not registered");
            continue;
        }

        result.RegisteredProgIds.Add(progId);
        Console.WriteLine("  registered");

        try
        {
            var runningApp = GetActiveComObject(progId);
            result.ConnectedProgId = progId;
            result.ConnectedToRunningInstance = true;
            Console.WriteLine("  connected to running instance via ROT");
            return runningApp;
        }
        catch (Exception activeEx)
        {
            result.ActiveObjectErrors[progId] = DescribeException(activeEx);
            Console.WriteLine("  running instance not available: " + DescribeException(activeEx));
        }

        try
        {
            var createdApp = Activator.CreateInstance(type);
            result.ConnectedProgId = progId;
            result.CreatedNewInstance = true;
            Console.WriteLine("  created new COM instance");
            return createdApp;
        }
        catch (Exception createEx)
        {
            result.CreateObjectErrors[progId] = DescribeException(createEx);
            Console.WriteLine("  create failed: " + DescribeException(createEx));
        }
    }

    return null;
}

static object? OpenDocument(object app, string modelPath, SmokeResult result)
{
    Console.WriteLine("Opening model via Documents.Open(path, true, false): " + modelPath);

    var documents = GetProperty(app, "Documents");
    if (documents is null)
    {
        result.OpenResult = "ERROR: Documents object not found";
        Console.WriteLine("  Documents object not found");
        return null;
    }

    try
    {
        var document = documents.GetType().InvokeMember(
            "Open",
            BindingFlags.InvokeMethod,
            null,
            documents,
            new object[] { modelPath, true, false });

        result.OpenResult = document is null ? "OK: null" : "OK: " + document.GetType().FullName;
        Console.WriteLine("  Documents.Open result: " + result.OpenResult);
        return document;
    }
    catch (Exception ex)
    {
        result.OpenResult = "ERROR: " + DescribeException(ex);
        Console.WriteLine("  Documents.Open failed: " + DescribeException(ex));
        return null;
    }
}

static void ReadDocumentInfo(object document, Dictionary<string, string?> output)
{
    ReadProperty(document, "Name", output);
    ReadProperty(document, "FileName", output);
    ReadProperty(document, "Path", output);
    ReadProperty(document, "DocumentType", output);
    ReadProperty(document, "Type", output);
}

static string CreateProjectContext(string modelPath, SmokeResult result)
{
    var contextPath = modelPath + ".cadassist.json";
    var now = DateTimeOffset.Now;

    var context = new ProjectContext
    {
        ProjectName = "CAD Assist demo project",
        CadSystem = "KOMPAS-3D",
        ModelPath = modelPath,
        DocumentName = result.ActiveDocumentProperties.GetValueOrDefault("Name"),
        DocumentDirectory = result.ActiveDocumentProperties.GetValueOrDefault("Path"),
        DocumentType = result.ActiveDocumentProperties.GetValueOrDefault("DocumentType"),
        Type = result.ActiveDocumentProperties.GetValueOrDefault("Type"),
        CreatedAt = now,
        UpdatedAt = now,
        Tasks = new List<ProjectTask>
        {
            new()
            {
                Id = "TASK-001",
                Title = "Проверить корректность модели",
                Description = "Автоматически созданная тестовая задача после открытия модели через API КОМПАС-3D.",
                Status = "Новая",
                Assignee = Environment.UserName,
                LinkedCadObject = result.ActiveDocumentProperties.GetValueOrDefault("Name") ?? Path.GetFileName(modelPath),
                CreatedAt = now
            }
        },
        Requirements = new List<ProjectRequirement>
        {
            new()
            {
                Id = "REQ-001",
                Title = "Модель должна быть доступна через интеграцию КОМПАС-3D",
                Status = "Выполнено"
            }
        },
        ActivityLog = new List<ActivityLogItem>
        {
            new()
            {
                At = now,
                Actor = Environment.UserName,
                Action = "Открыта модель через COM API КОМПАС-3D и создан проектный контекст CAD Assist"
            }
        }
    };

    File.WriteAllText(contextPath, JsonSerializer.Serialize(context, new JsonSerializerOptions { WriteIndented = true }));
    return contextPath;
}

static string? GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    }

    return null;
}

static string[] GetInterestingProcesses()
{
    var keywords = new[] { "kompas", "k3", "ascon", "cad", "cadassist" };
    return Process.GetProcesses()
        .Select(SafeProcessName)
        .Where(name => keywords.Any(keyword => name.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        .Distinct()
        .OrderBy(name => name)
        .ToArray();
}

static string SafeProcessName(Process process)
{
    try { return process.ProcessName; }
    catch { return "<unknown>"; }
}

static string FormatList(string[] items) => items.Length == 0 ? "not found" : string.Join(", ", items);

static object GetActiveComObject(string progId)
{
    var clsid = Type.GetTypeFromProgID(progId)?.GUID
        ?? throw new InvalidOperationException($"ProgID is not registered: {progId}");

    Ole32.GetRunningObjectTable(0, out var rot).ThrowIfFailed();
    Ole32.CreateBindCtx(0, out var bindCtx).ThrowIfFailed();
    rot.EnumRunning(out var enumMoniker);
    var monikers = new IMoniker[1];

    while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0)
    {
        monikers[0].GetDisplayName(bindCtx, null, out var displayName);
        if (!displayName.Contains(progId, StringComparison.OrdinalIgnoreCase) &&
            !displayName.Contains(clsid.ToString("B"), StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        rot.GetObject(monikers[0], out var runningObject);
        return runningObject;
    }

    throw new InvalidOperationException($"Running COM object not found in ROT: {progId}");
}

static object? GetProperty(object target, string propertyName)
{
    try
    {
        return target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, null);
    }
    catch
    {
        return null;
    }
}

static object? InvokeMethod(object target, string methodName, params object[] args)
{
    try
    {
        return target.GetType().InvokeMember(methodName, BindingFlags.InvokeMethod, null, target, args.Length == 0 ? null : args);
    }
    catch
    {
        return null;
    }
}

static void SetProperty(object target, string propertyName, object value, Dictionary<string, string?> output)
{
    try
    {
        target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, new[] { value });
        output[propertyName] = "OK";
        Console.WriteLine($"  set {propertyName}: OK");
    }
    catch (Exception ex)
    {
        output[propertyName] = "ERROR: " + DescribeException(ex);
        Console.WriteLine($"  set {propertyName}: ERROR: {DescribeException(ex)}");
    }
}

static void ReadProperty(object target, string propertyName, Dictionary<string, string?> output)
{
    try
    {
        var value = GetProperty(target, propertyName);
        output[propertyName] = value?.ToString();
        Console.WriteLine($"  {propertyName}: {output[propertyName] ?? "<null>"}");
    }
    catch (Exception ex)
    {
        output[propertyName] = "ERROR: " + DescribeException(ex);
        Console.WriteLine($"  {propertyName}: ERROR: {DescribeException(ex)}");
    }
}

static string DescribeException(Exception ex)
{
    var current = ex;
    while (current is TargetInvocationException && current.InnerException is not null)
    {
        current = current.InnerException;
    }

    if (current is COMException comException)
    {
        return $"{comException.Message} (HRESULT: 0x{comException.HResult:X8})";
    }

    return current.Message;
}

internal static class Ole32
{
    [DllImport("ole32.dll")]
    public static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);

    [DllImport("ole32.dll")]
    public static extern int CreateBindCtx(int reserved, out IBindCtx bindCtx);

    public static void ThrowIfFailed(this int hresult)
    {
        if (hresult < 0)
        {
            Marshal.ThrowExceptionForHR(hresult);
        }
    }
}

sealed class SmokeResult
{
    public bool Success { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string? MachineName { get; set; }
    public string? UserName { get; set; }
    public string? ProcessArchitecture { get; set; }
    public string? OsDescription { get; set; }
    public string? DotNetVersion { get; set; }
    public string? ModelPath { get; set; }
    public bool ModelFileExists { get; set; }
    public string[] ProcessesBefore { get; set; } = Array.Empty<string>();
    public string[] ProcessesAfter { get; set; } = Array.Empty<string>();
    public List<string> TriedProgIds { get; } = new();
    public List<string> RegisteredProgIds { get; } = new();
    public string? ConnectedProgId { get; set; }
    public bool ConnectedToRunningInstance { get; set; }
    public bool CreatedNewInstance { get; set; }
    public string? ApplicationType { get; set; }
    public Dictionary<string, string?> ApplicationProperties { get; } = new();
    public Dictionary<string, string?> SetPropertyResults { get; } = new();
    public bool ActiveDocumentFound { get; set; }
    public string? ActiveDocumentType { get; set; }
    public string? OpenResult { get; set; }
    public string? ProjectContextPath { get; set; }
    public Dictionary<string, string?> ActiveDocumentProperties { get; } = new();
    public Dictionary<string, string> ActiveObjectErrors { get; } = new();
    public Dictionary<string, string> CreateObjectErrors { get; } = new();
    public string? FatalError { get; set; }
}

sealed class ProjectContext
{
    public string ProjectName { get; set; } = "";
    public string CadSystem { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public string? DocumentName { get; set; }
    public string? DocumentDirectory { get; set; }
    public string? DocumentType { get; set; }
    public string? Type { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<ProjectTask> Tasks { get; set; } = new();
    public List<ProjectRequirement> Requirements { get; set; } = new();
    public List<ActivityLogItem> ActivityLog { get; set; } = new();
}

sealed class ProjectTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string LinkedCadObject { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

sealed class ProjectRequirement
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
}

sealed class ActivityLogItem
{
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
}
