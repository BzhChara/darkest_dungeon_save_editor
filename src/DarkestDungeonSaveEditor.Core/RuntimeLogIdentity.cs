using System.Reflection;
using System.Runtime.InteropServices;

namespace DarkestDungeonSaveEditor.Core;

public static class RuntimeLogIdentity
{
    public static string Describe(Assembly assembly) =>
        EditorText.Format("RuntimeLogIdentity_001", assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? EditorText.Get("BattleEncounterSelectionDialog_017")) +
        EditorText.Format("RuntimeLogIdentity_002", assembly.ManifestModule.ModuleVersionId) +
        EditorText.Format("RuntimeLogIdentity_003", typeof(RuntimeLogIdentity).Assembly.ManifestModule.ModuleVersionId) +
        EditorText.Format("RuntimeLogIdentity_004", Environment.ProcessPath ?? EditorText.Get("BattleEncounterSelectionDialog_017"), assembly.Location) +
        EditorText.Format("RuntimeLogIdentity_005", RuntimeInformation.FrameworkDescription, RuntimeInformation.ProcessArchitecture, RuntimeInformation.OSDescription);
}
