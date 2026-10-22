namespace TikzEdt.Modern.Core.Documents;

public enum LatexEngine
{
    PdfLatex,
    LuaLatex,
    XeLatex
}

public sealed record CompilerDiagnostic(
    string Message,
    int? Line = null,
    int? Column = null,
    DiagnosticSeverity Severity = DiagnosticSeverity.Error);

public enum DiagnosticSeverity
{
    Info,
    Warning,
    Error
}

public sealed record CompilationRequest(
    string Source,
    string WorkingDirectory,
    LatexEngine Engine,
    string? SourceFile = null);

public sealed record CompilationResult(
    bool Success,
    string? PdfPath,
    IReadOnlyList<CompilerDiagnostic> Diagnostics,
    string Log);

public interface ITikzCompiler
{
    Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken cancellationToken);
}

public interface ITikzParser
{
    ParseResult Parse(string source);
}

public sealed record ParseResult(
    bool Success,
    IReadOnlyList<CompilerDiagnostic> Diagnostics,
    object? SyntaxTree = null);
