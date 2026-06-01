using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Windows;
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
        TasksGrid.MouseDoubleClick += TasksGrid_MouseDoubleClick;

        ModelPathText.Text = "Текущая модель: " + _modelPath;
        ContextPathText.Text = "Проект: " + _contextPath;

        EnsureContextExists();
        LoadTasks();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadTasks();
    }

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

    private void TasksGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (TasksGrid.SelectedItem is not ProjectTask task)
        {
            return;
        }

        try
        {
            var modelFileName = task.ModelPath;
            if (string.IsNullOrWhiteSpace(modelFileName))
            {
                modelFileName = task.LinkedCadObject;
            }

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

            OpenModelInKompas(fullPath);
            StatusText.Text = $"Открыта модель: {fullPath}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Ошибка открытия модели: " + DescribeException(ex);
        }
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
            foreach (var task in context.Tasks.OrderBy(t => t.Id))
            {
                NormalizeTaskModelReference(task, context);
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
        if (!File.Exists(_contextPath))
        {
            return CreateEmptyContext();
        }

        var json = File.ReadAllText(_contextPath);
        var context = JsonSerializer.Deserialize<ProjectContext>(json, JsonOptions()) ?? CreateEmptyContext();
        EnsureCollections(context);
        if (string.IsNullOrWhiteSpace(context.ProjectDirectory))
        {
            context.ProjectDirectory = _projectDirectory;
        }
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
        foreach (var task in context.Tasks)
        {
            NormalizeTaskModelReference(task, context);
        }

        var directory = Path.GetDirectoryName(_contextPath);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        File.WriteAllText(_contextPath, JsonSerializer.Serialize(context, JsonOptions()));
    }

    private void NormalizeTaskModelReference(ProjectTask task, ProjectContext context)
    {
        if (string.IsNullOrWhiteSpace(task.ModelPath))
        {
            task.ModelPath = !string.IsNullOrWhiteSpace(task.LinkedCadObject)
                ? task.LinkedCadObject
                : Path.GetFileName(_modelPath);
        }

        if (Path.IsPathRooted(task.ModelPath))
        {
            task.ModelPath = Path.GetFileName(task.ModelPath);
        }

        if (string.IsNullOrWhiteSpace(task.LinkedCadObject))
        {
            task.LinkedCadObject = task.ModelPath;
        }
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

    private static void OpenModelInKompas(string modelPath)
    {
        var app = CreateKompasApplication();
        SetComProperty(app, "Visible", true);
        SetComProperty(app, "HideMessage", 1);

        var documents = GetComProperty(app, "Documents")
            ?? throw new InvalidOperationException("Не удалось получить объект Documents у КОМПАС.");

        documents.GetType().InvokeMember(
            "Open",
            BindingFlags.InvokeMethod,
            null,
            documents,
            new object[] { modelPath, true, false });
    }

    private static object CreateKompasApplication()
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
            var type = Type.GetTypeFromProgID(progId);
            if (type is null) continue;

            try
            {
                return Activator.CreateInstance(type)
                    ?? throw new InvalidOperationException("COM object is null: " + progId);
            }
            catch
            {
                // Try next ProgID.
            }
        }

        throw new InvalidOperationException("Не удалось создать объект КОМПАС.Application через COM.");
    }

    private static object? GetComProperty(object target, string propertyName)
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

    private static void SetComProperty(object target, string propertyName, object value)
    {
        try
        {
            target.GetType().InvokeMember(propertyName, BindingFlags.SetProperty, null, target, new[] { value });
        }
        catch
        {
            // Optional COM properties differ between KOMPAS versions.
        }
    }

    private static string DescribeException(Exception ex)
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

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}
