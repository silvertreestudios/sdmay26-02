using System;

namespace Game.Rules.Runtime
{
    /// <summary>
    /// Identifies an open PF2e skill by its stable data slug.
    /// </summary>
    /// <remarks>
    /// The predefined fields cover the standard skills, while <see cref="FromName"/> and
    /// <see cref="FromSlug"/> allow Lore skills and project-defined skills without changing code.
    /// </remarks>
    public readonly struct Skill : IEquatable<Skill>
    {
        /// <summary>Acrobatics.</summary>
        public static readonly Skill Acrobatics = FromSlug("acrobatics");

        /// <summary>Arcana.</summary>
        public static readonly Skill Arcana = FromSlug("arcana");

        /// <summary>Athletics.</summary>
        public static readonly Skill Athletics = FromSlug("athletics");

        /// <summary>Crafting.</summary>
        public static readonly Skill Crafting = FromSlug("crafting");

        /// <summary>Deception.</summary>
        public static readonly Skill Deception = FromSlug("deception");

        /// <summary>Diplomacy.</summary>
        public static readonly Skill Diplomacy = FromSlug("diplomacy");

        /// <summary>Intimidation.</summary>
        public static readonly Skill Intimidation = FromSlug("intimidation");

        /// <summary>Medicine.</summary>
        public static readonly Skill Medicine = FromSlug("medicine");

        /// <summary>Nature.</summary>
        public static readonly Skill Nature = FromSlug("nature");

        /// <summary>Occultism.</summary>
        public static readonly Skill Occultism = FromSlug("occultism");

        /// <summary>Performance.</summary>
        public static readonly Skill Performance = FromSlug("performance");

        /// <summary>Religion.</summary>
        public static readonly Skill Religion = FromSlug("religion");

        /// <summary>Society.</summary>
        public static readonly Skill Society = FromSlug("society");

        /// <summary>Stealth.</summary>
        public static readonly Skill Stealth = FromSlug("stealth");

        /// <summary>Survival.</summary>
        public static readonly Skill Survival = FromSlug("survival");

        /// <summary>Thievery.</summary>
        public static readonly Skill Thievery = FromSlug("thievery");

        /// <summary>Gets the normalized stable skill identity.</summary>
        public string Slug { get; }

        /// <summary>Gets whether this is the uninitialized default value.</summary>
        public bool IsEmpty => string.IsNullOrEmpty(Slug);

        private Skill(string slug)
        {
            Slug = StableId.Require(slug, nameof(slug));
        }

        /// <summary>
        /// Creates a skill by normalizing a display name such as <c>Sailing Lore</c>.
        /// </summary>
        /// <param name="value">The non-empty skill name.</param>
        /// <returns>The normalized open skill identity.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> does not contain a usable skill identity.
        /// </exception>
        public static Skill FromName(string value) => FromSlug(Pf2eSlug.FromName(value));

        /// <summary>
        /// Creates a skill from a stable data slug.
        /// </summary>
        /// <param name="value">The non-empty skill slug.</param>
        /// <returns>The normalized open skill identity.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="value"/> does not contain a usable skill identity.
        /// </exception>
        public static Skill FromSlug(string value) => new Skill(Pf2eSlug.FromName(value));

        /// <inheritdoc/>
        public bool Equals(Skill other) =>
            string.Equals(Slug, other.Slug, StringComparison.Ordinal);

        /// <inheritdoc/>
        public override bool Equals(object obj) => obj is Skill other && Equals(other);

        /// <inheritdoc/>
        public override int GetHashCode() =>
            StringComparer.Ordinal.GetHashCode(Slug ?? string.Empty);

        /// <inheritdoc/>
        public override string ToString() => Slug ?? string.Empty;

        /// <summary>Compares two skills by normalized identity.</summary>
        public static bool operator ==(Skill left, Skill right) => left.Equals(right);

        /// <summary>Compares two skills by normalized identity.</summary>
        public static bool operator !=(Skill left, Skill right) => !left.Equals(right);
    }
}
