using System.Collections;
using System.Reflection;

namespace Griff.Editor.Tests;

// A rule with no exceptions, in every assembly of Griff's and the editor's: a dictionary or set kept in a static field that
// holds a type (or one of its members) holds it strongly, so a script compiled again would leave its old assembly loaded
// for good. Such caches are ConditionalWeakTables (a component's ComponentModel is one).
public class WeakTypeCacheTests
{
    [Fact]
    public void NoStaticCache_HoldsATypeStrongly()
    {
        var strong = GriffAssemblies().SelectMany(Types).SelectMany(type =>
                type.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsLiteral && IsCache(f.FieldType) && f.FieldType.GetGenericArguments().Any(HoldsATypesCode))
                    .Select(f => $"{type.FullName}.{f.Name}"))
            .ToList();

        Assert.True(strong.Count == 0, "Static caches holding types strongly (make them ConditionalWeakTables): " + string.Join(", ", strong));
    }

    [Fact]
    public void TheRule_FindsAStrongCache()
    {
        Assert.True(IsCache(typeof(System.Collections.Concurrent.ConcurrentDictionary<Type, bool>)));
        Assert.True(IsCache(typeof(Dictionary<(Type, string), PropertyInfo?>)) && HoldsATypesCode(typeof((Type, string))));
        Assert.True(IsCache(typeof(HashSet<Type>)));
        Assert.False(IsCache(typeof(System.Runtime.CompilerServices.ConditionalWeakTable<Type, object>)));
        Assert.Contains(GriffAssemblies(), a => a.GetName().Name == "Griff.Scripting");
    }

    // The editor and every Griff assembly it loads, followed through their references.
    private static List<Assembly> GriffAssemblies()
    {
        var found = new List<Assembly>();
        var next = new Queue<Assembly>([typeof(App).Assembly]);
        while (next.TryDequeue(out var assembly))
        {
            if (found.Contains(assembly))
                continue;
            found.Add(assembly);
            foreach (var name in assembly.GetReferencedAssemblies().Where(n => n.Name!.StartsWith("Griff", StringComparison.Ordinal)))
                next.Enqueue(Assembly.Load(name));
        }

        return found;
    }

    private static IEnumerable<Type> Types(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    // A dictionary or set that can be added to: what a cache is.
    private static bool IsCache(Type type) =>
        type.IsGenericType && (Implements(type, typeof(IDictionary<,>)) || Implements(type, typeof(ISet<>)) || typeof(IDictionary).IsAssignableFrom(type));

    private static bool Implements(Type type, Type generic) =>
        (type.IsInterface && type.GetGenericTypeDefinition() == generic) ||
        type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == generic);

    // A type, a member of one, or something made of them (a tuple, an array, a list of them).
    private static bool HoldsATypesCode(Type type) =>
        typeof(Type).IsAssignableFrom(type) || typeof(MemberInfo).IsAssignableFrom(type) ||
        (type.IsArray && HoldsATypesCode(type.GetElementType()!)) ||
        (type.IsGenericType && type.GetGenericArguments().Any(HoldsATypesCode));
}
