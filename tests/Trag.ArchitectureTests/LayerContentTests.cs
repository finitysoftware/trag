using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Input;

namespace Trag.ArchitectureTests;

// What each layer may hold besides its references. Domain is values and rules: nothing in it changes
// on its own, waits, or asks the machine for the time, its folders or its processes. Application reads the clock only
// through a TimeProvider and the machine only through its ports, and offers no command: commands are the view models'.
public sealed partial class LayerContentTests
{
    private static readonly Assembly Domain = typeof(Trag.Domain.Paths.GitPath).Assembly;
    private static readonly Assembly Application = typeof(Trag.Application.Live.Repository).Assembly;
    private const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    // A local date is about here: the time text is the one place Domain reads the local time zone, at the moment a screen
    // shows it. The time itself it is given, by the models' TimeProvider.
    private static readonly string[] DomainTimeTexts =
    [
        "Trag.Domain/Presentation/Text/RelativeTime.cs",
    ];

    [Fact]
    public void DomainTypesRaiseNoEventSetNoPropertyAndWaitForNothing()
    {
        var found = Domain.GetTypes().Where(type => !type.IsDefined(typeof(CompilerGeneratedAttribute), false))
            .SelectMany(type => type.GetEvents(Declared).Select(item => type.FullName + " raises " + item.Name)
                .Concat(type.GetProperties(Declared).Where(property => property.SetMethod is { } set && !IsInit(set)).Select(item => type.FullName + " sets " + item.Name))
                .Concat(type.GetMethods(Declared).Where(method => IsAwaitable(method.ReturnType)).Select(item => type.FullName + " waits in " + item.Name))
                .Concat(type.GetFields(Declared).Where(field => field.FieldType == typeof(SemaphoreSlim) || field.FieldType == typeof(Lock)).Select(item => type.FullName + " locks with " + item.Name)))
            .ToArray();
        Assert.Empty(found);
    }

    [Fact]
    public void DomainAsksTheMachineForNothingButTheTimeItsTimeTextsShow()
    {
        var reads = Reads("Trag.Domain", MachineRead()).Concat(Reads("Trag.Domain", ClockRead()).Where(read => !DomainTimeTexts.Any(read.StartsWith)));
        Assert.Empty(reads);
        // The list names only files that still read the time.
        Assert.All(DomainTimeTexts, file => Assert.Contains(Reads("Trag.Domain", ClockRead()), read => read.StartsWith(file, StringComparison.Ordinal)));
    }

    [Fact]
    public void ApplicationReadsTheClockOnlyThroughATimeProviderAndTheMachineOnlyThroughItsPorts() =>
        Assert.Empty(Reads("Trag.Application", ClockRead()).Concat(Reads("Trag.Application", MachineRead())));

    [Fact]
    public void ApplicationOffersNoCommand()
    {
        var commands = Application.GetTypes()
            .SelectMany(type => type.GetProperties(Declared).Where(property => typeof(ICommand).IsAssignableFrom(property.PropertyType))
                .Select(property => type.FullName + "." + property.Name));
        Assert.Empty(commands);
    }

    private static bool IsInit(MethodInfo setter) => setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit));

    private static bool IsAwaitable(Type type) => typeof(Task).IsAssignableFrom(type) || type == typeof(ValueTask) || type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ValueTask<>);

    private static IEnumerable<string> Reads(string project, Regex pattern) => RepositoryRoot.ProductionSources(project)
        .SelectMany(file => pattern.Matches(CSharpSource.Strip(file.Text)).Select(match => file.Display + " " + match.Value));

    // The clock and the local time zone, read directly.
    [GeneratedRegex(@"\bDateTime(?:Offset)?\.(?:Utc)?Now\b|\bTimeZoneInfo\.Local\b|\.ToLocalTime\(")]
    private static partial Regex ClockRead();

    // The machine: its environment, its processes, its files and folders.
    [GeneratedRegex(@"(?<![.\w])(?:Environment|Process)\.\w+|(?<![.\w])(?:File|Directory)\.(?:Exists|Read|Write|Open|Delete|Move|Copy|Create|Append|Enumerate|Get)\w*\(")]
    private static partial Regex MachineRead();
}
