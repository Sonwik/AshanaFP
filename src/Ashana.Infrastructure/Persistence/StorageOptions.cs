namespace Ashana.Infrastructure.Persistence;

public enum StorageProvider
{
    /// <summary>Data lives only in memory (tests, quick demos). Lost on restart.</summary>
    Memory,

    /// <summary>In memory, saved to a JSON file after every change. Zero setup, survives restarts.</summary>
    File,
}

/// <summary>Configuration section <c>Storage</c>.</summary>
public sealed class StorageOptions
{
    public const string SectionName = "Storage";

    public StorageProvider Provider { get; set; } = StorageProvider.File;

    /// <summary>Data file for the File provider; relative paths start at the app folder.</summary>
    public string FilePath { get; set; } = "App_Data/ashana-data.json";
}
