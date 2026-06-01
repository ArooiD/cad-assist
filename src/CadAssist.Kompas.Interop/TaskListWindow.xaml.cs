using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace CadAssist.Kompas.Interop;

public partial class TaskListWindow : Window
{
    private readonly string _modelPath;
    private readonly string _contextPath;
    private readonly ObservableCollection<ProjectTask> _tasks = new();

    public TaskListWindow(string modelPath)
    {
        InitializeComponent();

        _modelPath = modelPath;
        _contextPath = modelPath + ".cadassist.json";
        TasksGrid.ItemsSource = _tasks;

        ModelPathText.Text = "Модель: " + _modelPath;
        ContextPathText.Text = "Контекст: " + _contextPath;

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
            var task = new ProjectTask
            {
                Id = NextTaskId(context.Tasks),
                Title = "Новая задача из интерфейса CAD Assist",
                Description = "Тестовая задача, добавленная из окна списка задач.",
                Status = "Новая",
                Assignee = Environment.UserName,
                LinkedCadObject = context.DocumentName ?? Path.GetFileName(_modelPath),
                CreatedAt = now
            };

            context.Tasks.Add(task);
            context.UpdatedAt = now;
            context.ActivityLog.Add(new ActivityLogItem
            {
                At = now,
                Actor = Environment.UserName,
                Action = $"Добавлена задача '{task.Title}' из окна CAD Assist"
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

    private void LoadTasks()
    {
        _tasks.Clear();

        if (!File.Exists(_contextPath))
        {
            SummaryText.Text = "Задач: 0";
            StatusText.Text = "Файл контекста не найден: " + _contextPath;
            return;
        }

        try
        {
            var context = LoadContextOrCreateEmpty();
            foreach (var task in context.Tasks.OrderBy(t => t.Id))
            {
                _tasks.Add(task);
            }

            SummaryText.Text = $"Задач: {_tasks.Count}";
            StatusText.Text = "Задачи загружены: " + DateTime.Now.ToString("HH:mm:ss");
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
            return new ProjectContext
            {
                ProjectName = "CAD Assist demo project",
                CadSystem = "KOMPAS-3D",
                ModelPath = _modelPath,
                DocumentName = Path.GetFileName(_modelPath),
                DocumentDirectory = Path.GetDirectoryName(_modelPath),
                CreatedAt = DateTimeOffset.Now,
                UpdatedAt = DateTimeOffset.Now
            };
        }

        var context = JsonSerializer.Deserialize<ProjectContext>(File.ReadAllText(_contextPath), JsonOptions());
        return context ?? new ProjectContext
        {
            ProjectName = "CAD Assist demo project",
            CadSystem = "KOMPAS-3D",
            ModelPath = _modelPath,
            DocumentName = Path.GetFileName(_modelPath),
            DocumentDirectory = Path.GetDirectoryName(_modelPath),
            CreatedAt = DateTimeOffset.Now,
            UpdatedAt = DateTimeOffset.Now
        };
    }

    private void SaveContext(ProjectContext context)
    {
        File.WriteAllText(_contextPath, JsonSerializer.Serialize(context, JsonOptions()));
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

    private static JsonSerializerOptions JsonOptions() => new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}
