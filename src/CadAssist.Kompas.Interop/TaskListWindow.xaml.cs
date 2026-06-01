using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CadAssist.Kompas.Interop;

public partial class TaskListWindow : Window
{
    private readonly string _modelPath;
    private readonly string _projectDirectory;
    private readonly string _contextPath;
    private readonly ObservableCollection<ProjectTask> _tasks = new();

    public TaskListWindow(string modelPath)
    {
        InitializeComponent();

        _modelPath = modelPath;
        _projectDirectory = Path.GetDirectoryName(modelPath) ?? Environment.CurrentDirectory;
        _contextPath = Path.Combine(_projectDirectory, "project.cadassist.json");
        TasksGrid.ItemsSource = _tasks;
        TasksGrid.PreviewMouseLeftButtonUp += TasksGrid_PreviewMouseLeftButtonUp;

        ModelPathText.Text = "Текущая модель: " + _modelPath;
        ContextPathText.Text = "Проект: " + _contextPath;

        EnsureContextExists();
        LoadTasks();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => LoadTasks();

    private void AddTaskButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var context = LoadContextOrCreateEmpty();
            var now = DateTimeOffset.Now;
            var modelFileName = Path.GetFileName(_modelPath);
            var task = new ProjectTask
            {
                Id = NextTaskId(context.Tasks),
                Title = "Новая задача из интерфейса CAD Assist",
                Description = "Тестовая задача, добавленная из окна списка задач.",
                Status = "Новая",
                Assignee = Environment.UserName,
                LinkedCadObject = modelFileName,
                ModelPath = modelFileName,
                CreatedAt = now
            };

            context.Tasks.Add(task);
            context.UpdatedAt = now;
            context.ActivityLog.Add(new ActivityLogItem
            {
                At = now,
                Actor = Environment.UserName,
                Action = $"Добавлена задача '{task.Title}' для модели '{modelFileName}' из окна CAD Assist"
            });

            SaveContext(context);
            LoadTasks();
            StatusText.Text = $"Добавлена задача {task.Id}.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка добавления задачи: " + ex.Message;
        }
    }

    private void CompleteTaskButton_Click(object sender, RoutedEventArgs e)
    {
        if (TasksGrid.SelectedItem is not ProjectTask selectedTask)
        {
            StatusText.Text = "Выберите задачу для завершения.";
            return;
        }

        if (string.Equals(selectedTask.Status, "Завершена", StringComparison.OrdinalIgnoreCase))
        {
            StatusText.Text = $"Задача {selectedTask.Id} уже завершена.";
            return;
        }

        var dialog = new CompleteTaskDialog(selectedTask)
        {
            Owner = this
        };

        if (dialog.ShowDialog() != true) return;

        try
        {
            var context = LoadContextOrCreateEmpty();
            var task = context.Tasks.FirstOrDefault(t => string.Equals(t.Id, selectedTask.Id, StringComparison.OrdinalIgnoreCase));
            if (task is null)
            {
                StatusText.Text = $"Задача {selectedTask.Id} не найдена в проектном файле.";
                return;
            }

            var now = DateTimeOffset.Now;
            task.Status = "Завершена";
            task.CompletedAt = now;
            task.CompletedBy = Environment.UserName;
            task.CompletionComment = dialog.CommentText;
            context.UpdatedAt = now;
            context.ActivityLog.Add(new ActivityLogItem
            {
                At = now,
                Actor = Environment.UserName,
                Action = $"Задача {task.Id} завершена. Комментарий: {dialog.CommentText}"
            });

            SaveContext(context);
            LoadTasks();
            StatusText.Text = $"Задача {task.Id} завершена.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка завершения задачи: " + ex.Message;
        }
    }

    private void TasksGrid_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (FindParent<DataGridRow>(e.OriginalSource as DependencyObject) is null) return;
        if (TasksGrid.SelectedItem is ProjectTask task) OpenTaskModel(task);
    }

    private void OpenTaskModel(ProjectTask task)
    {
        try
        {
            var modelFileName = string.IsNullOrWhiteSpace(task.ModelPath) ? task.LinkedCadObject : task.ModelPath;
            if (string.IsNullOrWhiteSpace(modelFileName))
            {
                StatusText.Text = $"У задачи {task.Id} не указан файл модели.";
                return;
            }

            var fullPath = Path.IsPathRooted(modelFileName)
                ? modelFileName
                : Path.Combine(_projectDirectory, modelFileName);

            if (!File.Exists(fullPath))
            {
                StatusText.Text = $"Файл модели не найден: {fullPath}";
                return;
            }

            var openedNewDocument = OpenModelInKompas(fullPath);
            StatusText.Text = openedNewDocument
                ? $"Открыта модель в текущем экземпляре КОМПАС: {fullPath}"
                : $"Модель уже была открыта, переключение выполнено: {fullPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка открытия модели: " + DescribeException(ex);
        }
    }

    private static T? FindParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child is not null)
        {
            if (child is T typed) return typed;
            child = System.Windows.Media.VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void EnsureContextExists()
    {
        if (File.Exists(_contextPath)) return;

        var context = LoadContextOrCreateEmpty();
        var now = DateTimeOffset.Now;
        var modelFileName = Path.GetFileName(_modelPath);
        context.Tasks.Add(new ProjectTask
        {
            Id = "TASK-001",
            Title = "Проверить корректность модели",
            Description = "Стартовая задача, созданная автоматически при первом запуске окна CAD Assist.",
            Status = "Новая",
            Assignee = Environment.UserName,
            LinkedCadObject = modelFileName,
            ModelPath = modelFileName,
            CreatedAt = now
        });
        context.ActivityLog.Add(new ActivityLogItem
        {
            At = now,
            Actor = Environment.UserName,
            Action = "Создан стартовый проектный контекст CAD Assist"
        });
        SaveContext(context);
    }

    private void LoadTasks()
    {
        _tasks.Clear();

        try
        {
            var context = LoadContextOrCreateEmpty();
            foreach (var task in context.Tasks.OrderBy(t => t.Status == "Завершена").ThenBy(t => t.Id))
            {
                NormalizeTaskModelReference(task);
                _tasks.Add(task);
            }

            SummaryText.Text = $"Задач: {_tasks.Count}";
            StatusText.Text = File.Exists(_contextPath)
                ? $"Задачи загружены: {_tasks.Count}. Проект: {_contextPath}"
                : "Файл проекта будет создан при добавлении задачи.";
        }
        catch (Exception ex)
        {
            SummaryText.Text = "Ошибка";
            StatusText.Text = "Не удалось прочитать задачи: " + ex.Message;
        }
    }

    private ProjectContext LoadContextOrCreateEmpty()
    {
        if (!File.Exists(_contextPath)) return CreateEmptyContext();

        var json = File.ReadAllText(_contextPath);
        var context = JsonSerializer.Deserialize<ProjectContext>(json, JsonOptions()) ?? CreateEmptyContext();
        EnsureCollections(context);
        if (string.IsNullOrWhiteSpace(context.ProjectDirectory)) context.ProjectDirectory = _projectDirectory;
        return context;
    }

    private ProjectContext CreateEmptyContext()
    {
        return new ProjectContext
        {
            ProjectName = "CAD Assist project",
            CadSystem = "KOMPAS-3D",
            ModelPath = Path.GetFileName(_modelPath),
            ProjectDirectory = _projectDirectory,
            DocumentName = Path.GetFileName(_modelPath),
            DocumentDirectory = _projectDirectory,
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private void SaveContext(ProjectContext context)
    {
        EnsureCollections(context);
        context.ProjectDirectory = _projectDirectory;
        foreach (var task in context.Tasks) NormalizeTaskModelReference(task);

        var directory = Path.GetDirectoryName(_contextPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_contextPath, JsonSerializer.Serialize(context, JsonOptions()));
    }

    private void NormalizeTaskModelReference(ProjectTask task)
    {
        if (string.IsNullOrWhiteSpace(task.ModelPath))
        {
            task.ModelPath = !string.IsNullOrWhiteSpace(task.LinkedCadObject)
                ? task.LinkedCadObject
                : Path.GetFileName(_modelPath);
        }

        if (Path.IsPathRooted(task.ModelPath)) task.ModelPath = Path.GetFileName(task.ModelPath);
        if (string.IsNullOrWhiteSpace(task.LinkedCadObject)) task.LinkedCadObject = task.ModelPath;
    }

    private static void EnsureCollections(ProjectContext context)
    {
        context.Tasks ??= new List<ProjectTask>();
        context.Requirements ??= new List<ProjectRequirement>();
        context.ActivityLog ??= new List<ActivityLogItem>();
    }

    private static string NextTaskId(List<ProjectTask> tasks)
    {
        var max = tasks
            .Select(task => task.Id)
            .Select(id => id.StartsWith("TASK-", StringComparison.OrdinalIgnoreCase) && int.TryParse(id[5..], out var n) ? n : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"TASK-{max + 1:000}";
    }

    private static bool OpenModelInKompas(string modelPath)
    {
        var app = GetRunningKompasApplication()
            ?? throw new InvalidOperationException("Не найден запущенный экземпляр КОМПАС. Откройте КОМПАС и проект, затем повторите попытку.");

        SetComProperty(app, "Visible", true);
        SetComProperty(app, "HideMessage", 1);

        if (TryActivateOpenDocument(app, modelPath)) return false;

        var documents = GetComProperty(app, "Documents")
            ?? throw new InvalidOperationException("Не удалось получить объект Documents у текущего экземпляра КОМПАС.");

        documents.GetType().InvokeMember(
            "Open",
            BindingFlags.InvokeMethod,
            null,
            documents,
            new object[] { modelPath, true, false });

        return true;
    }

    private static bool TryActivateOpenDocument(object app, string modelPath)
    {
        var targetPath = NormalizePath(modelPath);
        var documents = GetComProperty(app, "Documents");
        if (documents is null) return false;

        foreach (var document in EnumerateComCollection(documents))
        {
            var documentPath = GetDocumentPath(document);
            if (!string.Equals(NormalizePath(documentPath), targetPath, StringComparison.OrdinalIgnoreCase)) continue;

            TryCallComMethod(document, "Activate");
            TryCallComMethod(document, "SetCurrent");
            return true;
        }

        return false;
    }

    private static IEnumerable<object> EnumerateComCollection(object collection)
    {
        var count = TryGetComIntProperty(collection, "Count");
        if (count <= 0) yield break;

        for (var i = 0; i < count; i++)
        {
            var item = TryGetCollectionItem(collection, i) ?? TryGetCollectionItem(collection, i + 1);
            if (item is not null) yield return item;
        }
    }

    private static object? TryGetCollectionItem(object collection, int index)
    {
        return TryCallComMethod(collection, "Item", index)
            ?? TryCallComMethod(collection, "get_Item", index)
            ?? TryCallComMethod(collection, "Document", index)
            ?? TryCallComMethod(collection, "get_Document", index);
    }

    private static int TryGetComIntProperty(object target, string propertyName)
    {
        var value = GetComProperty(target, propertyName);
        return value is not null && int.TryParse(value.ToString(), out var count) ? count : 0;
    }

    private static string? GetDocumentPath(object document)
    {
        var pathName = GetComProperty(document, "PathName")?.ToString();
        if (!string.IsNullOrWhiteSpace(pathName)) return pathName;

        var fileName = GetComProperty(document, "FileName")?.ToString();
        if (!string.IsNullOrWhiteSpace(fileName)) return fileName;

        var path = GetComProperty(document, "Path")?.ToString();
        var name = GetComProperty(document, "Name")?.ToString();
        return !string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(name) ? Path.Combine(path, name) : null;
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;

        try
        {
            return Path.GetFullPath(path.Trim()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.Trim();
        }
    }

    private static object? TryCallComMethod(object target, string methodName, params object[] args)
    {
        try
        {
            return target.GetType().InvokeMember(
                methodName,
                BindingFlags.InvokeMethod,
                null,
                target,
                args.Length == 0 ? null : args);
        }
        catch
        {
            return null;
        }
    }

    private static object? GetRunningKompasApplication()
    {
        var progIds = new[]
        {
            "KOMPAS.Application.7",
            "Kompas.Application.7",
            "KOMPAS.Application",
            "Kompas.Application"
        };

        foreach (var progId in progIds)
        {
            try { return GetActiveComObject(progId); }
            catch { }
        }

        return null;
    }

    private static object GetActiveComObject(string progId)
    {
        var clsid = Type.GetTypeFromProgID(progId)?.GUID
            ?? throw new InvalidOperationException($"ProgID is not registered: {progId}");

        var rotResult = Ole32.GetRunningObjectTable(0, out var rot);
        if (rotResult < 0) Marshal.ThrowExceptionForHR(rotResult);

        var bindCtxResult = Ole32.CreateBindCtx(0, out var bindCtx);
        if (bindCtxResult < 0) Marshal.ThrowExceptionForHR(bindCtxResult);

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

    private static object? GetComProperty(object target, string propertyName)
    {
        try { return target.GetType().InvokeMember(propertyName, BindingFlags.GetProperty, null, target, null); }
        catch { return null; }
    }

    private static void SetComProperty(object target, string propertyName, object value)
    {
        try { target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, new[] { value }); }
        catch { }
    }

    private static string DescribeException(Exception ex)
    {
        var current = ex;
        while (current is TargetInvocationException && current.InnerException is not null) current = current.InnerException;
        return current is COMException comException
            ? $"{comException.Message} (HRESULT: 0x{comException.HResult:X8})"
            : current.Message;
    }

    internal static class Ole32
    {
        [DllImport("ole32.dll")]
        public static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);

        [DllImport("ole32.dll")]
        public static extern int CreateBindCtx(int reserved, out IBindCtx bindCtx);
    }

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
