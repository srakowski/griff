using Microsoft.Extensions.DependencyInjection;

namespace Griff.Editor.Core;

/// <summary>
/// How a service says in its constructor that it needs another later, not now: a <c>Lazy&lt;T&gt;</c> for one that can't be made
/// first (the panels are made before the commands that open them, so a panel that lists commands asks for them lazily) and a
/// <c>Func&lt;T&gt;</c> for a window or other thing made new each time.
/// </summary>
public static class ServiceFactories
{
    /// <summary>Lets any service be asked for as a <c>Lazy&lt;T&gt;</c>, made on first use.</summary>
    public static IServiceCollection AddLazyServices(this IServiceCollection services) =>
        services.AddTransient(typeof(Lazy<>), typeof(LazyService<>));

    /// <summary>Lets <typeparamref name="T"/> be asked for as a <c>Func&lt;T&gt;</c>, which makes (or gets) one each time it's called.</summary>
    public static IServiceCollection AddFactory<T>(this IServiceCollection services) where T : class =>
        services.AddTransient<Func<T>>(sp => () => sp.GetRequiredService<T>());

    private sealed class LazyService<T> : Lazy<T> where T : notnull
    {
        public LazyService(IServiceProvider services)
            : base(() => services.GetRequiredService<T>())
        {
        }
    }
}
