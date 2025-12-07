using System.ComponentModel.DataAnnotations;

namespace Dtce.Infrastructure.Local;

public class FileQueueOptions
{
    private string _rootPath = Path.Combine(AppContext.BaseDirectory, "local_queues");

    [Required]
    public string RootPath
    {
        get => _rootPath;
        set => _rootPath = string.IsNullOrWhiteSpace(value)
            ? Path.Combine(AppContext.BaseDirectory, "local_queues")
            : Path.GetFullPath(value);
    }

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromMilliseconds(250);
}


