namespace Macaron.DependencyInjection;

public abstract class RegistrationSource
{
    internal abstract object Create(IDependencyResolver resolver, ref CleanupCollector cleanupCollector);

    public sealed class Instance<T> : RegistrationSource where T : notnull
    {
        public T Value { get; }

        public Instance(T instance)
        {
            if (instance is null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            Value = instance;
        }

        internal override object Create(IDependencyResolver resolver, ref CleanupCollector cleanupCollector)
        {
            return Value;
        }
    }

    public sealed class Factory<T>(Func<T> factory) : RegistrationSource where T : notnull
    {
        public Func<T> Value { get; } = factory;

        internal override object Create(IDependencyResolver resolver, ref CleanupCollector cleanupCollector)
        {
            return Value();
        }
    }

    public sealed class ContextFactory<T>(DependencyFactory<T> factory) : RegistrationSource where T : notnull
    {
        public DependencyFactory<T> Value { get; } = factory;

        internal override object Create(IDependencyResolver resolver, ref CleanupCollector cleanupCollector)
        {
            return Value(resolver, ref cleanupCollector);
        }
    }

    private RegistrationSource() { }
}
