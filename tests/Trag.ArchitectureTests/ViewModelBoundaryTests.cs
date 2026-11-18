using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Trag.Application.Live;
using Trag.Application.Status;
using Trag.Application.Sync;
using Trag.Domain.Writes;
using Trag.Infrastructure.Git.Read;
using Trag.ViewModels.Shell;

namespace Trag.ArchitectureTests;

// The view models are the bridge between the views and the models: they use no port but the view's thread and the folder
// picker a screen asks, build no write, and take another view model only where one screen is composed of two. What a
// repository or the application has and does belongs to the models (Domain, Application), what goes out of the process to
// the services behind the Application's ports.
public sealed class ViewModelBoundaryTests
{
    private static readonly Assembly ViewModels = typeof(ShellViewModel).Assembly;
    private static readonly Assembly[] ProductionAssemblies =
        [typeof(GitWriteRequest).Assembly, typeof(Repository).Assembly, typeof(CliFileDiffReader).Assembly, ViewModels, typeof(Trag.Desktop.Program).Assembly];
    private const string Ports = "Trag.Application.Ports";
    // The view's thread is the view's; every other port is a model's.
    private const string ViewThread = Ports + ".Threading";
    // The port namespaces a view model has no using for at all.
    private static readonly string[] ModelPortNamespaces = ["Accounts", "Catalog", "Git", "Storage", "Watching", "Workspace"];
    private const BindingFlags Members = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    // The IL instructions by their value, to walk a method body.
    private static readonly Dictionary<short, OpCode> Instructions = typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(field => (OpCode)field.GetValue(null)!).GroupBy(code => code.Value).ToDictionary(group => group.Key, group => group.First());

    // The screens one view model composes of others; everything else reaches its data through a model, or the selection
    // of the file lists through the StatusSelection the file status keeps.
    private static readonly (string Owner, string Part)[] ScreenCompositions =
    [
        ("MergeDialogViewModel", "MergeBranchesViewModel"), // one dialog with a merge side and a rebase side
        ("MergeDialogViewModel", "RebaseBranchViewModel"),
        ("CloneViewModel", "LocalRepositoriesViewModel"),  // the clone dialog lands its repository in the local list
    ];

    // Constructors above five dependencies that were there before this rule; the list only shrinks. The engine takes one
    // optional port per Git reader, and the sync operations their Git steps.
    private static readonly string[] WideConstructors =
    [
        "Trag.Application.Workspace.RepositoryWorkspace",
        "Trag.Infrastructure.Git.Sync.PublishOperation",
    ];

    [Fact]
    public void ViewModelsUseNoPortButTheViewThreadAndTheFolderPicker()
    {
        var bySource = RepositoryRoot.ProductionSources("Trag.ViewModels")
            .Where(file => ModelPortNamespaces.Any(port => CSharpSource.Strip(file.Text).Contains(Ports + "." + port, StringComparison.Ordinal)))
            .Select(file => file.Display);
        var byMember = ViewModels.GetTypes()
            .SelectMany(type => MemberTypes(type).Where(IsModelPort).Select(port => type.FullName + " uses " + port.FullName));
        Assert.Empty(bySource.Concat(byMember).Distinct());
    }

    // Whatever the source looks like (a target-typed new, a lambda, an async method, a with), the compiled view models call
    // no write request's constructor and copy none. The models' own writes show that the reader sees each form.
    [Fact]
    public void ViewModelsBuildNoWriteRequest()
    {
        Assert.Contains(nameof(StageRequest), RequestsBuiltBy(typeof(StatusModel)));
        Assert.Contains(nameof(PublishRequest), RequestsBuiltBy(typeof(RemotesModel)));
        var violations = ViewModels.GetTypes().Where(type => !type.IsNested)
            .SelectMany(type => RequestsBuiltBy(type).Select(request => type.FullName + " builds " + request))
            .ToArray();
        Assert.Empty(violations);
    }

    // Whether a result still belongs to the session is the models' question, answered by their requests and the writer's
    // WriteBelongsToSession: a view model neither retires a repository nor asks whether it is retired.
    [Fact]
    public void ViewModelsAskNoRetirement()
    {
        var violations = RepositoryRoot.ProductionSources("Trag.ViewModels").Where(file => Regex.IsMatch(CSharpSource.Strip(file.Text), @"\b(Retire|IsRetired)\b"))
            .Select(file => file.Display + " asks about the repository's retirement");
        Assert.Empty(violations);
    }

    // The status bar says what the models did; a view model shows it and writes nothing to it.
    [Fact]
    public void ViewModelsWriteNothingToTheStatusBar()
    {
        var writes = RepositoryRoot.ProductionSources("Trag.ViewModels")
            .SelectMany(file => Regex.Matches(CSharpSource.Strip(file.Text), @"\b[Ss]tatusBar\.(\w+)\s*=(?!=)")
                .Select(match => file.Display + " writes StatusBar." + match.Groups[1].Value))
            .ToArray();
        Assert.Empty(writes);
    }

    // What a screen says, the icon it shows and the address it opens come from Domain's Presentation.Text, so a view
    // model's source holds no text of its own: its only string is the empty one a field starts with or is cleared to.
    // The scanner sees every form a literal takes, and the texts where they live.
    [Fact]
    public void ViewModelsHoldNoText()
    {
        Assert.Equal(["\"a\"", "$\"b{c}\"", "@\"d\"", "\"\"\"e\"\"\""],
            CSharpSource.StringLiterals("x = \"a\" + $\"b{c}\" + @\"d\" + \"\"\"e\"\"\"; // \"f\"\n/* \"g\" */"));
        Assert.Contains(RepositoryRoot.ProductionSources("Trag.Domain"), file => file.RelativePath.EndsWith("SessionText.cs", StringComparison.Ordinal)
            && CSharpSource.StringLiterals(file.Text).Count > 5);
        var texts = RepositoryRoot.ProductionSources("Trag.ViewModels")
            .SelectMany(file => CSharpSource.StringLiterals(file.Text).Where(literal => literal != "\"\"").Select(literal => file.Display + " says " + literal))
            .ToArray();
        Assert.Empty(texts);
    }

    [Fact]
    public void AViewModelTakesAnotherOnlyToComposeOneScreen()
    {
        var links = ViewModels.GetTypes()
            .Where(IsViewModel)
            .SelectMany(type => type.GetConstructors(Members).SelectMany(constructor => constructor.GetParameters())
                .Where(parameter => IsViewModel(parameter.ParameterType))
                .Select(parameter => (Owner: type.Name, Part: parameter.ParameterType.Name)))
            .Distinct().ToArray();
        Assert.Empty(links.Except(ScreenCompositions).Select(link => link.Owner + " takes " + link.Part));
        // The list names only compositions that still exist, so a link that goes also leaves the list.
        Assert.Empty(ScreenCompositions.Except(links).Select(link => "no longer composed: " + link.Owner + " → " + link.Part));
    }

    [Fact]
    public void ConstructorsTakeAtMostFiveDependencies()
    {
        var wide = ProductionAssemblies.SelectMany(assembly => assembly.GetTypes())
            .Where(type => type.IsClass && !IsRecord(type) && !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .Where(type => type.GetConstructors(Members).Any(constructor => constructor.GetParameters().Length > 5))
            .Select(type => type.FullName!).Order(StringComparer.Ordinal).ToArray();
        Assert.Empty(wide.Except(WideConstructors));
        Assert.Empty(WideConstructors.Except(wide).Select(name => "no longer wide: " + name));
    }

    private static bool IsViewModel(Type type) => type.Assembly == ViewModels && type.Name.EndsWith("ViewModel", StringComparison.Ordinal);

    private static bool IsRecord(Type type) => type.GetProperty("EqualityContract", BindingFlags.Instance | BindingFlags.NonPublic) is not null;

    private static bool IsModelPort(Type type) =>
        type.Namespace is { } name && name.StartsWith(Ports + ".", StringComparison.Ordinal) && name != ViewThread;

    // The types a view model holds or is given: its fields, properties, constructor and method parameters, and results.
    private static IEnumerable<Type> MemberTypes(Type type) =>
        type.GetFields(Members).Select(field => field.FieldType)
            .Concat(type.GetProperties(Members).Select(property => property.PropertyType))
            .Concat(type.GetConstructors(Members).SelectMany(constructor => constructor.GetParameters()).Select(parameter => parameter.ParameterType))
            .Concat(type.GetMethods(Members).SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType)))
            .SelectMany(Unwrap);

    // The write requests a type builds in its methods and in the lambdas and state machines the compiler nests in it: a
    // call to a request's constructor, or to the clone a with makes.
    private static IEnumerable<string> RequestsBuiltBy(Type type) =>
        WithNested(type).SelectMany(nested => nested.GetMethods(Members).Cast<MethodBase>().Concat(nested.GetConstructors(Members)))
            .SelectMany(Calls)
            .Where(called => called.DeclaringType is { } request && typeof(GitWriteRequest).IsAssignableFrom(request)
                && (called is ConstructorInfo || called.Name == "<Clone>$"))
            .Select(called => called.DeclaringType!.Name).Distinct();

    private static IEnumerable<Type> WithNested(Type type) => type.GetNestedTypes(Members).SelectMany(WithNested).Prepend(type);

    // The methods and constructors a method body names as the operand of an instruction (call, callvirt, newobj, ldftn).
    private static IEnumerable<MethodBase> Calls(MethodBase method)
    {
        if (method.GetMethodBody()?.GetILAsByteArray() is not { } il) yield break;
        var typeArguments = method.DeclaringType is { IsGenericType: true } generic ? generic.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } genericMethod ? genericMethod.GetGenericArguments() : null;
        for (var at = 0; at < il.Length;)
        {
            var code = Instructions[il[at] == 0xFE ? unchecked((short)(0xFE00 | il[at + 1])) : il[at]];
            at += code.Size;
            if (code.OperandType == OperandType.InlineMethod && method.Module.ResolveMethod(BitConverter.ToInt32(il, at), typeArguments, methodArguments) is { } called)
                yield return called;
            at += OperandSize(code.OperandType, il, at);
        }
    }

    private static int OperandSize(OperandType operand, byte[] il, int at) => operand switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, at),
        _ => 4
    };

    // A port may hide inside a generic: Func<IBranchWorkspace>, Task<IHistoryWorkspace?>…
    private static IEnumerable<Type> Unwrap(Type type) =>
        type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Prepend(type) : type.HasElementType ? Unwrap(type.GetElementType()!) : [type];
}
