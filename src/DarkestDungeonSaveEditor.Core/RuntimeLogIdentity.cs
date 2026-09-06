using System.Reflection;
using System.Runtime.InteropServices;

namespace DarkestDungeonSaveEditor.Core;

public static class RuntimeLogIdentity
{
    public static string Describe(Assembly assembly) =>
        $"程序启动：版本={assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "未知"}；" +
        $"构建标识(MVID)={assembly.ManifestModule.ModuleVersionId:D}；" +
        $"核心构建标识(MVID)={typeof(RuntimeLogIdentity).Assembly.ManifestModule.ModuleVersionId:D}；" +
        $"程序={Environment.ProcessPath ?? "未知"}；程序集={assembly.Location}；" +
        $"运行时={RuntimeInformation.FrameworkDescription}；架构={RuntimeInformation.ProcessArchitecture}；系统={RuntimeInformation.OSDescription}";
}
