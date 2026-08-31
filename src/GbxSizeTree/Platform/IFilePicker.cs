namespace GbxSizeTree.Cli.Platform;

/// <summary>Selects a Trackmania <c>.Map.Gbx</c> file, or reports cancellation/unavailability.</summary>
public interface IFilePicker
{
    string? PickMapFile();
}
