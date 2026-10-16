using System.Reflection;
using Spectre.Console.Cli;

namespace Sfl.Cli.Hosting;

/// <summary>
/// A minimal container so command classes can take a <see cref="CliContext"/> in their constructor.
/// Spectre.Console.Cli registers several implementations of some services and resolves them as
/// <c>IEnumerable&lt;T&gt;</c>, so every service keeps a list of registrations (the last one wins
/// for a single resolve).
/// </summary>
internal sealed class TypeRegistrar : ITypeRegistrar
{
    private readonly CliContext _context;
    private readonly Dictionary<Type, List<Func<TypeResolver, object>>> _registrations = [];

    public TypeRegistrar(CliContext context)
    {
        _context = context;
    }

    public void Register(Type service, Type implementation) =>
        Add(service, resolver => resolver.Create(implementation));

    public void RegisterInstance(Type service, object implementation) =>
        Add(service, _ => implementation);

    public void RegisterLazy(Type service, Func<object> factory) =>
        Add(service, _ => factory());

    public ITypeResolver Build() => new TypeResolver(_context, _registrations);

    private void Add(Type service, Func<TypeResolver, object> factory)
    {
        if (!_registrations.TryGetValue(service, out List<Func<TypeResolver, object>>? factories))
        {
            factories = [];
            _registrations[service] = factories;
        }

        factories.Add(factory);
    }

    private sealed class TypeResolver : ITypeResolver
    {
        private readonly CliContext _context;
        private readonly Dictionary<Type, List<Func<TypeResolver, object>>> _registrations;

        public TypeResolver(CliContext context, Dictionary<Type, List<Func<TypeResolver, object>>> registrations)
        {
            _context = context;
            _registrations = registrations;
        }

        public object? Resolve(Type? type)
        {
            if (type is null)
            {
                return null;
            }

            if (type == typeof(CliContext))
            {
                return _context;
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                Type elementType = type.GetGenericArguments()[0];
                List<Func<TypeResolver, object>> factories =
                    _registrations.TryGetValue(elementType, out var found) ? found : [];
                Array items = Array.CreateInstance(elementType, factories.Count);
                for (int index = 0; index < factories.Count; index++)
                {
                    items.SetValue(factories[index](this), index);
                }

                return items;
            }

            return _registrations.TryGetValue(type, out var registered) && registered.Count > 0
                ? registered[^1](this)
                : null;
        }

        public object Create(Type implementation)
        {
            ConstructorInfo constructor = implementation
                .GetConstructors()
                .OrderByDescending(candidate => candidate.GetParameters().Length)
                .First();
            object?[] arguments = constructor.GetParameters()
                .Select(parameter => Resolve(parameter.ParameterType)
                    ?? throw new InvalidOperationException(
                        $"Cannot resolve {parameter.ParameterType.Name} for {implementation.Name}."))
                .ToArray();
            return constructor.Invoke(arguments);
        }
    }
}
