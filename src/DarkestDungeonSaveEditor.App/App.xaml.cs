using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using DarkestDungeonSaveEditor.Core;

namespace DarkestDungeonSaveEditor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CrashDiagnostics.SetStage("Application: startup");
        try
        {
            CrashDiagnostics.RecordStatus(RuntimeLogIdentity.Describe(typeof(App).Assembly) +
                $"；本次日志={CrashDiagnostics.LogFilePath}");
        }
        catch (Exception ex)
        {
            CrashDiagnostics.RecordException("Application: startup identity", ex);
        }
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        base.OnStartup(e);
    }

    private static void OnDispatcherUnhandledException(
        object sender,
        DispatcherUnhandledExceptionEventArgs e)
    {
        CrashDiagnostics.RecordException("DispatcherUnhandledException", e.Exception);
        // Keep the default fatal behavior. The concrete exception must be known
        // before deciding whether it is safe to continue using the WPF window.
    }

    private static void OnAppDomainUnhandledException(
        object sender,
        UnhandledExceptionEventArgs e)
    {
        var exception = e.ExceptionObject as Exception ??
            new InvalidOperationException($"Unhandled non-Exception object: {e.ExceptionObject}");
        CrashDiagnostics.RecordException(
            "AppDomain.UnhandledException",
            exception,
            $"IsTerminating={e.IsTerminating}");
    }

    private static void OnUnobservedTaskException(
        object? sender,
        UnobservedTaskExceptionEventArgs e)
    {
        CrashDiagnostics.RecordException("TaskScheduler.UnobservedTaskException", e.Exception);
    }
}

internal static class CrashDiagnostics
{
    private static readonly Lazy<SessionLogFile> SessionLog = new(() => new SessionLogFile(
        SaveEditorLocations.ResolveLogDirectory(AppContext.BaseDirectory), DateTimeOffset.Now, Environment.ProcessId));
    private static string _stage = "Application: not started";

    public static string LogFilePath => SessionLog.Value.FilePath;

    public static void SetStage(string stage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stage);
        Interlocked.Exchange(ref _stage, stage);
        WriteEntry("Stage", null, stage, DiagnosticLogLevel.Trace);
    }

    public static void RecordException(
        string source,
        Exception exception,
        string? details = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        ArgumentNullException.ThrowIfNull(exception);
        WriteEntry(source, exception, details, DiagnosticLogLevel.Error);
    }

    public static void RecordStatus(string message, DiagnosticLogLevel level = DiagnosticLogLevel.Information)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        WriteEntry("Status", null, message, level);
    }

    public static void RecordCatalogDiagnostics(CatalogDiagnosticBatch batch)
    {
        try
        {
            foreach (var entry in batch.Drain())
            {
                RecordStatus(entry.Message, entry.Level);
            }
        }
        catch (Exception ex)
        {
            RecordException("Catalog diagnostics: flush", ex);
        }
    }

    private static void WriteEntry(
        string source,
        Exception? exception,
        string? details,
        DiagnosticLogLevel level)
    {
        try
        {
            var builder = new StringBuilder()
                .AppendLine("---")
                .AppendLine($"Time: {DateTimeOffset.Now:O}")
                .AppendLine($"Source: {source}")
                .AppendLine($"Level: {level}")
                .AppendLine($"Stage: {Volatile.Read(ref _stage)}")
                .AppendLine($"ProcessId: {Environment.ProcessId}")
                .AppendLine($"ManagedThreadId: {Environment.CurrentManagedThreadId}")
                .AppendLine($"ApartmentState: {Thread.CurrentThread.GetApartmentState()}");
            if (!string.IsNullOrWhiteSpace(details))
            {
                builder.AppendLine($"Details: {details}");
            }

            if (exception is not null)
            {
                builder.AppendLine("Exception:").AppendLine(exception.ToString());
            }

            SessionLog.Value.Append(builder.ToString());
        }
        catch
        {
            // Diagnostics must never replace the original failure.
        }
    }
}
