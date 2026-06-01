using CadAssist.Kompas.Interop;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;

var jsonLogPath = GetArgValue(args, "--json-log");
var modelPath = GetArgValue(args, "--model-path");
var addTaskTitle = GetArgValue(args, "--add-task");
var runSmokeTest = HasArg(args, "--smoke-test");

if (!runSmokeTest)
{
    modelPath = ResolveTaskWindowModelPath(modelPath);
    OpenTaskWindowOnStaThread(modelPath);
    return 0;
}

if (string.IsNullOrWhiteSpace(modelPath))
{
    modelPath = @"C:\cad-assist-test\test.m3d";
}

var result = new SmokeResult
{
    StartedAt = DateTimeOffset.Now,
    MachineName = Environment.MachineName,
    UserName = Environment.UserName,
    ProcessArchitecture = RuntimeInformation.ProcessArchitecture.ToString(),
    OsDescription = RuntimeInformation.OSDescription,
    DotNetVersion = Environment.Version.ToString(),
    ModelPath = modelPath,
    AddTaskTitle = addTaskTitle
};

try
{
    Console.WriteLine("CAD Assist KOMPAS-3D smoke test");
    Console.WriteLine($"Machine: {result.MachineName}");
    Console.WriteLine($"User: {result.UserName}");
    Console.WriteLine($"Model path: {modelPath}");
    Console.WriteLine($"Task to add: {addTaskTitle ?? "<not provided>"}");

    result.ModelFileExists = File.Exists(modelPath);
    Console.WriteLine($"Model file exists: {result.ModelFileExists}");

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
    if (File.Exists(modelPath))
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

        var contextPath = CreateProjectContext(modelPath, result);
        result.ProjectContextPath = contextPath;
        Console.WriteLine("CAD Assist project context written: " + contextPath);
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
        File.WriteAllText(jsonLogPath, JsonSerializer.Serialize(result, JsonOptions()));
    }
}

return result.Success ? 0 : 1;

static string ResolveTaskWindowModelPath(string? explicitModelPath)
{
    if (!string.IsNullOrWhiteSpace(explicitModelPath))
    {
        return explicitModelPath;
    }

    var activePath = TryGetActiveKompasDocumentPath();
    if (!string.IsNullOrWhiteSpace(activePath))
    {
        return activePath;
    }

    return @"C:\cad-assist-test\test.m3d";
}

static string? TryGetActiveKompasDocumentPath()
{
    try
    {
        var result = new SmokeResult();
        var app = CreateOrConnectKompas(result);
        if (app is null) return null;

        var activeDocument = GetProperty(app, "ActiveDocument") ?? InvokeMethod(app, "ActiveDocument");
        if (activeDocument is null) return null;

        var pathName = ReadStringProperty(activeDocument, "PathName");
        if (!string.IsNullOrWhiteSpace(pathName) && File.Exists(pathName)) return pathName;

        var fullPath = ReadStringProperty(activeDocument, "FileName");
        if (!string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath)) return fullPath;

        var path = ReadStringProperty(activeDocument, "Path");
        var name = ReadStringProperty(activeDocument, "Name");
        if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name))
        {
            var combined = Path.Combine(path, name);
            if (File.Exists(combined)) return combined;
        }
    }
    catch
    {
        return null;
    }

    return null;
}

static string? ReadStringProperty(object target, string propertyName)
{
    try
    {
        return GetProperty(target, propertyName)?.ToString();
    }
    catch
    {
        return null;
    }
}

static void OpenTaskWindowOnStaThread(string modelPath)
{
    Exception? uiException = null;

    var uiThread = new Thread(() =>
    {
        try
        {
            var wpfApp = new Application
            {
                ShutdownMode = ShutdownMode.OnMainWindowClose
            };
            var window = new TaskListWindow(modelPath);
            wpfApp.Run(window);
        }
        catch (Exception ex)
        {
            uiException = ex;
        }
    });

    uiThread.SetApartmentState(ApartmentState.STA);
    uiThread.Start();
    uiThread.Join();

    if (uiException is not null)
    {
        throw uiException;
    }
}

static JsonSerializerOptions JsonOptions() => new()
{
    WriteIndented = true,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
};

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
    ReadProperty(document, "PathName", output);
    ReadProperty(document, "Path", output);
    ReadProperty(document, "DocumentType", output);
    ReadProperty(document, "Type", output);
}

static string CreateProjectContext(string modelPath, SmokeResult result)
{
    var contextPath = modelPath + ".cadassist.json";
    var now = DateTimeOffset.Now;
    var options = JsonOptions();
    var context = LoadOrCreateProjectContext(contextPath, modelPath, result, now, options);

    context.ModelPath = modelPath;
    context.DocumentName = result.ActiveDocumentProperties.GetValueOrDefault("Name") ?? context.DocumentName;
    context.DocumentDirectory = result.ActiveDocumentProperties.GetValueOrDefault("Path") ?? context.DocumentDirectory;
    context.DocumentType = result.ActiveDocumentProperties.GetValueOrDefault("DocumentType") ?? context.DocumentType;
    context.Type = result.ActiveDocumentProperties.GetValueOrDefault("Type") ?? context.Type;
    context.UpdatedAt = now;
    EnsureDefaultRequirement(context);

    Console.WriteLine($"Existing task count before: {context.Tasks.Count}");
    Console.WriteLine("Tasks before:");
    PrintTasks(context.Tasks);
    result.TaskCountBefore = context.Tasks.Count;

    if (!string.IsNullOrWhiteSpace(result.AddTaskTitle))
    {
        var task = new ProjectTask
        {
            Id = NextTaskId(context.Tasks),
            Title = result.AddTaskTitle,
            Description = "Задача добавлена через CAD Assist после открытия модели через API КОМПАС-3D.",
            Status = "Новая",
            Assignee = Environment.UserName,
            LinkedCadObject = context.DocumentName ?? Path.GetFileName(modelPath),
            CreatedAt = now
        };
        context.Tasks.Add(task);
        result.AddedTaskId = task.Id;
        Console.WriteLine($"Added project task: {task.Id} | {task.Status} | {task.Title}");
    }
    else
    {
        Console.WriteLine("No task title was provided. Task list was not changed.");
    }

    context.ActivityLog.Add(new ActivityLogItem
    {
        At = now,
        Actor = Environment.UserName,
        Action = string.IsNullOrWhiteSpace(result.AddTaskTitle)
            ? "Открыта модель через COM API КОМПАС-3D и обновлён проектный контекст CAD Assist"
            : $"Добавлена проектная задача '{result.AddTaskTitle}' к модели через CAD Assist"
    });

    Console.WriteLine($"Task count after: {context.Tasks.Count}");
    Console.WriteLine("Tasks after:");
    PrintTasks(context.Tasks);
    result.TaskCountAfter = context.Tasks.Count;
    result.TasksAfter = context.Tasks.Select(t => $"{t.Id} | {t.Status} | {t.Title}").ToArray();

    File.WriteAllText(contextPath, JsonSerializer.Serialize(context, options));
    return contextPath;
}

static ProjectContext LoadOrCreateProjectContext(string contextPath, string modelPath, SmokeResult result, DateTimeOffset now, JsonSerializerOptions options)
{
    if (File.Exists(contextPath))
    {
        try
        {
            var existing = JsonSerializer.Deserialize<ProjectContext>(File.ReadAllText(contextPath), options);
            if (existing is not null)
            {
                Console.WriteLine("Existing CAD Assist project context loaded: " + contextPath);
                return existing;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Existing context could not be read, a new one will be created: " + DescribeException(ex));
        }
    }

    Console.WriteLine("Creating new CAD Assist project context: " + contextPath);
    return new ProjectContext
    {
        ProjectName = "CAD Assist demo project",
        CadSystem = "KOMPAS-3D",
        ModelPath = modelPath,
        DocumentName = result.ActiveDocumentProperties.GetValueOrDefault("Name"),
        DocumentDirectory = result.ActiveDocumentProperties.GetValueOrDefault("Path"),
        DocumentType = result.ActiveDocumentProperties.GetValueOrDefault("DocumentType"),
        Type = result.ActiveDocumentProperties.GetValueOrDefault("Type"),
        CreatedAt = now,
        UpdatedAt = now
    };
}

static void EnsureDefaultRequirement(ProjectContext context)
{
    if (context.Requirements.Any(r => r.Id == "REQ-001")) return;
    context.Requirements.Add(new ProjectRequirement
    {
        Id = "REQ-001",
        Title = "Модель должна быть доступна через интеграцию КОМПАС-3D",
        Status = "Выполнено"
    });
}

static string NextTaskId(List<ProjectTask> tasks)
{
    var max = tasks
        .Select(task => task.Id)
        .Select(id => id.StartsWith("TASK-", StringComparison.OrdinalIgnoreCase) && int.TryParse(id[5..], out var n) ? n : 0)
        .DefaultIfEmpty(0)
        .Max();
    return $"TASK-{max + 1:000}";
}

static void PrintTasks(List<ProjectTask> tasks)
{
    if (tasks.Count == 0)
    {
        Console.WriteLine("  <no tasks>");
        return;
    }

    foreach (var task in tasks)
    {
        Console.WriteLine($"  {task.Id} | {task.Status} | {task.Title}");
    }
}

static string? GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
    }

    return null;
}

static bool HasArg(string[] args, string name) => args.Any(arg => string.Equals(arg, name, StringComparison.OrdinalIgnoreCase));

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

public sealed class SmokeResult
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
    public string? AddTaskTitle { get; set; }
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
    public string? AddedTaskId { get; set; }
    public int TaskCountBefore { get; set; }
    public int TaskCountAfter { get; set; }
    public string[] TasksAfter { get; set; } = Array.Empty<string>();
    public Dictionary<string, string?> ActiveDocumentProperties { get; } = new();
    public Dictionary<string, string> ActiveObjectErrors { get; } = new();
    public Dictionary<string, string> CreateObjectErrors { get; } = new();
    public string? FatalError { get; set; }
}
