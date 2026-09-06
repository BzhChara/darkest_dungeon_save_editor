namespace DarkestDungeonSaveEditor.Core;

public enum DiagnosticLogLevel
{
    Trace,
    Information,
    Warning,
    Error
}

public sealed record DiagnosticLogEntry(DiagnosticLogLevel Level, string Message);
