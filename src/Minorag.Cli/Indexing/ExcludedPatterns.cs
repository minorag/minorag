namespace Minorag.Cli.Indexing;

public class ExcludedPatterns
{
    public static HashSet<string> ExcludedFiles => new(StringComparer.OrdinalIgnoreCase)
    {
        // OS / IDE noise
        ".ds_store",
        "thumbs.db",

        // Lockfiles – huge & rarely useful for RAG
        "package-lock.json",
        "yarn.lock",
        "pnpm-lock.yaml",
        "pnpm-lock.yml",
        "poetry.lock",
        "pipfile.lock",
        "composer.lock",
        "cargo.lock",

        // Local config / secrets (very important)
        ".env",
        ".env.local",
        ".env.development",
        ".env.production",
        "appsettings.local.json",
        "appsettings.Development.local.json",
        // minorag ignore
         ".minoragignore"
    };

    public static HashSet<string> ExcludedDirs => new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "node_modules", ".git", ".vs", ".idea", ".venv",
        "__pycache__", ".mypy_cache", ".pytest_cache",
        ".gradle", "build", "out", "target",
        "dist", "coverage", ".next", ".angular", ".nuxt", "storybook-static",
        "vendor", "logs", "tmp", "temp", ".cache",
        "cmake-build-debug", "cmake-build-release", "CMakeFiles"
    };

    public static HashSet<string> BinaryExtensions => new(StringComparer.OrdinalIgnoreCase)
    {
        "png", "ico", "jar", "woff", "woff2", "dll", "exe", "pdb", "snap",
        "gif", "jpg", "jpeg", "so",

        // Images
        "bmp", "tiff", "webp", "svgz",

        // Design
        "ai", "eps", "psd", "sketch",

        // Documents
        "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx",

        // Audio + video
        "mp3", "wav", "ogg", "mp4", "mov", "mkv", "avi",

        // Archives
        "zip", "rar", "7z", "tar", "gz", "bz2",

        // Binary artifacts
        "class", "wasm", "sqlite", "db", "bak",

        // Fonts
        "ttf", "otf", "eot", "ttc",

        // Misc
        "lock", "bin"
    };
}
