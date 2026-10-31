// 금지된 타입을 런타임 검증에 전달하기 위해 notnull 제약 경고를 허용한다.
#pragma warning disable CS8714

namespace Macaron.DependencyInjection.Tests;

public class NullableServiceRegistrationTests
{
    public delegate void RegisterNullable(
        IDependencyRegistrar registrar,
        Func<int?> factory,
        Action<int?> cleanup,
        object? tag
    );

    private static IEnumerable<TestCaseData> FactoryRegistrations()
    {
        var registrations = new (string Name, RegisterNullable Register)[]
        {
            ("RawTransient", (registrar, factory, cleanup, tag) =>
                registrar.RegisterTransient((_, ref _) => factory(), tag, cleanup)),
            ("RawScoped", (registrar, factory, cleanup, tag) =>
                registrar.RegisterScoped((_, ref _) => factory(), tag, cleanup)),
            ("RawSingleton", (registrar, factory, cleanup, tag) =>
                registrar.RegisterSingleton((_, ref _) => factory(), tag, cleanup)),
            ("FuncTransient", (registrar, factory, cleanup, tag) =>
                registrar.RegisterTransient(factory, tag, cleanup)),
            ("FuncScoped", (registrar, factory, cleanup, tag) =>
                registrar.RegisterScoped(factory, tag, cleanup)),
            ("FuncSingleton", (registrar, factory, cleanup, tag) =>
                registrar.RegisterSingleton(factory, tag, cleanup))
        };

        foreach (var tag in new object?[] { null, "service" })
        {
            foreach (var (name, register) in registrations)
            {
                yield return new TestCaseData(register, tag)
                    .SetName($"NullableFactory_{name}_{tag ?? "DefaultTag"}");
            }

            foreach (var lifetime in Enum.GetValues<Lifetime>())
            {
                RegisterNullable rawRegistration = (registrar, factory, cleanup, currentTag) =>
                    registrar.Register((_, ref _) => factory(), lifetime, currentTag, cleanup);
                RegisterNullable funcRegistration = (registrar, factory, cleanup, currentTag) =>
                    registrar.Register(factory, lifetime, currentTag, cleanup);

                yield return new TestCaseData(rawRegistration, tag)
                    .SetName($"NullableFactory_RawRegister_{lifetime}_{tag ?? "DefaultTag"}");
                yield return new TestCaseData(funcRegistration, tag)
                    .SetName($"NullableFactory_FuncRegister_{lifetime}_{tag ?? "DefaultTag"}");
            }
        }
    }

    [TestCaseSource(nameof(FactoryRegistrations))]
    public void NullableFactory_ShouldFailDuringRegistrationWithoutInvokingCallbacks(
        RegisterNullable register,
        object? tag
    )
    {
        var factoryCalls = 0;
        var cleanupCalls = 0;

        using (var container = DependencyContainerFactory.Create(registrar =>
        {
            var error = Assert.Throws<ArgumentException>(() => register(
                registrar,
                factory: () =>
                {
                    factoryCalls++;

                    return 42;
                },
                cleanup: _ => cleanupCalls++,
                tag
            ));

            Assert.That(error!.Message, Does.Contain(typeof(int?).ToString()));

            registrar.RegisterInstance(instance: 7, tag);
        }))
        {
            Assert.That(container.Resolve<int>(tag), Is.EqualTo(7));
        }

        Assert.That(factoryCalls, Is.Zero);
        Assert.That(cleanupCalls, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(42)]
    public void NullableInstance_ShouldFailDuringRegistrationWithoutAddingCleanup(int value)
    {
        var cleanupCalls = 0;

        using (var container = DependencyContainerFactory.Create(registrar =>
        {
            var error = Assert.Throws<ArgumentException>(() =>
            {
                registrar.RegisterInstance<int?>(instance: value, cleanup: _ => cleanupCalls++);
            });

            Assert.That(error!.Message, Does.Contain(typeof(int?).ToString()));

            registrar.RegisterInstance(instance: value);
        }))
        {
            Assert.That(container.Resolve<int>(), Is.EqualTo(value));
        }

        Assert.That(cleanupCalls, Is.Zero);
    }

    [Test]
    public void NullNullableInstance_ShouldRetainNullInstanceValidation()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            var error = Assert.Throws<ArgumentNullException>(() =>
            {
                registrar.RegisterInstance<int?>(instance: null);
            });

            Assert.That(error!.ParamName, Is.EqualTo("instance"));
        });
    }

    [Test]
    public void GenericRegistrationHelper_ShouldRejectClosedNullableType()
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            Assert.That(() => RegisterInstance<int?>(registrar, 42), Throws.TypeOf<ArgumentException>());

            RegisterInstance(registrar, 7);
        });

        Assert.That(container.Resolve<int>(), Is.EqualTo(7));

        #region Local Functions
        static void RegisterInstance<T>(IDependencyRegistrar registrar, T value) where T : notnull
        {
            registrar.RegisterInstance(instance: value);
        }
        #endregion
    }

    [Test]
    public void ChildRegistration_ShouldRejectNullableTypeBeforeRunningFactory()
    {
        var factoryCalls = 0;
        using var parent = DependencyContainerFactory.Create(registrar => registrar.RegisterInstance(instance: 7));
        using var child = parent.CreateChild(registrar =>
        {
            Assert.That(
                () => registrar.RegisterScoped<int?>(factory: () =>
                {
                    factoryCalls++;

                    return 42;
                }),
                Throws.TypeOf<ArgumentException>()
            );
        });

        Assert.That(child.Resolve<int>(), Is.EqualTo(7));
        Assert.That(factoryCalls, Is.Zero);
    }
}
