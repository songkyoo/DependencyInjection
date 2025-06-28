namespace Macaron.DependencyInjection.Tests;

public class KeyTests
{
    [Test]
    public void Keys_WithSameTypeAndTag_ShouldBeEqual()
    {
        var key1 = Key.Of<string>("tag");
        var key2 = Key.Of<string>("tag");

        Assert.That(key1, Is.EqualTo(key2));
        Assert.That(((IKey)key1).Equals(key2), Is.True);
        Assert.That(key1.GetHashCode(), Is.EqualTo(key2.GetHashCode()));
    }

    [Test]
    public void Keys_WithDifferentTags_ShouldNotBeEqual()
    {
        var key1 = Key.Of<int>("a");
        var key2 = Key.Of<int>("b");

        Assert.That(key1, Is.Not.EqualTo(key2));
        Assert.That(((IKey)key1).Equals(key2), Is.False);
    }

    [Test]
    public void Keys_WithDifferentTypes_ShouldNotBeEqual()
    {
        var key1 = Key.Of<int>("tag");
        var key2 = Key.Of<float>("tag");

        Assert.That(key1, Is.Not.EqualTo((IKey)key2));
        Assert.That(((IKey)key1).Equals(key2), Is.False);
    }

    [Test]
    public void Key_OfWithoutTag_ShouldReturnCachedInstance()
    {
        var key1 = Key.Of<Guid>();
        var key2 = Key.Of<Guid>();

        Assert.That(key1, Is.SameAs(key2));
        Assert.That(key1, Is.EqualTo(key2));
    }

    [Test]
    public void ToString_ShouldContainTypeAndTag()
    {
        var key = Key.Of<DateTime>("MyTag");
        var str = key.ToString();

        Assert.That(str, Does.Contain("DateTime"));
        Assert.That(str, Does.Contain("MyTag"));
    }

    [Test]
    public void Equals_WithNull_ShouldReturnFalse()
    {
        var key = Key.Of<string>("x");

        Assert.That(key, Is.Not.EqualTo(null));
    }
}
