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

        object? app = null;
        try
        {
            app = GetActiveComObject(progId);
            result.ConnectedProgId = progId;
            result.ConnectedToRunningInstance = true;
            Console.WriteLine("  connected to running instance via ROT");
        }
        catch (Exception activeEx)
        {
            result.ActiveObjectErrors[progId] = DescribeException(activeEx);
            Console.WriteLine("  running instance not available: " + DescribeException(activeEx));

            try
            {
                app = Activator.CreateInstance(type);
                result.ConnectedProgId = progId;
                result.CreatedNewInstance = true;
                Console.WriteLine("  created new COM instance");
            }
            catch (Exception createEx)
            {
                result.CreateObjectErrors[progId] = DescribeException(createEx);
                Console.WriteLine("  create failed: " + DescribeException(createEx));
                continue;
            }
        }

        if (app is null) continue;

        result.Success = true;
        result.ApplicationType = app.GetType().FullName;

        SetProperty(app, "Visible", true, result.SetPropertyResults);
        SetProperty(app, "HideMessage", 1, result.SetPropertyResults);
        InvokeAndLog(app, "Application.ActivateControllerAPI", "ActivateControllerAPI", result.MethodResults);

        ReadProperty(app, "Visible", result.ApplicationProperties);
        ReadProperty(app, "Caption", result.ApplicationProperties);
        ReadProperty(app, "Version", result.ApplicationProperties);
        ReadProperty(app, "Name", result.ApplicationProperties);

        if (!string.IsNullOrWhiteSpace(modelPath) && File.Exists(modelPath))
        {
            TryOpenModel(app, modelPath, result);
        }

        Thread.Sleep(3000);
        result.ProcessesAfter = GetInterestingProcesses();
        Console.WriteLine("CAD-like processes after: " + FormatList(result.ProcessesAfter));

        var activeDocument = GetProperty(app, "ActiveDocument") ?? InvokeMethod(app, "ActiveDocument");
        if (activeDocument is null)
        {
            Console.WriteLine("Active document: not found after open attempts.");
            result.ActiveDocumentFound = false;
        }
        else
        {
            result.ActiveDocumentFound = true;
            result.ActiveDocumentType = activeDocument.GetType().FullName;
            ReadProperty(activeDocument, "Name", result.ActiveDocumentProperties);
            ReadProperty(activeDocument, "FileName", result.ActiveDocumentProperties);
            ReadProperty(activeDocument, "Path", result.ActiveDocumentProperties);
            ReadProperty(activeDocument, "DocumentType", result.ActiveDocumentProperties);
            ReadProperty(activeDocument, "Type", result.ActiveDocumentProperties);
            Console.WriteLine("Active document found: " + result.ActiveDocumentType);
        }

        break;
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

static void TryOpenModel(object app, string modelPath, SmokeResult result)
{
    Console.WriteLine("Trying to open model: " + modelPath);

    var documents = GetProperty(app, "Documents");
    if (documents is not null)
    {
        Console.WriteLine("  Documents object found: " + documents.GetType().FullName);
        TryOpenDocumentsObject(documents, modelPath, result);
    }
    else
    {
        Console.WriteLine("  Documents object not found");
    }

    TryOpenApplicationObject(app, modelPath, result);
}

static void TryOpenDocumentsObject(object documents, string modelPath, SmokeResult result)
{
    var methodNames = new[] { "Open", "OpenDocument", "Add", "OpenByFileName" };
    foreach (var methodName in methodNames)
    {
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, true);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, false);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, true, false);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, false, false);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, true, true);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, false, true);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, 0);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, 1);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, 0, true);
        InvokeAndLog(documents, "Documents." + methodName, methodName, result.OpenAttempts, modelPath, 1, true);
    }
}

static void TryOpenApplicationObject(object app, string modelPath, SmokeResult result)
{
    var methodNames = new[] { "OpenDocument", "DocumentOpen", "Open", "OpenDoc", "ksOpenDocument" };
    foreach (var methodName in methodNames)
    {
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath);
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath, true);
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath, false);
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath, true, false);
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath, false, false);
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath, 0);
        InvokeAndLog(app, "Application." + methodName, methodName, result.OpenAttempts, modelPath, 1);
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

static void InvokeAndLog(object target, string label, string actualMethodName, Dictionary<string, string?> output, params object[] args)
{
    var key = label + "(" + args.Length + "):[" + string.Join(",", args.Select(a => a?.GetType().Name ?? "null")) + "]";
    try
    {
        var value = target.GetType().InvokeMember(actualMethodName, BindingFlags.InvokeMethod, null, target, args.Length == 0 ? null : args);
        output[key] = value?.ToString() ?? "OK";
        Console.WriteLine($"  method {key}: {output[key]}");
    }
    catch (Exception ex)
    {
        output[key] = "ERROR: " + DescribeException(ex);
        Console.WriteLine($"  method {key}: ERROR: {DescribeException(ex)}");
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
    public Dictionary<string, string?> MethodResults { get; } = new();
    public Dictionary<string, string?> OpenAttempts { get; } = new();
    public bool ActiveDocumentFound { get; set; }
    public string? ActiveDocumentType { get; set; }
    public Dictionary<string, string?> ActiveDocumentProperties { get; } = new();
    public Dictionary<string, string> ActiveObjectErrors { get; } = new();
    public Dictionary<string, string> CreateObjectErrors { get; } = new();
    public string? FatalError { get; set; }
}
