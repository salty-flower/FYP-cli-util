using System.Collections;
using System.Diagnostics.CodeAnalysis;
using AspectInjector.Broker;
using Serilog;

// ReSharper disable UnusedMember.Global

namespace DataCollection.Common;

[Aspect(Scope.Global)]
[Injection(typeof(LogEntryAndExit))]
[AttributeUsage(AttributeTargets.Method)]
[SuppressMessage("Performance", "CA1822:Mark members as static")]
public class LogEntryAndExit : Attribute
{
    [Advice(Kind.Before, Targets = Target.Method)]
    public void LogEnter(
        [Argument(Source.Instance)] object instance,
        [Argument(Source.Name)] string name
    ) => Log.Information("Entering {Name}.{S}", instance.GetType().Name, name);

    [Advice(Kind.After, Targets = Target.Method)]
    public void LogExit(
        [Argument(Source.Instance)] object instance,
        [Argument(Source.Name)] string name,
        [Argument(Source.ReturnValue)] object? returnValue
    )
    {
        switch (returnValue)
        {
            case ICollection collection:
                Log.Information(
                    "Leaving {Name}.{S} with count: {CollectionCount}",
                    instance.GetType().Name,
                    name,
                    collection.Count
                );
                break;
            case IEnumerable enumerable:
                Log.Information(
                    "Leaving {Name}.{S} with count: {Count}",
                    instance.GetType().Name,
                    name,
                    enumerable.Cast<object?>().Count()
                );
                break;
            default:
                Log.Information("Leaving {Name}.{S}", instance.GetType().Name, name);
                break;
        }
    }
}
