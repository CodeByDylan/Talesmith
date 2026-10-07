using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

namespace Talesmith.Scripting.Compiler.Analyzers;

/// <summary>The ids of the diagnostics the script analyzers report.</summary>
public static class ScriptDiagnosticIds
{
    /// <summary>A call that blocks the game thread, such as <c>Thread.Sleep</c>, <c>Task.Wait</c>, <c>.Result</c> or file access, in an update method.</summary>
    public const string BlockingCall = "TS1001";

    /// <summary>An allocation of a class, array or collection, or a LINQ call, in an update method.</summary>
    public const string Allocation = "TS1002";

    /// <summary>A string built by concatenation, interpolation or formatting in an update method.</summary>
    public const string StringBuilding = "TS1003";

    /// <summary>An <c>async void</c> method or lambda, whose failures cannot be attributed or observed.</summary>
    public const string AsyncVoid = "TS1004";
}

/// <summary>Finds common game-loop pitfalls in scripts: blocking calls, allocations and string building in update methods, and <c>async void</c>.</summary>
/// <remarks>
/// Update methods are overrides of <c>Script.FixedUpdate</c>, <c>Update</c> and <c>LateUpdate</c> and implementations of
/// <c>ISystem.Update</c>, including lambdas inside them. Code inside <c>throw</c> statements is ignored, since it does not run every frame.
/// </remarks>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
internal sealed class GameLoopAnalyzer : DiagnosticAnalyzer
{
    private const string Category = "Talesmith.GameLoop";

    private static readonly DiagnosticDescriptor BlockingCall = new(ScriptDiagnosticIds.BlockingCall, "Blocking call in an update method",
        "'{0}' blocks the game thread in {1}; await it in an async routine started with Run(...) or move the work off the game loop", Category,
        DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Update methods run every frame on the game thread; blocking there freezes the game.");

    private static readonly DiagnosticDescriptor Allocation = new(ScriptDiagnosticIds.Allocation, "Allocation in an update method",
        "{0} allocates every time {1} runs; create it once, such as in OnCreate, and reuse it", Category, DiagnosticSeverity.Warning, isEnabledByDefault: true,
        description: "Allocating every frame makes the garbage collector pause the game.");

    private static readonly DiagnosticDescriptor StringBuilding = new(ScriptDiagnosticIds.StringBuilding, "String building in an update method",
        "{0} builds a new string every time {1} runs; build it only when the value changes", Category, DiagnosticSeverity.Info, isEnabledByDefault: true);

    private static readonly DiagnosticDescriptor AsyncVoid = new(ScriptDiagnosticIds.AsyncVoid, "async void",
        "'{0}' is async void, so its failures cannot be observed; return Task and start it with Run(...)", Category, DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    private static readonly ImmutableHashSet<string> UpdateMethods = ["FixedUpdate", "Update", "LateUpdate"];

    private static readonly ImmutableHashSet<string> FileTypes =
        ["System.IO.File", "System.IO.Directory", "System.IO.FileStream", "System.IO.StreamReader", "System.IO.StreamWriter"];

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } = [BlockingCall, Allocation, StringBuilding, AsyncVoid];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterOperationAction(AnalyzeLambda, OperationKind.AnonymousFunction);
        context.RegisterOperationBlockStartAction(start =>
        {
            if (start.OwningSymbol is not IMethodSymbol method || !IsUpdateMethod(method))
                return;
            var name = $"{method.ContainingType.Name}.{method.Name}";
            start.RegisterOperationAction(c => AnalyzeInvocation(c, name), OperationKind.Invocation);
            start.RegisterOperationAction(c => AnalyzePropertyReference(c, name), OperationKind.PropertyReference);
            start.RegisterOperationAction(c => AnalyzeCreation(c, name), OperationKind.ObjectCreation, OperationKind.ArrayCreation,
                OperationKind.CollectionExpression);
            start.RegisterOperationAction(c => AnalyzeConcatenation(c, name), OperationKind.Binary, OperationKind.InterpolatedString);
        });
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        var method = (IMethodSymbol)context.Symbol;
        if (method is { IsAsync: true, ReturnsVoid: true, MethodKind: MethodKind.Ordinary or MethodKind.ExplicitInterfaceImplementation } && !method.IsImplicitlyDeclared)
            context.ReportDiagnostic(Diagnostic.Create(AsyncVoid, method.Locations[0], method.Name));
    }

    private static void AnalyzeLambda(OperationAnalysisContext context)
    {
        var lambda = (IAnonymousFunctionOperation)context.Operation;
        if (lambda.Symbol is { IsAsync: true, ReturnsVoid: true })
            context.ReportDiagnostic(Diagnostic.Create(AsyncVoid, lambda.Syntax.GetLocation(), "async lambda"));
    }

    private static void AnalyzeInvocation(OperationAnalysisContext context, string method)
    {
        var invocation = (IInvocationOperation)context.Operation;
        if (IsCold(invocation))
            return;
        var target = invocation.TargetMethod;
        var type = target.ContainingType?.OriginalDefinition.ToDisplayString() ?? "";
        var call = $"{target.ContainingType?.Name}.{target.Name}";
        if (type == "System.Threading.Thread" && target.Name == "Sleep"
            || type is "System.Threading.Tasks.Task" or "System.Threading.Tasks.Task<TResult>" && target.Name is "Wait" or "WaitAll" or "WaitAny"
            || type is "System.Runtime.CompilerServices.TaskAwaiter" or "System.Runtime.CompilerServices.TaskAwaiter<TResult>"
                or "System.Runtime.CompilerServices.ValueTaskAwaiter<TResult>" && target.Name == "GetResult"
            || FileTypes.Contains(type))
        {
            context.ReportDiagnostic(Diagnostic.Create(BlockingCall, invocation.Syntax.GetLocation(), call, method));
            return;
        }

        if (type == "System.Linq.Enumerable")
        {
            context.ReportDiagnostic(Diagnostic.Create(Allocation, invocation.Syntax.GetLocation(), $"The LINQ call {target.Name}", method));
            return;
        }

        if (type == "string" && target.Name is "Format" or "Concat" or "Join")
            context.ReportDiagnostic(Diagnostic.Create(StringBuilding, invocation.Syntax.GetLocation(), $"string.{target.Name}", method));
    }

    private static void AnalyzePropertyReference(OperationAnalysisContext context, string method)
    {
        var reference = (IPropertyReferenceOperation)context.Operation;
        var property = reference.Property;
        if (property.Name == "Result" && property.ContainingType?.OriginalDefinition.ToDisplayString() is "System.Threading.Tasks.Task<TResult>" or
                "System.Threading.Tasks.ValueTask<TResult>" && !IsCold(reference))
            context.ReportDiagnostic(Diagnostic.Create(BlockingCall, reference.Syntax.GetLocation(), "Task.Result", method));
    }

    private static void AnalyzeCreation(OperationAnalysisContext context, string method)
    {
        var operation = context.Operation;
        if (IsCold(operation) || operation.Type is not { IsReferenceType: true } type)
            return;
        if (operation is ICollectionExpressionOperation { Elements.Length: 0 } && type is IArrayTypeSymbol)
            return;
        var what = operation.Kind switch
        {
            OperationKind.ArrayCreation => $"The array new {type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            OperationKind.CollectionExpression => $"The collection expression creating {type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}",
            _ => $"new {type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat)}"
        };
        context.ReportDiagnostic(Diagnostic.Create(Allocation, operation.Syntax.GetLocation(), what, method));
    }

    private static void AnalyzeConcatenation(OperationAnalysisContext context, string method)
    {
        var operation = context.Operation;
        if (operation.ConstantValue.HasValue || operation.Type?.SpecialType != SpecialType.System_String || IsCold(operation))
            return;
        if (operation is IBinaryOperation { OperatorKind: not BinaryOperatorKind.Add })
            return;
        if (operation.Parent is IBinaryOperation { OperatorKind: BinaryOperatorKind.Add } parent && parent.Type?.SpecialType == SpecialType.System_String)
            return;
        var what = operation is IInterpolatedStringOperation ? "The interpolated string" : "The string concatenation";
        context.ReportDiagnostic(Diagnostic.Create(StringBuilding, operation.Syntax.GetLocation(), what, method));
    }

    /// <summary>Whether an operation only runs when something goes wrong, such as building an exception to throw.</summary>
    private static bool IsCold(IOperation operation)
    {
        for (var current = operation.Parent; current is not null; current = current.Parent)
        {
            if (current is IThrowOperation)
                return true;
        }

        return false;
    }

    private static bool IsUpdateMethod(IMethodSymbol method)
    {
        if (method.Name == "Update" && method.ContainingType.AllInterfaces.Any(i => i.ToDisplayString() == "Talesmith.Systems.ISystem"))
            return true;
        if (!method.IsOverride || !UpdateMethods.Contains(method.Name))
            return false;
        for (var overridden = method.OverriddenMethod; overridden is not null; overridden = overridden.OverriddenMethod)
        {
            if (overridden.ContainingType.ToDisplayString() == "Talesmith.Scripting.Script")
                return true;
        }

        return false;
    }
}
