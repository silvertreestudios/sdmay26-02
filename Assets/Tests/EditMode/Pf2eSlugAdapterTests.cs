using NUnit.Framework;

namespace Game.Creature.Rules.Tests
{
    public sealed class Pf2eSlugAdapterTests
    {
        [TestCase("Dragon's Rage!", "dragons-rage")]
        [TestCase("  Multiple   Spaces ", "multiple-spaces")]
        [TestCase(null, "")]
        public void LegacyAdapterMatchesCanonicalRuntimeNormalization(string value, string expected)
        {
            Assert.That(Pf2eSlug.FromName(value), Is.EqualTo(expected));
            Assert.That(Pf2eSlug.FromName(value), Is.EqualTo(Game.Rules.Runtime.Pf2eSlug.FromName(value)));
        }
    }
}
