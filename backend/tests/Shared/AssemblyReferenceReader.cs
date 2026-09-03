using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace OmniAI.Testing;

/// <summary>
/// Reads the assembly references recorded in a compiled assembly's metadata.
/// </summary>
/// <remarks>
/// Metadata is read directly from the file rather than through
/// <see cref="System.Reflection.Assembly"/>, so no code is loaded or executed.
/// That keeps these architecture guards side-effect free and independent of any
/// host code-integrity policy that governs loading locally built binaries.
/// This file is shared by the test projects via a linked compile item.
/// </remarks>
internal static class AssemblyReferenceReader
{
    /// <summary>
    /// Returns the names of the assemblies referenced by <paramref name="assemblyFileName"/>,
    /// which is resolved relative to the current test output directory.
    /// </summary>
    public static IReadOnlyList<string> ReferencedAssemblyNames(string assemblyFileName)
    {
        string path = Path.Combine(AppContext.BaseDirectory, assemblyFileName);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"Expected '{assemblyFileName}' in the test output directory. " +
                "The project reference under test is missing or was not copied.",
                path);
        }

        using FileStream stream = File.OpenRead(path);
        using PEReader peReader = new(stream);

        MetadataReader metadata = peReader.GetMetadataReader();

        return
        [
            .. metadata.AssemblyReferences
                .Select(handle => metadata.GetString(metadata.GetAssemblyReference(handle).Name))
        ];
    }
}
