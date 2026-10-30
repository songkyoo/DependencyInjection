// ReSharper disable AccessToDisposedClosure
namespace Macaron.DependencyInjection.Tests;

public class DependencyContainerTagTests
{
    private sealed class Service;

    private sealed class OtherService;

    private sealed record ValueTag(string Value);

    private sealed class ReferenceTag
    {
        public override string ToString() => "service";
    }

    private enum ServiceTag
    {
        Main = 1
    }

    private static IEnumerable<TestCaseData> EqualTags()
    {
        foreach (var lifetime in Enum.GetValues<Lifetime>())
        {
            yield return new TestCaseData(null, null, lifetime);
            yield return new TestCaseData("service", new string("service".ToCharArray()), lifetime);
            yield return new TestCaseData(42, 42, lifetime);
            yield return new TestCaseData(ServiceTag.Main, ServiceTag.Main, lifetime);
            yield return new TestCaseData(typeof(Service), typeof(Service), lifetime);
            yield return new TestCaseData(new ValueTag("service"), new ValueTag("service"), lifetime);
        }
    }

    [TestCaseSource(nameof(EqualTags))]
    public void EqualTags_ShouldShareRegistrationAndCache(object? registeredTag, object? lookupTag, Lifetime lifetime)
    {
        var creations = 0;
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(() =>
            {
                creations++;

                return new Service();
            }, lifetime, tag: registeredTag);
        });
        using var scope = container.CreateLocalScope();
        var first = container.Resolve<Service>(registeredTag);
        var second = container.Resolve<Service>(lookupTag);
        var third = scope.Resolve<Service>(lookupTag);

        Assert.That(container.Contains<Service>(lookupTag), Is.True);
        Assert.That(scope.Contains<Service>(lookupTag), Is.True);
        Assert.That(second, lifetime == Lifetime.Transient ? Is.Not.SameAs(first) : Is.SameAs(first));
        Assert.That(third, lifetime == Lifetime.Transient ? Is.Not.SameAs(second) : Is.SameAs(first));
        Assert.That(creations, Is.EqualTo(lifetime == Lifetime.Transient ? 3 : 1));
    }

    [TestCaseSource(nameof(EqualTags))]
    public void EqualTagsRegisteredTwice_ShouldFailBeforeFactoriesRun(
        object? firstTag,
        object? secondTag,
        Lifetime firstLifetime
    )
    {
        foreach (var secondLifetime in Enum.GetValues<Lifetime>())
        {
            var factoryCalls = 0;
            var error = Assert.Throws<ArgumentException>(() => DependencyContainerFactory.Create(registrar =>
            {
                registrar.Register(CreateService, firstLifetime, tag: firstTag);
                registrar.Register(CreateService, secondLifetime, tag: secondTag);
            }));

            Assert.That(error!.ParamName, Is.EqualTo("key"));
            Assert.That(error.Message, Does.Contain(typeof(Service).ToString()));
            Assert.That(error.Message, Does.Contain(secondTag?.ToString() ?? "null"));
            Assert.That(factoryCalls, Is.Zero);

            Service CreateService()
            {
                factoryCalls++;

                return new Service();
            }
        }
    }

    [Test]
    public void OmittedTag_ShouldConflictWithExplicitNullTag([Values] Lifetime lifetime)
    {
        Assert.That(
            () => DependencyContainerFactory.Create(registrar =>
            {
                registrar.Register<Service>(factory: () => new Service(), lifetime);
                registrar.Register<Service>(factory: () => new Service(), lifetime, tag: null);
            }),
            Throws.ArgumentException
        );
    }

    [Test]
    public void UnequalTags_ShouldKeepIndependentRegistrationsAndCaches([Values] Lifetime lifetime)
    {
        object?[] tags = [null, "", "service", "SERVICE", 1, 1L, ServiceTag.Main, new ReferenceTag(), new ReferenceTag()];
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            foreach (var tag in tags)
            {
                registrar.Register<Service>(factory: () => new Service(), lifetime, tag: tag);
            }
        });
        var instances = tags.Select(tag => container.Resolve<Service>(tag)).ToArray();

        Assert.That(instances.Distinct().Count(), Is.EqualTo(tags.Length));

        for (var index = 0; index < tags.Length; index++)
        {
            Assert.That(container.Contains<Service>(tags[index]), Is.True);
            Assert.That(container.Resolve<Service>(tags[index]), lifetime == Lifetime.Transient
                ? Is.Not.SameAs(instances[index])
                : Is.SameAs(instances[index]));
        }
    }

    [Test]
    public void SameTagForDifferentServiceTypes_ShouldKeepSeparateRegistrations([Values] Lifetime lifetime)
    {
        using var container = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register<Service>(factory: () => new Service(), lifetime, tag: "service");
            registrar.Register<OtherService>(factory: () => new OtherService(), lifetime, tag: "service");
        });

        Assert.That(container.Resolve<Service>("service"), Is.TypeOf<Service>());
        Assert.That(container.Resolve<OtherService>("service"), Is.TypeOf<OtherService>());
        Assert.That(container.Contains<object>("service"), Is.False);
    }

    [Test]
    public void EqualChildTag_ShouldMaskParentRegistrationAndInitialization(
        [Values(Lifetime.Scoped, Lifetime.Singleton)] Lifetime parentLifetime,
        [Values] Lifetime childLifetime
    )
    {
        var parentTag = new ValueTag("service");
        var childTag = new ValueTag("service");
        var parentCreations = 0;
        var childCreations = 0;
        using var root = DependencyContainerFactory.Create(registrar =>
        {
            registrar.Register(() =>
            {
                parentCreations++;

                return new Service();
            }, parentLifetime, tag: parentTag);
        });
        using var child = root.CreateChild(registrar =>
        {
            registrar.Register(() =>
            {
                childCreations++;

                return new Service();
            }, childLifetime, tag: childTag);
        });

        Assert.That(parentCreations, Is.EqualTo(1));
        Assert.That(childCreations, Is.EqualTo(childLifetime == Lifetime.Transient ? 0 : 1));
        Assert.That(child.Contains<Service>(parentTag), Is.True);
        Assert.That(child.Resolve<Service>(parentTag), Is.Not.SameAs(root.Resolve<Service>(childTag)));
        Assert.That(childCreations, Is.EqualTo(1));
    }
}
