namespace Macaron.DependencyInjection.Tests;

public class KeyTests
{
    [Test]
    public void Default_ShouldReturnSameKeyForSameServiceType()
    {
        var first = Key<string>.Default;
        var second = Key<string>.Default;

        Assert.That(first, Is.SameAs(second));
        Assert.That(first.Name, Is.Empty);
        Assert.That((object)Key<int>.Default, Is.Not.SameAs(first));
    }

    [Test]
    public void Keys_WithSameName_ShouldHaveReferenceEquality()
    {
        var first = new Key<string>("service");
        var second = new Key<string>("service");
        var shared = first;

        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(first == second, Is.False);
        Assert.That(first.Equals(shared), Is.True);
        Assert.That(first == shared, Is.True);
    }

    [Test]
    public void Keys_WithSameTypeAndName_ShouldHaveDifferentIds()
    {
        var first = new Key<string>("service");
        var second = new Key<string>("service");

        Assert.That(first.Id, Is.Not.EqualTo(second.Id));
    }

    [Test]
    public void UnnamedKeys_ShouldBeDistinctFromEachOtherAndDefaultKey()
    {
        var first = new Key<string>();
        var second = new Key<string>();

        Assert.That(first.Name, Is.Empty);
        Assert.That(first, Is.Not.EqualTo(second));
        Assert.That(first, Is.Not.EqualTo(Key<string>.Default));
    }

    [TestCase("")]
    [TestCase("Application")]
    public void ToString_ShouldDescribeServiceTypeOptionalNameAndId(string name)
    {
        var key = new Key<DateTime>(name);
        var expected = name.Length == 0
            ? $"Key {{ Type = {typeof(DateTime)}, Id = {key.Id} }}"
            : $"Key {{ Type = {typeof(DateTime)}, Name = {name}, Id = {key.Id} }}";

        Assert.That(key.Name, Is.EqualTo(name));
        Assert.That(key.ToString(), Is.EqualTo(expected));
    }
}
