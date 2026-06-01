namespace CadAssist.Kompas.Interop;

public sealed class ProjectContext
{
    public string ProjectName { get; set; } = "";
    public string CadSystem { get; set; } = "";
    public string ModelPath { get; set; } = "";
    public string ProjectDirectory { get; set; } = "";
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

public sealed class ProjectTask
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Assignee { get; set; } = "";
    public string LinkedCadObject { get; set; } = "";
    public string? ModelPath { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ProjectRequirement
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
}

public sealed class ActivityLogItem
{
    public DateTimeOffset At { get; set; }
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
}
