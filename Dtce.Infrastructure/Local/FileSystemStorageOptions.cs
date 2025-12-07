using System.ComponentModel.DataAnnotations;

namespace Dtce.Infrastructure.Local;

public class FileSystemStorageOptions
{
    private string _rootPath = Path.Combine(AppContext.BaseDirectory, "local_storage");

    [Required]
    public string RootPath
    {
        get => _rootPath;
        set => _rootPath = string.IsNullOrWhiteSpace(value)
            ? Path.Combine(AppContext.BaseDirectory, "local_storage")
            : Path.GetFullPath(value);
    }
}


