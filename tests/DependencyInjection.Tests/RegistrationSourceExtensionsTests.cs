namespace Macaron.DependencyInjection.Tests;

public class RegistrationSourceExtensionsTests
{
    private sealed class RecordingRegistrar : IDependencyRegistrar
    {
        public RegistrationSource Source { get; private set; } = null!;
        public Lifetime Lifetime { get; private set; }
        public object? Tag { get; private set; }
        public Delegate? Cleanup { get; private set; }

        public void RegisterTransient<T>(
            RegistrationSource.Factory<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Transient, tag, cleanup);
        }

        public void RegisterTransient<T>(
            RegistrationSource.ContextFactory<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Transient, tag, cleanup);
        }

        public void RegisterScoped<T>(
            RegistrationSource.Factory<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Scoped, tag, cleanup);
        }

        public void RegisterScoped<T>(
            RegistrationSource.ContextFactory<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Scoped, tag, cleanup);
        }

        public void RegisterSingleton<T>(
            RegistrationSource.Instance<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Singleton, tag, cleanup);
        }

        public void RegisterSingleton<T>(
            RegistrationSource.Factory<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Singleton, tag, cleanup);
        }

        public void RegisterSingleton<T>(
            RegistrationSource.ContextFactory<T> source,
            object? tag = null,
            Action<T>? cleanup = null
        ) where T : notnull
        {
            Capture(source, Lifetime.Singleton, tag, cleanup);
        }

        private void Capture<T>(RegistrationSource source, Lifetime lifetime, object? tag, Action<T>? cleanup)
        {
            Source = source;
            Lifetime = lifetime;
            Tag = tag;
            Cleanup = cleanup;
        }
    }

    private static IEnumerable<TestCaseData> SimpleFactoryRegistrations()
    {
        yield return new TestCaseData(
            (Action<IDependencyRegistrar, Func<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.RegisterTransient(factory: factory, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Transient
        ).SetName("SimpleFactoryRegistrations_Transient_LifetimeMethod");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, Func<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.Register(factory: factory, lifetime: Lifetime.Transient, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Transient
        ).SetName("SimpleFactoryRegistrations_Transient_LifetimeArgument");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, Func<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.RegisterScoped(factory: factory, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Scoped
        ).SetName("SimpleFactoryRegistrations_Scoped_LifetimeMethod");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, Func<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.Register(factory: factory, lifetime: Lifetime.Scoped, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Scoped
        ).SetName("SimpleFactoryRegistrations_Scoped_LifetimeArgument");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, Func<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.RegisterSingleton(factory: factory, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Singleton
        ).SetName("SimpleFactoryRegistrations_Singleton_LifetimeMethod");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, Func<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.Register(factory: factory, lifetime: Lifetime.Singleton, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Singleton
        ).SetName("SimpleFactoryRegistrations_Singleton_LifetimeArgument");
    }

    private static IEnumerable<TestCaseData> ContextFactoryRegistrations()
    {
        yield return new TestCaseData(
            (Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.RegisterTransient(factory: factory, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Transient
        ).SetName("ContextFactoryRegistrations_Transient_LifetimeMethod");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.Register(factory: factory, lifetime: Lifetime.Transient, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Transient
        ).SetName("ContextFactoryRegistrations_Transient_LifetimeArgument");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.RegisterScoped(factory: factory, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Scoped
        ).SetName("ContextFactoryRegistrations_Scoped_LifetimeMethod");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.Register(factory: factory, lifetime: Lifetime.Scoped, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Scoped
        ).SetName("ContextFactoryRegistrations_Scoped_LifetimeArgument");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.RegisterSingleton(factory: factory, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Singleton
        ).SetName("ContextFactoryRegistrations_Singleton_LifetimeMethod");

        yield return new TestCaseData(
            (Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>>)((registrar, factory, tag, cleanup) =>
            {
                registrar.Register(factory: factory, lifetime: Lifetime.Singleton, tag: tag, cleanup: cleanup);
            }),
            Lifetime.Singleton
        ).SetName("ContextFactoryRegistrations_Singleton_LifetimeArgument");
    }

    [TestCaseSource(nameof(SimpleFactoryRegistrations))]
    public void SimpleFactoryConvenienceApi_ShouldPreserveFactoryTagAndCleanup(
        Action<IDependencyRegistrar, Func<int>, object?, Action<int>> register,
        Lifetime expectedLifetime
    )
    {
        var registrar = new RecordingRegistrar();
        var calls = 0;
        Func<int> factory = () => ++calls;
        var tag = new object();
        Action<int> cleanup = _ => { };

        register(registrar, factory, tag, cleanup);

        Assert.That(registrar.Source, Is.TypeOf<RegistrationSource.Factory<int>>());

        var source = (RegistrationSource.Factory<int>)registrar.Source;

        Assert.That(source.Value, Is.SameAs(factory));
        Assert.That(registrar.Lifetime, Is.EqualTo(expectedLifetime));
        Assert.That(registrar.Tag, Is.SameAs(tag));
        Assert.That(registrar.Cleanup, Is.SameAs(cleanup));
        Assert.That(calls, Is.Zero);
    }

    [TestCaseSource(nameof(ContextFactoryRegistrations))]
    public void ContextFactoryConvenienceApi_ShouldPreserveFactoryTagAndCleanup(
        Action<IDependencyRegistrar, DependencyFactory<int>, object?, Action<int>> register,
        Lifetime expectedLifetime
    )
    {
        var registrar = new RecordingRegistrar();
        var calls = 0;
        DependencyFactory<int> factory = (_, ref _) => ++calls;
        var tag = new object();
        Action<int> cleanup = _ => { };

        register(registrar, factory, tag, cleanup);

        Assert.That(registrar.Source, Is.TypeOf<RegistrationSource.ContextFactory<int>>());

        var source = (RegistrationSource.ContextFactory<int>)registrar.Source;

        Assert.That(source.Value, Is.SameAs(factory));
        Assert.That(registrar.Lifetime, Is.EqualTo(expectedLifetime));
        Assert.That(registrar.Tag, Is.SameAs(tag));
        Assert.That(registrar.Cleanup, Is.SameAs(cleanup));
        Assert.That(calls, Is.Zero);
    }

    [Test]
    public void InstanceConvenienceApi_ShouldPreserveInstanceTagAndCleanup()
    {
        var registrar = new RecordingRegistrar();
        var instance = new object();
        var tag = new object();
        Action<object> cleanup = _ => { };

        registrar.RegisterInstance(instance: instance, tag, cleanup);

        Assert.That(registrar.Source, Is.TypeOf<RegistrationSource.Instance<object>>());

        var source = (RegistrationSource.Instance<object>)registrar.Source;

        Assert.That(source.Value, Is.SameAs(instance));
        Assert.That(registrar.Lifetime, Is.EqualTo(Lifetime.Singleton));
        Assert.That(registrar.Tag, Is.SameAs(tag));
        Assert.That(registrar.Cleanup, Is.SameAs(cleanup));
    }

    [Test]
    public void DelegateInstance_ShouldRemainAnInstance_WithoutExecutingIt()
    {
        var calls = 0;
        Func<int> instance = () => ++calls;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.RegisterInstance(instance);
        });

        Assert.That(container.Resolve<Func<int>>(), Is.SameAs(instance));
        Assert.That(calls, Is.Zero);
    }

    [TestCase(Lifetime.Transient)]
    [TestCase(Lifetime.Scoped)]
    [TestCase(Lifetime.Singleton)]
    public void FactorySourceWithLifetime_ShouldForwardTheOriginalSource(Lifetime lifetime)
    {
        var registrar = new RecordingRegistrar();
        var source = new RegistrationSource.Factory<int>(() => 42);

        registrar.Register(source, lifetime);

        Assert.That(registrar.Source, Is.SameAs(source));
        Assert.That(registrar.Lifetime, Is.EqualTo(lifetime));
        Assert.That(registrar.Tag, Is.Null);
        Assert.That(registrar.Cleanup, Is.Null);
    }

    [TestCase(Lifetime.Transient)]
    [TestCase(Lifetime.Scoped)]
    [TestCase(Lifetime.Singleton)]
    public void ContextFactorySourceWithLifetime_ShouldForwardTheOriginalSource(Lifetime lifetime)
    {
        var registrar = new RecordingRegistrar();
        var source = new RegistrationSource.ContextFactory<int>((_, ref _) => 42);

        registrar.Register(source, lifetime);

        Assert.That(registrar.Source, Is.SameAs(source));
        Assert.That(registrar.Lifetime, Is.EqualTo(lifetime));
        Assert.That(registrar.Tag, Is.Null);
        Assert.That(registrar.Cleanup, Is.Null);
    }

    [Test]
    public void InvalidLifetime_ShouldRejectBothFactorySources_WithoutRegistering()
    {
        var registrar = new RecordingRegistrar();
        var factory = new RegistrationSource.Factory<int>(() => 42);
        var contextFactory = new RegistrationSource.ContextFactory<int>((_, ref _) => 42);
        var invalidLifetime = (Lifetime)int.MaxValue;

        Assert.That(() => registrar.Register(factory, invalidLifetime), Throws.InvalidOperationException);
        Assert.That(() => registrar.Register(contextFactory, invalidLifetime), Throws.InvalidOperationException);
        Assert.That(registrar.Source, Is.Null);
    }
}
